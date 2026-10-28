using System.Collections.ObjectModel;
using System.Windows;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// State of the Usage History window's task performance section (DESIGN 4b, 3c; DESIGN 12, <i>Usage History: task
/// performance</i>): the range's comparison as <see cref="TaskPerformanceView"/> words it, in the pair, layout and sort
/// chosen here. Rows are kept and updated in place, so values crossfade, bars spring and sorted rows move.
/// </summary>
/// <remarks>
/// The window feeds it with <see cref="Update"/> after every recomputation, <see cref="RememberPair"/> once,
/// <see cref="SetAppearance"/> on theme changes; the section reports its width, picks and sorts.
/// </remarks>
internal sealed class TaskPerformanceViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<SourceKind, TaskSourceOption> _options = new();
    private Localizer _loc;
    private TaskComparison _comparison = TaskComparison.Empty;
    private SourceKind? _selectedSource;
    private bool _analyzing;
    private TaskPair? _remembered;
    private Action<TaskPair>? _pairChanged;
    private TaskModelColumn _sort = TaskModelTable.DefaultSort;
    private bool? _descending;
    private double _contentWidth = double.PositiveInfinity;
    private TaskPerformanceView? _view;
    private TaskPerformanceMode _mode = TaskPerformanceMode.Empty;
    private TaskPerformanceWidth _width = TaskPerformanceWidth.Wide;
    private TaskPair? _pair;
    private IReadOnlyList<TaskSourceOption> _optionsA = [];
    private IReadOnlyList<TaskSourceOption> _optionsB = [];
    private TaskSourceOption? _pickedA;
    private TaskSourceOption? _pickedB;
    private string _title = string.Empty;
    private string _info = string.Empty;
    private string _infoLabel = string.Empty;
    private IReadOnlyList<TaskInfoTerm> _infoTerms = [];
    private string _versus = string.Empty;
    private string _pickA = string.Empty;
    private string _pickB = string.Empty;
    private string _pickAHelp = string.Empty;
    private string _pickBHelp = string.Empty;
    private string? _onlyOne;
    private string _byModel = string.Empty;
    private string _footnote = string.Empty;
    private string _analyzingText = string.Empty;
    private string _emptyText = string.Empty;
    private string _emptyHint = string.Empty;
    private string _copyRow = string.Empty;
    private IReadOnlyList<TaskModelHeader> _headers = [];
    private TaskModelColumn _sortColumn = TaskModelTable.DefaultSort;
    private bool _sortDescending = true;
    private bool _hasModels;
    private bool _dark;
    private bool _highContrast;
    private bool _reduceMotion;
    private int _appearanceGeneration;
    private bool _disposed;

    public TaskPerformanceViewModel(Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        _loc = loc;
        _loc.LanguageChanged += OnLanguageChanged;
        Rows = TaskMetrics.All.Select(m => new TaskCompareRowView(m)).ToArray();
        SingleRows = TaskMetrics.All.Select(m => new TaskSingleRowView(m)).ToArray();
        Rebuild();
    }

    /// <summary>After every rebuild, once the rows hold the new words.</summary>
    public event EventHandler? Rebuilt;

    public Localizer Loc => _loc;

    public TaskPerformanceMode Mode
    {
        get => _mode;
        private set
        {
            if (Set(ref _mode, value))
            {
                RaiseShown();
            }
        }
    }

    /// <summary>The layout for the section's content width (wide from 860, medium from 600, else narrow).</summary>
    public TaskPerformanceWidth Width
    {
        get => _width;
        private set
        {
            if (Set(ref _width, value))
            {
                OnPropertyChanged(nameof(IsNarrow));
                OnPropertyChanged(nameof(CenterColumn));
                OnPropertyChanged(nameof(HistogramWidth));
                RaiseShown();
            }
        }
    }

    public bool IsNarrow => Width == TaskPerformanceWidth.Narrow;

    /// <summary>The butterfly's centre column (140 wide, 120 medium).</summary>
    public GridLength CenterColumn => new(TaskPerformanceLayout.CenterColumn(Width));

    /// <summary>A side header's distribution width (120 wide, 96 medium).</summary>
    public double HistogramWidth => TaskPerformanceLayout.HistogramWidth(Width);

    /// <summary>The pair shown side by side; null outside the pair mode.</summary>
    public TaskPair? Pair { get => _pair; private set => Set(ref _pair, value); }

    public bool ShowsAnalyzing => Mode == TaskPerformanceMode.Analyzing;

    public bool ShowsEmpty => Mode == TaskPerformanceMode.Empty;

    public bool ShowsButterfly => Mode == TaskPerformanceMode.Pair && !IsNarrow;

    public bool ShowsPairCards => Mode == TaskPerformanceMode.Pair && IsNarrow;

    public bool ShowsSingle => Mode == TaskPerformanceMode.Single;

    public bool ShowsData => Mode is TaskPerformanceMode.Pair or TaskPerformanceMode.Single;

    public bool ShowsPickers => Mode == TaskPerformanceMode.Pair;

    public bool ShowsOnlyOne => Mode == TaskPerformanceMode.Single && OnlyOne is not null;

    public bool ShowsModels => ShowsData && HasModels;

    public bool ShowsTable => ShowsData && HasModels && !IsNarrow;

    public bool ShowsList => ShowsData && HasModels && IsNarrow;

    public IReadOnlyList<TaskSourceOption> OptionsA { get => _optionsA; private set => Set(ref _optionsA, value); }

    public IReadOnlyList<TaskSourceOption> OptionsB { get => _optionsB; private set => Set(ref _optionsB, value); }

    public TaskSourceOption? PickedA { get => _pickedA; private set => Set(ref _pickedA, value); }

    public TaskSourceOption? PickedB { get => _pickedB; private set => Set(ref _pickedB, value); }

    public string Title { get => _title; private set => Set(ref _title, value); }

    public string Info { get => _info; private set => Set(ref _info, value); }

    /// <summary>The ⓘ button's name and tooltip.</summary>
    public string InfoLabel { get => _infoLabel; private set => Set(ref _infoLabel, value); }

    /// <summary>The ⓘ popover's definition list: every metric's full name with its hint.</summary>
    public IReadOnlyList<TaskInfoTerm> InfoTerms { get => _infoTerms; private set => Set(ref _infoTerms, value); }

    public string Versus { get => _versus; private set => Set(ref _versus, value); }

    public string PickA { get => _pickA; private set => Set(ref _pickA, value); }

    public string PickB { get => _pickB; private set => Set(ref _pickB, value); }

    /// <summary>A picker's help text: <c>vs</c> and the other side's source.</summary>
    public string PickAHelp { get => _pickAHelp; private set => Set(ref _pickAHelp, value); }

    public string PickBHelp { get => _pickBHelp; private set => Set(ref _pickBHelp, value); }

    /// <summary><c>history.tasks.onlyOne</c> in place of the pickers; null when the pickers show or the window chose the source.</summary>
    public string? OnlyOne
    {
        get => _onlyOne;
        private set
        {
            if (Set(ref _onlyOne, value))
            {
                OnPropertyChanged(nameof(ShowsOnlyOne));
            }
        }
    }

    public TaskSideView SideA { get; } = new();

    public TaskSideView SideB { get; } = new();

    public TaskSideView Single { get; } = new();

    /// <summary>The five butterfly rows, one per metric, always the same objects.</summary>
    public IReadOnlyList<TaskCompareRowView> Rows { get; }

    /// <summary>The five one-source rows, one per metric.</summary>
    public IReadOnlyList<TaskSingleRowView> SingleRows { get; }

    /// <summary>The models in the chosen order; rows are kept per id and moved.</summary>
    public ObservableCollection<TaskModelRowView> Models { get; } = [];

    public bool HasModels
    {
        get => _hasModels;
        private set
        {
            if (Set(ref _hasModels, value))
            {
                RaiseShown();
            }
        }
    }

    public IReadOnlyList<TaskModelHeader> Headers { get => _headers; private set => Set(ref _headers, value); }

    public TaskModelColumn SortColumn { get => _sortColumn; private set => Set(ref _sortColumn, value); }

    public bool SortDescending { get => _sortDescending; private set => Set(ref _sortDescending, value); }

    public string ByModel { get => _byModel; private set => Set(ref _byModel, value); }

    public string Footnote { get => _footnote; private set => Set(ref _footnote, value); }

    public string AnalyzingText { get => _analyzingText; private set => Set(ref _analyzingText, value); }

    public string EmptyText { get => _emptyText; private set => Set(ref _emptyText, value); }

    public string EmptyHint { get => _emptyHint; private set => Set(ref _emptyHint, value); }

    /// <summary><c>win.history.copyRow</c>, the rows' context menu.</summary>
    public string CopyRow { get => _copyRow; private set => Set(ref _copyRow, value); }

    public bool Dark { get => _dark; private set => Set(ref _dark, value); }

    public bool HighContrast { get => _highContrast; private set => Set(ref _highContrast, value); }

    public bool ReduceMotion { get => _reduceMotion; private set => Set(ref _reduceMotion, value); }

    /// <summary>Counts theme notifications; the section redraws on each, for system colors that changed alone.</summary>
    public int AppearanceGeneration { get => _appearanceGeneration; private set => Set(ref _appearanceGeneration, value); }

    /// <summary>The range's records and the window's choices; call after every recomputation of the window.</summary>
    /// <param name="comparison">The range's comparison (METRICS 6.11) — over the one source when the window's picker selects one.</param>
    /// <param name="selectedSource">The window's source picker; null for all sources.</param>
    /// <param name="analyzing">The history is being re-analysed (<c>history.analyzing</c>).</param>
    public void Update(TaskComparison comparison, SourceKind? selectedSource, bool analyzing)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        _comparison = comparison;
        _selectedSource = selectedSource;
        _analyzing = analyzing;
        Rebuild();
    }

    /// <param name="pair">The pair the window remembers; null for the default.</param>
    /// <param name="changed">Persists a pair chosen in the pickers.</param>
    public void RememberPair(TaskPair? pair, Action<TaskPair>? changed)
    {
        _remembered = pair is { IsValid: true } ? pair : null;
        _pairChanged = changed;
        Rebuild();
    }

    public void SetAppearance(bool dark, bool highContrast, bool reduceMotion)
    {
        Dark = dark;
        HighContrast = highContrast;
        ReduceMotion = reduceMotion;
        AppearanceGeneration++;
    }

    /// <summary>Re-reads every word in <paramref name="loc"/> (the language changed).</summary>
    public void Relocalize(Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (!ReferenceEquals(loc, _loc))
        {
            _loc.LanguageChanged -= OnLanguageChanged;
            _loc = loc;
            _loc.LanguageChanged += OnLanguageChanged;
        }

        Rebuild();
    }

    /// <summary>The section's content width; a new layout rebuilds the rows.</summary>
    public void SetContentWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return;
        }

        var before = TaskPerformanceLayout.Classify(_contentWidth);
        _contentWidth = width;
        if (TaskPerformanceLayout.Classify(width) != before)
        {
            Rebuild();
        }
    }

    /// <summary>A picker's new choice; choosing the other side's source swaps the two.</summary>
    public void Pick(bool first, SourceKind source)
    {
        if (Pair is not { } pair || (first ? pair.A : pair.B) == source)
        {
            return;
        }

        var next = TaskPerformancePair.Pick(pair, first, source);
        _remembered = next;
        _pairChanged?.Invoke(next);
        Rebuild();
    }

    /// <summary>A header click: the sorted column flips its direction, another column starts in its first direction.</summary>
    public void SortBy(TaskModelColumn column)
    {
        if (column == _sortColumn && _view is not null)
        {
            _descending = !_sortDescending;
        }
        else
        {
            _descending = TaskModelTable.FirstDirection(column);
        }

        _sort = column;
        Rebuild();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loc.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        // The medium table merges scored into the tasks column, which then shows a sort by scored tasks.
        var merged = TaskPerformanceLayout.MergesCounts(TaskPerformanceLayout.Classify(_contentWidth));
        var sort = merged && _sort == TaskModelColumn.Scored ? TaskModelColumn.Tasks : _sort;
        var input = new TaskPerformanceInput(_comparison, _selectedSource, _remembered, _analyzing, sort, _descending);
        var view = TaskPerformanceView.Build(input, _contentWidth, _loc);
        _view = view;

        Title = view.Title;
        Info = view.Info;
        InfoLabel = view.InfoLabel;
        if (!InfoTerms.SequenceEqual(view.InfoTerms))
        {
            InfoTerms = view.InfoTerms;
        }

        Versus = view.Versus;
        PickA = view.PickA;
        PickB = view.PickB;
        ByModel = view.ByModel;
        Footnote = view.Footnote;
        AnalyzingText = view.AnalyzingText;
        EmptyText = view.EmptyText;
        EmptyHint = view.EmptyHint;
        CopyRow = _loc["win.history.copyRow"];

        Pair = view.Pair;
        ApplyPickers(view);
        OnlyOne = view.OnlyOne;
        SideA.Apply(view.SideA);
        SideB.Apply(view.SideB);
        Single.Apply(view.Single);
        if (view.Pair is { } pair)
        {
            for (var i = 0; i < Rows.Count && i < view.Rows.Count; i++)
            {
                Rows[i].Apply(view.Rows[i], pair);
            }
        }

        if (view.Single is { } single)
        {
            for (var i = 0; i < SingleRows.Count && i < view.SingleRows.Count; i++)
            {
                SingleRows[i].Apply(view.SingleRows[i], single.Source);
            }
        }

        ApplyModels(view.Models);
        Width = view.Width;
        Mode = view.Mode;
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPickers(TaskPerformanceView view)
    {
        if (view.Pair is not { } pair)
        {
            PickedA = PickedB = null;
            OptionsA = OptionsB = [];
            PickAHelp = PickBHelp = string.Empty;
            return;
        }

        var optionsA = view.OptionsA.Select(Option).ToArray();
        var optionsB = view.OptionsB.Select(Option).ToArray();
        if (!OptionsA.SequenceEqual(optionsA))
        {
            OptionsA = optionsA;
        }

        if (!OptionsB.SequenceEqual(optionsB))
        {
            OptionsB = optionsB;
        }

        PickedA = Option(pair.A);
        PickedB = Option(pair.B);
        PickAHelp = view.Versus + " " + PickedB.Name;
        PickBHelp = view.Versus + " " + PickedA.Name;
    }

    private TaskSourceOption Option(SourceKind source)
    {
        if (!_options.TryGetValue(source, out var option))
        {
            option = new TaskSourceOption(source);
            _options[source] = option;
        }

        option.Name = TaskPerformanceView.Name(source, _loc);
        return option;
    }

    /// <summary>Matches rows by model id and moves them into the new order.</summary>
    private void ApplyModels(TaskModelTable table)
    {
        if (!Headers.SequenceEqual(table.Headers))
        {
            Headers = table.Headers;
        }

        SortColumn = table.SortColumn;
        SortDescending = table.Descending;
        var scored = _comparison.ByModel.GroupBy(m => m.Model, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Stats.ScoredTasks, StringComparer.Ordinal);
        var existing = Models.ToDictionary(r => r.Model, StringComparer.Ordinal);
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            if (!existing.Remove(row.Model, out var view))
            {
                view = new TaskModelRowView(row.Model);
                Models.Insert(Math.Min(i, Models.Count), view);
            }
            else if (Models.IndexOf(view) is var at && at != i)
            {
                Models.Move(at, i);
            }

            view.Apply(row, table.LowSampleText, scored.GetValueOrDefault(row.Model), _loc);
        }

        foreach (var stale in existing.Values)
        {
            Models.Remove(stale);
        }

        HasModels = Models.Count > 0;
    }

    private void RaiseShown()
    {
        OnPropertyChanged(nameof(ShowsAnalyzing));
        OnPropertyChanged(nameof(ShowsEmpty));
        OnPropertyChanged(nameof(ShowsButterfly));
        OnPropertyChanged(nameof(ShowsPairCards));
        OnPropertyChanged(nameof(ShowsSingle));
        OnPropertyChanged(nameof(ShowsData));
        OnPropertyChanged(nameof(ShowsPickers));
        OnPropertyChanged(nameof(ShowsOnlyOne));
        OnPropertyChanged(nameof(ShowsModels));
        OnPropertyChanged(nameof(ShowsTable));
        OnPropertyChanged(nameof(ShowsList));
    }
}
