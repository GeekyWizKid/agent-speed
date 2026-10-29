using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>A pair picker's choice: a scored source with its dot.</summary>
internal sealed class TaskSourceOption(SourceKind source) : ObservableObject
{
    private string _name = string.Empty;

    public SourceKind Source { get; } = source;

    public IReadOnlyList<SourceKind> Sources { get; } = [source];

    public string Name { get => _name; set => Set(ref _name, value); }

    public override string ToString() => Name;
}

/// <summary>
/// A side header or narrow side card (DESIGN 12, <i>Side headers</i>), kept across updates so its median crossfades
/// and its distribution springs when the range, source or pair changes.
/// </summary>
internal sealed class TaskSideView : ObservableObject
{
    private bool _isShown;
    private SourceKind _source;
    private IReadOnlyList<SourceKind> _sources = [];
    private Color _color = Colors.Gray;
    private string _name = string.Empty;
    private bool _isEmpty;
    private string _counts = string.Empty;
    private string _median = string.Empty;
    private string _medianCaption = string.Empty;
    private TaskHistogram? _histogram;
    private bool _lowSample;
    private string _lowSampleText = string.Empty;
    private string _lowSampleHint = string.Empty;
    private string _emptyText = string.Empty;
    private string _spoken = string.Empty;

    public bool IsShown { get => _isShown; private set => Set(ref _isShown, value); }

    public SourceKind Source => _source;

    /// <summary>The source alone, for its dot.</summary>
    public IReadOnlyList<SourceKind> Sources { get => _sources; private set => Set(ref _sources, value); }

    public Color Color { get => _color; private set => Set(ref _color, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>No record in the range: the header shows <see cref="EmptyText"/> under the name, and nothing else.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (Set(ref _isEmpty, value))
            {
                OnPropertyChanged(nameof(HasRecords));
            }
        }
    }

    public bool HasRecords => !IsEmpty;

    public string Counts { get => _counts; private set => Set(ref _counts, value); }

    public string Median { get => _median; private set => Set(ref _median, value); }

    public string MedianCaption { get => _medianCaption; private set => Set(ref _medianCaption, value); }

    public TaskHistogram? Histogram { get => _histogram; private set => Set(ref _histogram, value); }

    public bool LowSample { get => _lowSample; private set => Set(ref _lowSample, value); }

    public string LowSampleText { get => _lowSampleText; private set => Set(ref _lowSampleText, value); }

    public string LowSampleHint { get => _lowSampleHint; private set => Set(ref _lowSampleHint, value); }

    public string EmptyText { get => _emptyText; private set => Set(ref _emptyText, value); }

    /// <summary><c>a11y.history.tasks.side</c>: the header is one element.</summary>
    public string Spoken { get => _spoken; private set => Set(ref _spoken, value); }

    public void Apply(TaskSide? side)
    {
        IsShown = side is not null;
        if (side is null)
        {
            return;
        }

        if (side.Source != _source || Sources.Count == 0)
        {
            _source = side.Source;
            Sources = [side.Source];
            Color = TaskAppearance.SourceColor(side.Source);
        }

        Name = side.Name;
        IsEmpty = side.IsEmpty;
        Counts = side.Counts;
        Median = side.Median;
        MedianCaption = side.MedianCaption;
        if (!Same(Histogram, side.Histogram))
        {
            Histogram = side.Histogram;
        }

        LowSample = side.LowSample;
        LowSampleText = side.LowSampleText;
        LowSampleHint = side.LowSampleHint;
        EmptyText = side.EmptyText;
        Spoken = side.Spoken;
    }

    private static bool Same(TaskHistogram? a, TaskHistogram? b) =>
        a is not null && b is not null && a.MedianBin == b.MedianBin && a.MedianAt == b.MedianAt && a.Spoken == b.Spoken
        && a.Counts.SequenceEqual(b.Counts);
}
