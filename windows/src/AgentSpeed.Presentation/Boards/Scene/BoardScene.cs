using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// One board's picture, platform-neutral (DESIGN 2c, Two layers): each engine tick (<see cref="Apply"/>) sets the rows'
/// places and fires the effects of its events; each frame (<see cref="Render"/>) composites the cached layers — rows, the
/// hero and the footer, rasterized by the platform only when their content changes — at their animated places between the
/// effects behind and above them. The draw list is in canvas points (<see cref="BoardCanvas"/>); nothing allocates per frame.
/// </summary>
/// <remarks>
/// Layer keys: a row's body is its plan key and its value <c>key#v</c>; the hero alternates <see cref="HeroKeyA"/> and
/// <see cref="HeroKeyB"/> so a new value crossfades over the old one (<see cref="HeroKey"/> is the one to rasterize), and the
/// footer is <see cref="FooterKey"/>.
/// </remarks>
public sealed class BoardScene
{
    public const string HeroKeyA = "#hero.a";
    public const string HeroKeyB = "#hero.b";
    public const string FooterKey = "#footer";

    /// <summary>A rise of more than 8 % + 20 tok/s in one tick punches the number (DESIGN 2c Hero number).</summary>
    public const double PunchRatio = 0.08;
    public const double PunchFloor = 20;
    public const double PunchSeconds = 0.4;
    public const double HeroFadeSeconds = 0.24;

    private readonly Dictionary<string, BoardRowMotion> _rows = new(StringComparer.Ordinal);
    private readonly List<BoardRowMotion> _order = [];
    private BoardTick? _tick;
    private double _lastFrame = double.NaN;
    private double _heroChanged = double.NegativeInfinity;
    private double _punchStart = double.NegativeInfinity;
    private string _heroText = string.Empty;
    private bool _heroB;

    public BoardScene(BoardStyle style, BoardSize size, uint seed = 1)
    {
        Effects = new BoardEffects(style, size, seed);
        Configure(style, size, Effects.Policy);
    }

    public BoardEffects Effects { get; }

    public BoardStyle Style { get; private set; }

    public BoardSize Size { get; private set; }

    public BoardMetrics Metrics { get; private set; } = BoardMetrics.For(BoardStyle.Glass, BoardSize.M);

    public BoardCanvas Canvas => Effects.Canvas;

    public BoardTick? Tick => _tick;

    /// <summary>The hero layer the platform rasterizes for the current tick.</summary>
    public string HeroKey => _heroB ? HeroKeyB : HeroKeyA;

    /// <summary>The row whose tint hover lifts (DESIGN 2c: hover lifts the row tint only).</summary>
    public string? Hovered { get; set; }

    /// <summary>The hero number's box in board points (embers leave it); a heuristic until the style's hero is laid out.</summary>
    public (double Left, double Top, double Width, double Height) HeroNumber { get; set; }

    /// <summary>The rows in drawing order (the plan's order, then leaving rows).</summary>
    public IReadOnlyList<BoardRowMotion> Rows => _order;

    public void Configure(BoardStyle style, BoardSize size, BoardEffectsPolicy policy)
    {
        var restyle = style != Style;
        Style = style;
        Size = size;
        Metrics = BoardMetrics.For(style, size);
        Effects.Configure(style, size);
        Effects.Policy = policy;
        HeroNumber = (Metrics.Pad, Metrics.HeroTop + 18, Metrics.ValueSize * 2.8, Metrics.ValueSize);
        if (restyle)
        {
            foreach (var row in _order)
            {
                row.Restyle(style);
            }
        }
    }

    /// <summary>Applies an engine tick: rows move to their plan places, enter or leave, values flash, events fire effects.</summary>
    public void Apply(BoardTick tick, double now)
    {
        ArgumentNullException.ThrowIfNull(tick);
        var previous = _tick;
        _tick = tick;
        var reduce = Effects.Policy.ReduceMotion;
        foreach (var row in _order)
        {
            row.Leave(now);
        }

        var first = previous is null;
        var placed = new List<BoardRowMotion>(tick.Entries.Count);
        foreach (var entry in tick.Entries)
        {
            if (!_rows.TryGetValue(entry.Key, out var row))
            {
                row = new BoardRowMotion(entry.Key, Style, entry, now, appear: !first);
                _rows[entry.Key] = row;
            }

            row.Update(entry, tick.LiveOf(entry), now, reduce);
            placed.Add(row);
        }

        foreach (var row in _order)
        {
            if (row.Leaving)
            {
                placed.Add(row);
            }
        }

        _order.Clear();
        _order.AddRange(placed);
        if (first)
        {
            foreach (var row in _order)
            {
                row.Settle();
            }
        }

        if (!string.Equals(tick.ValueText, _heroText, StringComparison.Ordinal))
        {
            _heroText = tick.ValueText;
            _heroB = !_heroB;
            _heroChanged = first ? double.NegativeInfinity : now;
        }

        Effects.SetHeat(tick.Heat, tick.Overdrive, tick.Tier, tick.Generating);
        if (previous is not null)
        {
            Fire(previous, tick, now);
        }
    }

    /// <summary>The frame at <paramref name="now"/>: backdrop, effects behind, layers, effects above.</summary>
    public void Render(double now, IBoardLayers layers, BoardDrawList list)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(list);
        var dt = double.IsNaN(_lastFrame) ? 0 : Math.Clamp(now - _lastFrame, 0, BoardEffects.MaxStep);
        _lastFrame = now;
        Effects.Advance(now);
        var canvas = Canvas;
        var m = BoardCanvas.Margin;
        var metrics = Metrics;
        var reduce = Effects.Policy.ReduceMotion;
        list.Canvas = canvas.Extent;
        list.BoxHalf = canvas.Half;
        list.BoxRadius = canvas.Radius;

        BoardBackdrop.Emit(list, canvas, Style, Effects.Policy.HighContrast);
        Effects.EmitBack(list);

        var clip = RectBounds.From(m + metrics.ListLeft, m + metrics.ListTop, metrics.ListWidth, metrics.ListHeight);
        list.Clip = BoardClip.Inside;
        for (var i = _order.Count - 1; i >= 0; i--)
        {
            if (_order[i].Gone(now))
            {
                _rows.Remove(_order[i].Key);
                _order.RemoveAt(i);
            }
        }

        foreach (var row in _order)
        {
            row.Step(dt);
            var alpha = row.Alpha(now);
            if (alpha <= 0.002)
            {
                continue;
            }

            var top = m + metrics.ListTop + row.Y + row.EnterLift(now, reduce);
            var left = m + metrics.ListLeft;
            if (string.Equals(row.Key, Hovered, StringComparison.Ordinal) && row.Entry.Kind == BoardEntryKind.Group)
            {
                list.Add(OrbGpuInstance.RoundedRect(left + metrics.ListWidth / 2, top + row.Entry.Height / 2, metrics.ListWidth / 2,
                    row.Entry.Height / 2 - 1, 0, 9, Rgba.White.WithAlpha(0.06 * alpha)));
            }

            if (layers.TryGet(row.Key, out var body))
            {
                list.AddLayer(BoardLayerInstance.Create(body, left, top, alpha, clip: clip));
            }

            if (layers.TryGet(row.ValueKey, out var value))
            {
                var (tint, amount, dim) = Flash(row, now);
                list.AddLayer(BoardLayerInstance.Create(value, left + metrics.ListWidth - value.Width, top, alpha * dim, tint, amount, clip));
            }

            if (row.Entry.Kind == BoardEntryKind.Group && !row.Leaving)
            {
                Effects.FollowRow(row.Key, metrics.ListTop + row.Y + row.Entry.Height / 2);
            }
        }

        list.Clip = BoardClip.None;
        EmitHero(now, layers, list);
        if (layers.TryGet(FooterKey, out var footer))
        {
            list.AddLayer(BoardLayerInstance.Create(footer, m + metrics.Pad, m + metrics.FooterTop, 1));
        }

        Effects.EmitFront(list);
    }

    /// <summary>
    /// The frames the board needs (DESIGN 2c, Frame policy): the effects' class, raised to 60 fps while a row moves or fades,
    /// the hero's digits roll or it punches, and to 30 fps while a value flashes or dims.
    /// </summary>
    public FrameClass Demand(double now)
    {
        var demand = Effects.Demand;
        if (demand == FrameClass.Fps60)
        {
            return demand;
        }

        if (now - _heroChanged < HeroFadeSeconds || now - _punchStart < PunchSeconds)
        {
            return FrameClass.Fps60;
        }

        var flashing = false;
        foreach (var row in _order)
        {
            if (row.Moving(now))
            {
                return FrameClass.Fps60;
            }

            flashing |= row.Flashing(now);
        }

        return flashing ? FramePolicy.Max(demand, FrameClass.Fps30) : demand;
    }

    /// <summary>The group row under a canvas point, for hover, tooltips and clicks.</summary>
    public BoardRowMotion? HitRow(double x, double y)
    {
        var metrics = Metrics;
        var m = BoardCanvas.Margin;
        if (x < m + metrics.ListLeft || x > m + metrics.ListLeft + metrics.ListWidth || y < m + metrics.ListTop ||
            y > m + metrics.ListTop + metrics.ListHeight)
        {
            return null;
        }

        foreach (var row in _order)
        {
            var top = m + metrics.ListTop + row.Y;
            if (!row.Leaving && y >= top && y < top + row.Entry.Height)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>Ends every motion at once (the window appears, Reduce Motion turns on).</summary>
    public void Settle()
    {
        foreach (var row in _order)
        {
            row.Settle();
        }

        _heroChanged = double.NegativeInfinity;
        _punchStart = double.NegativeInfinity;
        _lastFrame = double.NaN;
        Effects.Clear();
        Effects.Resume();
    }

    private void EmitHero(double now, IBoardLayers layers, BoardDrawList list)
    {
        var m = BoardCanvas.Margin;
        var x = m + Metrics.Pad;
        var y = m + Metrics.HeroTop;
        var fade = Easing.OutCubic(Math.Clamp((now - _heroChanged) / HeroFadeSeconds, 0, 1));
        var punch = PunchScale(now);
        var oldKey = _heroB ? HeroKeyA : HeroKeyB;
        if (fade < 1 && layers.TryGet(oldKey, out var old))
        {
            list.AddLayer(Scaled(old, x, y, 1 - fade, punch));
        }

        if (layers.TryGet(HeroKey, out var hero))
        {
            list.AddLayer(Scaled(hero, x, y, fade, punch));
        }
    }

    private BoardLayerInstance Scaled(in BoardLayerSlot slot, double x, double y, double alpha, double scale)
    {
        // The punch scales about the number's center.
        var (left, top, width, height) = HeroNumber;
        var cx = BoardCanvas.Margin + left + width / 2;
        var cy = BoardCanvas.Margin + top + height / 2;
        var instance = BoardLayerInstance.Create(slot, cx + (x - cx) * scale, cy + (y - cy) * scale, alpha);
        instance.Rect.Z *= (float)scale;
        instance.Rect.W *= (float)scale;
        return instance;
    }

    // The CSS punch: 1 → 1.06 at 35 % → 1 over 0.4 s.
    private double PunchScale(double now)
    {
        var t = (now - _punchStart) / PunchSeconds;
        if (t is < 0 or >= 1)
        {
            return 1;
        }

        var up = t < 0.35 ? Easing.InOutSine(t / 0.35) : 1 - Easing.InOutSine((t - 0.35) / 0.65);
        return 1 + 0.06 * up;
    }

    private (Rgba Tint, double Amount, double Dim) Flash(BoardRowMotion row, double now)
    {
        var t = row.FlashProgress(now);
        if (t >= 1 || row.FlashDirection == 0 || Effects.Policy.ReduceMotion)
        {
            return (default, 0, 1);
        }

        if (row.FlashDirection < 0)
        {
            return (default, 0, 0.45 + 0.55 * Easing.OutCubic(t));
        }

        // White-hot, then the row's heat color, then the layer's own color.
        var heat = HeatPalette.Sample(HeatMath.Heat(row.Live, _tick?.Redline ?? HeatMath.DefaultRedline));
        var color = Rgba.Lerp(HeatPalette.WhiteHot, heat, Math.Clamp(t / 0.4, 0, 1));
        return (color, 1 - Easing.OutCubic(t), 1);
    }

    private void Fire(BoardTick previous, BoardTick tick, double now)
    {
        var reduce = Effects.Policy.ReduceMotion;
        if (!reduce && previous.Value > 0 && tick.Value > previous.Value * (1 + PunchRatio) + PunchFloor)
        {
            _punchStart = now;
            var (left, top, width, height) = HeroNumber;
            Effects.Punch(left, top, width, height, (tick.Value - previous.Value) / Math.Max(1, previous.Value));
        }

        if (tick.NewPeak)
        {
            var (left, top, width, height) = HeroNumber;
            Effects.NewPeak(left + width / 2, top + height / 2);
        }

        foreach (var e in tick.Events)
        {
            if (RowFx(tick, e) is not { } fx)
            {
                continue;
            }

            if (e.Kind == BoardRankEventKind.NewLeader)
            {
                Effects.NewLeader(fx);
            }
            else
            {
                Effects.Overtake(fx);
            }
        }
    }

    private BoardRowFx? RowFx(BoardTick tick, BoardRankEvent e)
    {
        var metrics = Metrics;
        BoardEntry? climber = null;
        BoardEntry? passed = null;
        foreach (var entry in tick.Entries)
        {
            if (entry.Kind != BoardEntryKind.Group)
            {
                continue;
            }

            if (climber is not null && passed is null)
            {
                passed = entry;
            }

            if (string.Equals(entry.Key, e.Id, StringComparison.Ordinal))
            {
                climber = entry;
            }
        }

        if (climber is null)
        {
            return null;
        }

        var centerY = metrics.ListTop + climber.Y + climber.Height / 2;
        var passedY = passed is null ? double.NaN : metrics.ListTop + passed.Y + passed.Height / 2;
        var rankX = metrics.ListLeft + 14;
        return new BoardRowFx(climber.Key, metrics.ListLeft, metrics.ListLeft + metrics.ListWidth, centerY, climber.Height, rankX, centerY,
            passedY, HeatPalette.SourceColor(climber.Group.Root.Source));
    }
}
