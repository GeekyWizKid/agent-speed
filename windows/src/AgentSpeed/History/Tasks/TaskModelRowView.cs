using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// A model of the by-model table or narrow list (DESIGN 12, <i>By model</i>), kept per model id so a new sort moves it
/// with a spring and a new range crossfades its values.
/// </summary>
internal sealed class TaskModelRowView(string model) : ObservableObject
{
    private string _name = string.Empty;
    private bool _isUnknown;
    private IReadOnlyList<SourceKind> _sources = [];
    private string _sourcesTooltip = string.Empty;
    private string _tasks = string.Empty;
    private string _scored = string.Empty;
    private string _scoredOfTasks = string.Empty;
    private string _median = string.Empty;
    private string _medianSpoken = string.Empty;
    private TaskStrip? _strip;
    private TaskValue? _success;
    private TaskValue? _stall;
    private TaskValue? _fixFail;
    private TaskValue? _attempts;
    private TaskValue? _firstPass;
    private bool _lowSample;
    private string _lowSampleText = string.Empty;
    private string _lowSampleHint = string.Empty;
    private string _line2 = string.Empty;
    private string _line3 = string.Empty;
    private string _spoken = string.Empty;
    private string _modelSpoken = string.Empty;

    /// <summary>The id as logged; empty for an unknown model.</summary>
    public string Model { get; } = model;

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>No model id was logged: <c>cost.noModelName</c> in secondary italic.</summary>
    public bool IsUnknown { get => _isUnknown; private set => Set(ref _isUnknown, value); }

    public IReadOnlyList<SourceKind> Sources { get => _sources; private set => Set(ref _sources, value); }

    public string SourcesTooltip { get => _sourcesTooltip; private set => Set(ref _sourcesTooltip, value); }

    public string Tasks { get => _tasks; private set => Set(ref _tasks, value); }

    public string Scored { get => _scored; private set => Set(ref _scored, value); }

    public string ScoredOfTasks { get => _scoredOfTasks; private set => Set(ref _scoredOfTasks, value); }

    public string Median { get => _median; private set => Set(ref _median, value); }

    /// <summary>The median with <c>a11y.history.tasks.iqr</c>.</summary>
    public string MedianSpoken { get => _medianSpoken; private set => Set(ref _medianSpoken, value); }

    public TaskStrip? Strip
    {
        get => _strip;
        private set
        {
            if (Set(ref _strip, value))
            {
                OnPropertyChanged(nameof(HasStrip));
            }
        }
    }

    public bool HasStrip => Strip is not null;

    public TaskValue? Success { get => _success; private set => Set(ref _success, value); }

    public TaskValue? Stall { get => _stall; private set => Set(ref _stall, value); }

    public TaskValue? FixFail { get => _fixFail; private set => Set(ref _fixFail, value); }

    public TaskValue? Attempts { get => _attempts; private set => Set(ref _attempts, value); }

    public TaskValue? FirstPass { get => _firstPass; private set => Set(ref _firstPass, value); }

    /// <summary>Fewer than 10 scored tasks: values in secondary and the chip after the id.</summary>
    public bool LowSample { get => _lowSample; private set => Set(ref _lowSample, value); }

    public string LowSampleText { get => _lowSampleText; private set => Set(ref _lowSampleText, value); }

    public string LowSampleHint { get => _lowSampleHint; private set => Set(ref _lowSampleHint, value); }

    public string Line2 { get => _line2; private set => Set(ref _line2, value); }

    public string Line3 { get => _line3; private set => Set(ref _line3, value); }

    /// <summary>The row read as one label, every column named.</summary>
    public string Spoken { get => _spoken; private set => Set(ref _spoken, value); }

    /// <summary>The model cell's words: the id, its sources and the chip.</summary>
    public string ModelSpoken { get => _modelSpoken; private set => Set(ref _modelSpoken, value); }

    /// <summary>The row as tab-separated cells.</summary>
    public string CopyText { get; private set; } = string.Empty;

    public TaskValue? Rate(TaskModelColumn column) => column switch
    {
        TaskModelColumn.Success => Success,
        TaskModelColumn.Stall => Stall,
        TaskModelColumn.FixFail => FixFail,
        TaskModelColumn.Attempts => Attempts,
        TaskModelColumn.FirstPass => FirstPass,
        _ => null,
    };

    /// <param name="scored">The model's scored tasks, for the chip's tooltip.</param>
    public void Apply(TaskModelRow row, string lowSampleText, int scored, Localizer loc)
    {
        Name = row.Name;
        IsUnknown = row.IsUnknown;
        if (!Sources.SequenceEqual(row.Sources))
        {
            Sources = row.Sources;
        }

        SourcesTooltip = row.SourcesTooltip;
        Tasks = row.Tasks;
        Scored = row.Scored;
        ScoredOfTasks = row.ScoredOfTasks;
        Median = row.Median;
        MedianSpoken = row.MedianSpoken;
        Strip = row.Strip;
        Success = row.Rate(TaskMetric.Success);
        Stall = row.Rate(TaskMetric.Stall);
        FixFail = row.Rate(TaskMetric.FixFail);
        Attempts = row.Rate(TaskMetric.Attempts);
        FirstPass = row.Rate(TaskMetric.FirstPass);
        LowSample = row.LowSample;
        LowSampleText = lowSampleText;
        LowSampleHint = loc.Format("history.tasks.sample.hint", scored, TaskMetrics.LowSample);
        Line2 = row.Line2;
        Line3 = row.Line3;
        Spoken = row.Spoken;
        string[] parts = row.LowSample ? [row.Name, row.SourcesTooltip, lowSampleText] : [row.Name, row.SourcesTooltip];
        ModelSpoken = HistoryText.JoinParts(parts, loc);
        CopyText = row.CopyText;
    }
}
