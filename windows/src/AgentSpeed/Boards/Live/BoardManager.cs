using System.Windows.Threading;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Core;
using AgentSpeed.Interop;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Orb.Render;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Live;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Services;
using AgentSpeed.Taskbar;
using AgentSpeed.Theme;
using AgentSpeed.Titles;

namespace AgentSpeed.Boards.Live;

/// <summary>What the board manager needs from the application.</summary>
internal interface IBoardHost
{
    void ShowDashboardAt(POINT anchor);

    void ShowBoardMenu(string boardId);

    void OpenBoardSettings(string? boardId);
}

/// <summary>
/// The leaderboard boards (DESIGN 2c): one GPU window per visible board of the settings' <see cref="BoardsDocument"/>,
/// each fed from the engine snapshot for its sources (METRICS 6.8), placed per display arrangement clear of the orbs,
/// with its tooltip, click-to-expand, accessibility and the shared effects, appearance and title theme.
/// </summary>
internal sealed class BoardManager : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly OrbClock _clock;
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly SpeedModel _speed;
    private readonly IBoardHost _host;
    private readonly Func<IReadOnlyList<OrbDisc>> _orbs;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _deadline;
    private readonly DispatcherTimer _tipTimer;
    private Localizer _loc;
    private HoverTooltip? _tooltip;
    private (string Board, string Row)? _hover;
    private BoardsDocument? _known;
    private bool _started;
    private bool _locked;
    private bool _failed;
    private bool _disposed;
    private uint _seed = 1;

    /// <param name="orbs">The visible orbs' discs in physical pixels (boards keep clear of them).</param>
    public BoardManager(Dispatcher ui, OrbClock clock, SettingsService settings, ThemeService theme, SpeedModel speed, Localizer loc, IBoardHost host,
        Func<IReadOnlyList<OrbDisc>> orbs)
    {
        _ui = ui;
        _clock = clock;
        _settings = settings;
        _theme = theme;
        _speed = speed;
        _loc = loc;
        _host = host;
        _orbs = orbs;
        _deadline = new DispatcherTimer(DispatcherPriority.Normal, ui);
        _deadline.Tick += (_, _) =>
        {
            _deadline.Stop();
            OnDeadline();
        };
        _tipTimer = new DispatcherTimer(TimeSpan.FromSeconds(BoardTooltip.DelaySeconds), DispatcherPriority.Normal, (_, _) => ShowTip(), ui);
        _tipTimer.Stop();
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        GpuOrbSurface.RendererFailed += OnRendererFailed;
        CompositorTicker.DisplayStateChanged += OnDisplayStateChanged;
    }

    /// <summary>The General title theme names the boards' titles; without it they show the Vehicles titles.</summary>
    public TitleThemesService? Titles { get; init; }

    /// <summary>Formats today's cost in the footers (DESIGN 9); without it the footers show no cost.</summary>
    public CostDisplay? Cost { get; init; }

    private AppSettings Settings => _settings.Current;

    /// <summary>The shown boards' squares (physical pixels), the obstacles of the other windows' placement.</summary>
    public IReadOnlyDictionary<string, BoardObstacle> Squares =>
        _entries.Where(e => e.Value.Window.IsVisible).ToDictionary(e => e.Key,
            e => BoardObstacle.Square(e.Value.Window.Center, BoardPlacement.HalfSide(e.Value.Definition.Size, e.Value.Window.DisplayScale)),
            StringComparer.Ordinal);

    /// <summary>Synchronizes the windows with the settings (document, theme, effects, metric, language).</summary>
    public void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var settings = Settings;
        var document = settings.Boards;
        var previous = _known;
        _known = document;
        var visible = _failed || GpuDevice.Shared is null ? [] : document.Boards.Where(b => b.Visible).ToArray();
        var ids = visible.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _entries.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            Close(id);
        }

        var created = new List<string>();
        var look = new BoardLook(_theme.IsDark, _theme.HighContrast, _loc.LanguageCode);
        foreach (var board in visible)
        {
            var redline = settings.RedlineFor(settings.Metric);
            if (!_entries.TryGetValue(board.Id, out var entry))
            {
                var isNew = _started && previous is not null && previous.Find(board.Id) is null;
                entry = new Entry(board, new BoardLiveFeed(board));
                entry.Feed.Configure(board, settings.Metric, redline, NowMs(), isNew);
                Open(entry, look);
                _entries[board.Id] = entry;
                created.Add(board.Id);
            }
            else
            {
                entry.Feed.Configure(board, settings.Metric, redline, NowMs());
                if (entry.Look != look || entry.Definition.Style != board.Style)
                {
                    Reopen(entry, look, board.Style);
                }
            }

            entry.Definition = board;
            Configure(entry);
        }

        Position(created);
        foreach (var id in created)
        {
            _entries[id].Window.Show();
        }

        _started = true;
        Refresh(relayout: true);
    }

    /// <summary>Applies an engine snapshot to every board.</summary>
    public void Update(SpeedSnapshot snapshot)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var now = _speed.Now;
        var texts = Texts();
        foreach (var entry in _entries.Values)
        {
            entry.Expansion.Expire(now);
            Push(entry, entry.Feed.Update(snapshot, now, texts, entry.Expansion.Current(now)), texts, now);
        }

        Schedule();
    }

    /// <summary>The engine's all-sources new peak: every board tracking all sources celebrates it.</summary>
    public void OnEvent(SpeedEvent speedEvent)
    {
        var texts = Texts();
        foreach (var entry in _entries.Values)
        {
            if (entry.Feed.EnginePeak(speedEvent) is { } tick)
            {
                Push(entry, tick, texts, _speed.Now);
            }
        }
    }

    public void Relocalize(Localizer loc)
    {
        _loc = loc;
        Apply();
    }

    public void OnDisplayChanged()
    {
        _clock.RefreshDisplay();
        Position([]);
    }

    /// <summary>
    /// <c>boards.add</c>: a new board with the last used style, M and all sources, next to <paramref name="origin"/> (the
    /// window it came from) clear of every orb and board; nothing at the limit.
    /// </summary>
    public void Add(BoardObstacle? origin)
    {
        if (_disposed || Settings.Boards.Add() is not { } added)
        {
            return;
        }

        var others = new List<BoardObstacle>(_orbs().Select(o => BoardObstacle.Disc(o.Center, o.Radius)));
        others.AddRange(Squares.Values);
        var placed = BoardArrangement.PlaceNew(added.Document, Displays(), added.Board.Id, origin, others);
        _settings.Update(s => s with { Boards = placed.Document });
    }

    /// <summary>The square of a shown board (the origin of a board added from its menu).</summary>
    public BoardObstacle? SquareOf(string boardId) => Squares.TryGetValue(boardId, out var square) ? square : null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        GpuOrbSurface.RendererFailed -= OnRendererFailed;
        CompositorTicker.DisplayStateChanged -= OnDisplayStateChanged;
        _deadline.Stop();
        _tipTimer.Stop();
        foreach (var id in _entries.Keys.ToArray())
        {
            Close(id);
        }

        _tooltip?.Close();
    }

    // ---- Windows ----

    /// <summary>The painter of a style's cached layers (DESIGN 2c Styles).</summary>
    private IBoardLayerPainter CreatePainter(BoardStyle style) => new PlaceholderBoardPainter(_loc);

    private void Open(Entry entry, BoardLook look)
    {
        var board = entry.Definition;
        var painter = CreatePainter(board.Style);
        var seed = _seed++ * 2654435761u | 1;
        var window = new GpuBoardWindow(board.Id, GpuDevice.Shared!, _clock, board.Style, board.Size, painter, seed);
        var id = board.Id;
        window.DashboardRequested += (_, _) => _host.ShowDashboardAt(new POINT((int)window.Center.X, (int)window.Center.Y));
        window.MenuRequested += (_, _) =>
        {
            HideTip();
            _host.ShowBoardMenu(id);
        };
        window.DragReleased += (_, snapped) => OnDragReleased(id, snapped);
        window.HoveredRowChanged += (_, key) => OnHover(id, key);
        window.RowClicked += (_, key) => OnRowClicked(id, key);
        window.Surface.Locked = _locked || CompositorTicker.DisplayOff;
        entry.Window = window;
        entry.Painter = painter;
        entry.Look = look;
        entry.Config = null;
        SetActions(entry);
    }

    /// <summary>A new style or appearance needs a new painter: the window is rebuilt where it was.</summary>
    private void Reopen(Entry entry, BoardLook look, BoardStyle style)
    {
        var center = entry.Window.Center;
        var shown = entry.Window.IsVisible;
        CloseWindow(entry);
        entry.Definition = entry.Definition with { Style = style };
        Open(entry, look);
        entry.Window.MoveTo(center);
        if (shown)
        {
            entry.Window.Show();
        }

        if (entry.Feed.Last is { } last)
        {
            entry.Window.Apply(last);
        }
    }

    private void Configure(Entry entry)
    {
        var settings = Settings;
        var board = entry.Definition;
        var config = (Style: board.Style, Size: board.Size,
            Policy: new BoardEffectsPolicy(settings.Effects, _theme.ReduceMotion, _theme.HighContrast, settings.CelebratePeaks),
            Options: new BoardWindowOptions(board.AlwaysOnTop, board.ClickThrough, board.Opacity, _theme.ReduceMotion));
        if (entry.Config == config)
        {
            return;
        }

        entry.Config = config;
        entry.Window.Configure(config.Style, config.Size, config.Policy, config.Options);
    }

    private void SetActions(Entry entry)
    {
        var id = entry.Definition.Id;
        var window = entry.Window;
        window.Automation.SetActions(
        [
            (_loc["menu.dashboard"], () => _host.ShowDashboardAt(new POINT((int)window.Center.X, (int)window.Center.Y))),
            (_loc["a11y.orb.showMenu"], () => _host.ShowBoardMenu(id)),
            (_loc["boards.settings"], () => _host.OpenBoardSettings(id)),
        ]);
    }

    private void Close(string id)
    {
        if (!_entries.Remove(id, out var entry))
        {
            return;
        }

        if (_hover?.Board == id)
        {
            HideTip();
        }

        CloseWindow(entry);
    }

    private static void CloseWindow(Entry entry)
    {
        entry.Window.Dispose();
        (entry.Painter as IDisposable)?.Dispose();
    }

    // ---- Data ----

    private BoardTexts Texts() => new(_loc, Titles?.General ?? TitleTheme.Vehicles, Cost);

    /// <summary>Lays every board out again (style, size, language, expansion) without new data.</summary>
    private void Refresh(bool relayout)
    {
        var now = _speed.Now;
        var texts = Texts();
        foreach (var entry in _entries.Values)
        {
            if (entry.Feed.Last is null)
            {
                Push(entry, entry.Feed.Update(_speed.Snapshot, now, texts, entry.Expansion.Current(now)), texts, now);
            }
            else if (relayout && entry.Feed.Relayout(texts, entry.Expansion.Current(now)) is { } tick)
            {
                Push(entry, tick, texts, now);
            }
        }

        Schedule();
    }

    private void Push(Entry entry, BoardTick tick, BoardTexts texts, double now)
    {
        entry.Window.Apply(tick);
        var automation = entry.Window.Automation;
        automation.Name = BoardAccessibility.Board(entry.Definition, tick.ValueText, texts.SpokenTitle(entry.Feed.Data.Title), _loc);
        automation.SetRows([.. BoardAccessibility.Items(tick).Select(e => (e.Key, BoardAccessibility.Row(e, _loc)))]);
        if (entry.Announcer.NewLeader(tick, now, _loc) is { } text)
        {
            automation.Announce(text);
        }

        if (_hover is { } hover && hover.Board == entry.Definition.Id && _tooltip is { IsVisible: true })
        {
            ShowTip();
        }
    }

    private void OnDeadline()
    {
        var now = _speed.Now;
        var texts = Texts();
        foreach (var entry in _entries.Values)
        {
            var expired = entry.Expansion.Expire(now);
            var tick = entry.Feed.Tick(now, texts, entry.Expansion.Current(now));
            if (tick is null && expired)
            {
                tick = entry.Feed.Relayout(texts, null);
            }

            if (tick is not null)
            {
                Push(entry, tick, texts, now);
            }
        }

        Schedule();
    }

    private void Schedule()
    {
        double? next = null;
        foreach (var entry in _entries.Values)
        {
            foreach (var at in new[] { entry.Feed.NextDeadline, entry.Expansion.Deadline })
            {
                if (at is { } value && (next is null || value < next))
                {
                    next = value;
                }
            }
        }

        _deadline.Stop();
        if (next is { } deadline)
        {
            _deadline.Interval = TimeSpan.FromSeconds(Math.Max(0.02, deadline - _speed.Now + 0.01));
            _deadline.Start();
        }
    }

    // ---- Interaction ----

    private void OnRowClicked(string id, string key)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        var now = _speed.Now;
        var texts = Texts();
        entry.Expansion.Toggle(key, now);
        if (entry.Feed.Relayout(texts, entry.Expansion.Current(now)) is { } tick)
        {
            Push(entry, tick, texts, now);
        }

        Schedule();
    }

    private void OnHover(string id, string? key)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            entry.Expansion.Touch(_speed.Now);
        }

        _tipTimer.Stop();
        if (key is null)
        {
            if (_hover?.Board == id)
            {
                HideTip();
            }

            return;
        }

        _hover = (id, key);
        if (_tooltip is { IsVisible: true })
        {
            ShowTip();
        }
        else
        {
            _tipTimer.Start();
        }
    }

    private void ShowTip()
    {
        _tipTimer.Stop();
        if (_hover is not { } hover || !_entries.TryGetValue(hover.Board, out var entry)
            || entry.Feed.Last?.Entries.FirstOrDefault(e => e.Kind == BoardEntryKind.Group && e.Key == hover.Row) is not { } row
            || entry.Window.RowRect(hover.Row) is not { } anchor)
        {
            HideTip();
            return;
        }

        var lines = BoardTooltip.Lines(row.Group, _loc);
        _tooltip ??= new HoverTooltip();
        _tooltip.ShowFor(anchor, lines[0], string.Join(Environment.NewLine, lines.Skip(1)), _theme.ReduceMotion);
    }

    private void HideTip()
    {
        _tipTimer.Stop();
        _hover = null;
        _tooltip?.HideTip();
    }

    // ---- Positions ----

    private static IReadOnlyList<OrbDisplay> Displays() =>
        Monitors.All().Select(m => new OrbDisplay(m.Key, m.WorkRect, m.Scale, m.IsPrimary)).ToArray();

    private void Position(IReadOnlyCollection<string> fresh)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var result = BoardArrangement.Resolve(Settings.Boards, Displays(), _orbs());
        foreach (var (id, location) in result.Locations)
        {
            if (_entries.TryGetValue(id, out var entry) && !entry.Window.IsDragging
                && (fresh.Contains(id) || OrbGeometry.Distance(entry.Window.Center, location.Center) > 0.5))
            {
                entry.Window.MoveTo(location.Center);
            }
        }

        if (!result.Document.Equals(Settings.Boards))
        {
            _settings.Update(s => s with { Boards = result.Document });
        }
    }

    private void OnDragReleased(string id, PointD snapped)
    {
        HideTip();
        if (!_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        var others = new List<BoardObstacle>(_orbs().Select(o => BoardObstacle.Disc(o.Center, o.Radius)));
        others.AddRange(Squares.Where(s => s.Key != id).Select(s => s.Value));
        var center = BoardArrangement.Release(entry.Definition, Displays(), others, snapped);
        if (OrbGeometry.Distance(center, snapped) > 0.5)
        {
            entry.Window.MoveTo(center);
        }

        _settings.Update(s => s with { Boards = BoardArrangement.Commit(s.Boards, Displays(), id, center) });
    }

    // ---- System state ----

    /// <summary>Rendering pauses while the session is locked (DESIGN 2a, Frame policy); data keeps flowing.</summary>
    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e) => _ui.BeginInvoke(() =>
    {
        _locked = e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
            || (_locked && e.Reason is not (Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect));
        OnDisplayStateChanged(this, EventArgs.Empty);
    });

    private void OnDisplayStateChanged(object? sender, EventArgs e)
    {
        foreach (var entry in _entries.Values)
        {
            entry.Window.Surface.Locked = _locked || CompositorTicker.DisplayOff;
        }
    }

    /// <summary>Boards draw only on the GPU: after a renderer failure they close (the orbs fall back to WPF).</summary>
    private void OnRendererFailed(object? sender, EventArgs e) => _ui.BeginInvoke(() =>
    {
        if (_failed || _disposed)
        {
            return;
        }

        App.AppLog.Warn("The GPU renderer failed; the leaderboard boards are closed until the next start.");
        _failed = true;
        Apply();
    });

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>What a board's painter depends on besides its style: appearance and language.</summary>
    private readonly record struct BoardLook(bool Dark, bool HighContrast, string Language);

    private sealed class Entry(BoardDefinition definition, BoardLiveFeed feed)
    {
        public BoardDefinition Definition { get; set; } = definition;

        public BoardLiveFeed Feed { get; } = feed;

        public BoardExpansion Expansion { get; } = new();

        public BoardAnnouncer Announcer { get; } = new();

        public GpuBoardWindow Window { get; set; } = null!;

        public IBoardLayerPainter? Painter { get; set; }

        public BoardLook Look { get; set; }

        public (BoardStyle Style, BoardSize Size, BoardEffectsPolicy Policy, BoardWindowOptions Options)? Config { get; set; }
    }
}
