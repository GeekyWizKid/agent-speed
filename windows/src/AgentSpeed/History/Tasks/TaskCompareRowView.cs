using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>A metric's name in the centre of a comparison row, or in a one-source row's label column.</summary>
internal abstract class TaskMetricRowView(TaskMetric metric) : ObservableObject
{
    private string _name = string.Empty;
    private string _hint = string.Empty;
    private bool _lowerIsBetter;
    private string _lowerBetterText = string.Empty;
    private string _spoken = string.Empty;

    public TaskMetric Metric { get; } = metric;

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>The metric's <c>.hint</c>, its name's tooltip.</summary>
    public string Hint { get => _hint; private set => Set(ref _hint, value); }

    public bool LowerIsBetter { get => _lowerIsBetter; private set => Set(ref _lowerIsBetter, value); }

    public string LowerBetterText { get => _lowerBetterText; private set => Set(ref _lowerBetterText, value); }

    /// <summary>The row is one element for screen readers.</summary>
    public string Spoken { get => _spoken; private set => Set(ref _spoken, value); }

    protected void ApplyMetric(string name, string hint, bool lowerIsBetter, string lowerBetterText, string spoken)
    {
        Name = name;
        Hint = hint;
        LowerIsBetter = lowerIsBetter;
        LowerBetterText = lowerBetterText;
        Spoken = spoken;
    }
}

/// <summary>
/// One butterfly row (DESIGN 12, <i>Comparison rows</i>), kept per metric so its values crossfade and its tracks spring
/// when the range, source or pair changes.
/// </summary>
internal sealed class TaskCompareRowView(TaskMetric metric) : TaskMetricRowView(metric)
{
    private TaskValue? _a;
    private TaskValue? _b;
    private Color _colorA = Colors.Gray;
    private Color _colorB = Colors.Gray;

    public TaskValue? A { get => _a; private set => Set(ref _a, value); }

    public TaskValue? B { get => _b; private set => Set(ref _b, value); }

    public Color ColorA { get => _colorA; private set => Set(ref _colorA, value); }

    public Color ColorB { get => _colorB; private set => Set(ref _colorB, value); }

    public void Apply(TaskCompareRow row, TaskPair pair)
    {
        ApplyMetric(row.Name, row.Hint, row.LowerIsBetter, row.LowerBetterText, row.Spoken);
        ColorA = TaskAppearance.SourceColor(pair.A);
        ColorB = TaskAppearance.SourceColor(pair.B);
        A = row.A;
        B = row.B;
    }
}

/// <summary>A one-source row (DESIGN 12, <i>One source</i>): label column, bar and value.</summary>
internal sealed class TaskSingleRowView(TaskMetric metric) : TaskMetricRowView(metric)
{
    private TaskValue? _value;
    private Color _color = Colors.Gray;

    public TaskValue? Value { get => _value; private set => Set(ref _value, value); }

    public Color Color { get => _color; private set => Set(ref _color, value); }

    public void Apply(TaskSingleRow row, SourceKind source)
    {
        ApplyMetric(row.Name, row.Hint, row.LowerIsBetter, row.LowerBetterText, row.Spoken);
        Color = TaskAppearance.SourceColor(source);
        Value = row.Value;
    }
}
