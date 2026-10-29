using System.Text;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>The by-model table's columns, in order (DESIGN 12, <i>By model</i>).</summary>
public enum TaskModelColumn
{
    Model,
    Tasks,
    Scored,
    Median,
    Success,
    Stall,
    FixFail,
    Attempts,
    FirstPass,
}

/// <param name="Text">The header's short name.</param>
/// <param name="FullName">Its tooltip and accessibility label.</param>
/// <param name="Merged">Medium layout: the tasks column reads <c>history.tasks.column.scoredOfTasks</c> and holds both counts.</param>
public sealed record TaskModelHeader(TaskModelColumn Column, string Text, string FullName, bool Numeric, bool Merged = false);

/// <summary>A median cell's interquartile strip, as fractions of 0–100, in its first source's color.</summary>
public readonly record struct TaskStrip(double Lower, double Upper, double Median, SourceKind Color);

/// <param name="Model">The id as logged; empty for an unknown model.</param>
/// <param name="Name">The id, or <c>cost.noModelName</c>.</param>
/// <param name="SourcesTooltip"><c>history.tasks.model.sources</c>.</param>
/// <param name="ScoredOfTasks">The medium layout's merged cell (<c>96 / 128</c>).</param>
/// <param name="Strip">Null without a scored task.</param>
/// <param name="MedianSpoken">The median cell's text with <c>a11y.history.tasks.iqr</c>.</param>
/// <param name="Rates">Success, stall, fix failure, attempts and first pass, in <see cref="TaskMetrics.All"/> order.</param>
/// <param name="LowSample">Fewer than 10 scored tasks: values in secondary and the chip after the id.</param>
/// <param name="Line2">The narrow list's second line: success · stall · fix failure.</param>
/// <param name="Line3">The narrow list's third line: scored and tasks · attempts · first pass.</param>
/// <param name="Spoken">The row read as one label, every column named.</param>
/// <param name="CopyText">The row as tab-separated cells.</param>
public sealed record TaskModelRow(
    string Model,
    string Name,
    bool IsUnknown,
    IReadOnlyList<SourceKind> Sources,
    string SourcesTooltip,
    string Tasks,
    string Scored,
    string ScoredOfTasks,
    string Median,
    string MedianSpoken,
    TaskStrip? Strip,
    IReadOnlyList<TaskValue> Rates,
    bool LowSample,
    string Line2,
    string Line3,
    string Spoken,
    string CopyText)
{
    public TaskValue Rate(TaskMetric metric) => Rates[(int)metric];
}

/// <summary>The by-model table: headers, rows in the chosen order, the sort, and the small-sample chip's words.</summary>
public sealed record TaskModelTable(
    IReadOnlyList<TaskModelHeader> Headers,
    IReadOnlyList<TaskModelRow> Rows,
    TaskModelColumn SortColumn,
    bool Descending,
    string LowSampleText)
{
    public static TaskModelTable Empty { get; } = new([], [], TaskModelColumn.Tasks, true, string.Empty);

    /// <summary>The sort the table opens with: tasks descending, then the id.</summary>
    public const TaskModelColumn DefaultSort = TaskModelColumn.Tasks;

    /// <summary>The direction a column sorts in when first chosen: the id ascending, numbers descending.</summary>
    public static bool FirstDirection(TaskModelColumn column) => column != TaskModelColumn.Model;

    public static TaskModelTable Build(IReadOnlyList<TaskModelStats> models, Localizer loc, TaskPerformanceWidth width,
        TaskModelColumn sort = DefaultSort, bool? descending = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(loc);
        var down = descending ?? FirstDirection(sort);
        var rows = Sort(models, sort, down).Select(m => Row(m, loc)).ToArray();
        return new TaskModelTable(HeadersFor(loc, TaskPerformanceLayout.MergesCounts(width)), rows, sort, down, loc["history.tasks.sample.low"]);
    }

    public static IReadOnlyList<TaskModelHeader> HeadersFor(Localizer loc, bool merged)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var headers = new List<TaskModelHeader>(9)
        {
            new(TaskModelColumn.Model, loc["history.tasks.column.model"], loc["history.tasks.column.model"], false),
        };
        if (merged)
        {
            var both = loc["history.tasks.column.scoredOfTasks"];
            headers.Add(new TaskModelHeader(TaskModelColumn.Tasks, both, both, true, Merged: true));
        }
        else
        {
            headers.Add(new TaskModelHeader(TaskModelColumn.Tasks, loc["history.tasks.metric.tasks"], loc["history.tasks.metric.tasks"], true));
            headers.Add(new TaskModelHeader(TaskModelColumn.Scored, loc["history.tasks.metric.scored.short"], loc["history.tasks.metric.scored"], true));
        }

        headers.Add(new TaskModelHeader(TaskModelColumn.Median, loc["history.tasks.metric.median.short"], loc["history.tasks.metric.median"], true));
        foreach (var metric in TaskMetrics.All)
        {
            headers.Add(new TaskModelHeader(Column(metric), loc[TaskMetrics.ShortKey(metric)], loc[TaskMetrics.Key(metric)], true));
        }

        return headers;
    }

    /// <summary>
    /// Rows by <paramref name="column"/>; a missing value sorts last either way, and ties fall back to the default order
    /// (tasks descending, then the id's UTF-8 bytes).
    /// </summary>
    public static IReadOnlyList<TaskModelStats> Sort(IEnumerable<TaskModelStats> models, TaskModelColumn column, bool descending)
    {
        ArgumentNullException.ThrowIfNull(models);
        var list = models.ToList();
        list.Sort((x, y) =>
        {
            var primary = column == TaskModelColumn.Model ? CompareIds(x.Model, y.Model) : CompareNullable(Key(x.Stats, column), Key(y.Stats, column), descending);
            if (column == TaskModelColumn.Model && descending)
            {
                primary = -primary;
            }

            if (primary != 0)
            {
                return primary;
            }

            var tasks = y.Stats.Tasks.CompareTo(x.Stats.Tasks);
            return tasks != 0 ? tasks : CompareIds(x.Model, y.Model);
        });
        return list;
    }

    /// <summary>The ids' UTF-8 bytes, as METRICS 6.11 orders them.</summary>
    public static int CompareIds(string? a, string? b)
    {
        var x = Encoding.UTF8.GetBytes(a ?? string.Empty);
        var y = Encoding.UTF8.GetBytes(b ?? string.Empty);
        return x.AsSpan().SequenceCompareTo(y);
    }

    private static int CompareNullable(double? a, double? b, bool descending)
    {
        if (a is null || b is null)
        {
            return a is null ? (b is null ? 0 : 1) : -1;
        }

        var order = a.Value.CompareTo(b.Value);
        return descending ? -order : order;
    }

    private static double? Key(TaskStats stats, TaskModelColumn column) => column switch
    {
        TaskModelColumn.Tasks => stats.Tasks,
        TaskModelColumn.Scored => stats.ScoredTasks,
        TaskModelColumn.Median => stats.MedianScore,
        TaskModelColumn.Success => stats.SuccessRate,
        TaskModelColumn.Stall => stats.StallRate,
        TaskModelColumn.FixFail => stats.FixFailureRate,
        TaskModelColumn.Attempts => stats.RecoverAttempts,
        TaskModelColumn.FirstPass => stats.TimeToFirstPassMs,
        _ => null,
    };

    private static TaskModelColumn Column(TaskMetric metric) => metric switch
    {
        TaskMetric.Success => TaskModelColumn.Success,
        TaskMetric.Stall => TaskModelColumn.Stall,
        TaskMetric.FixFail => TaskModelColumn.FixFail,
        TaskMetric.Attempts => TaskModelColumn.Attempts,
        _ => TaskModelColumn.FirstPass,
    };

    private static TaskModelRow Row(TaskModelStats model, Localizer loc)
    {
        var stats = model.Stats;
        var unknown = string.IsNullOrWhiteSpace(model.Model);
        var name = unknown ? loc["cost.noModelName"] : model.Model;
        var sources = TaskScoredSources.All.Where(model.Sources.Contains).Concat(model.Sources.Where(s => !TaskScoredSources.Contains(s))).Distinct().ToArray();
        var sourcesTooltip = loc.Format("history.tasks.model.sources", HistoryText.SourceList(sources, loc));
        var low = stats.ScoredTasks < TaskMetrics.LowSample;
        var emphasis = low ? TaskEmphasis.Other : TaskEmphasis.Plain;
        var rates = TaskMetrics.All.Select(m => TaskMetrics.Value(m, stats, TaskMetrics.Scale(m, TaskMetrics.Raw(m, stats), null), emphasis, loc)).ToArray();
        var tasks = TaskPerformanceText.Count(stats.Tasks, loc);
        var scored = TaskPerformanceText.Count(stats.ScoredTasks, loc);
        var median = TaskPerformanceText.Score(stats.MedianScore);
        TaskStrip? strip = stats is { MedianScore: { } m, LowerQuartile: { } q1, UpperQuartile: { } q3 } && sources.Length > 0
            ? new TaskStrip(Fraction(q1), Fraction(q3), Fraction(m), sources[0])
            : null;
        var medianSpoken = strip is null
            ? median
            : HistoryText.JoinParts([median, loc.Format("a11y.history.tasks.iqr", TaskPerformanceText.Score(stats.LowerQuartile), TaskPerformanceText.Score(stats.UpperQuartile))], loc);

        var labels = new[]
        {
            loc["history.tasks.metric.success"], loc["history.tasks.metric.stall"], loc["history.tasks.metric.fixFail"],
        };
        var line2 = string.Join(" · ", labels.Select((label, i) => label + " " + rates[i].Text));
        var line3 = string.Join(" · ",
            loc.Format("history.tasks.counts", stats.ScoredTasks, stats.Tasks),
            loc["history.tasks.metric.attempts.short"] + " " + rates[(int)TaskMetric.Attempts].Text,
            loc["history.tasks.metric.firstPass.short"] + " " + rates[(int)TaskMetric.FirstPass].Text);

        var spoken = new List<string>
        {
            name,
            sourcesTooltip,
            HistoryText.Labelled(loc["history.tasks.metric.tasks"], tasks, loc),
            HistoryText.Labelled(loc["history.tasks.metric.scored"], scored, loc),
            HistoryText.Labelled(loc["history.tasks.metric.median"], medianSpoken, loc),
        };
        spoken.AddRange(TaskMetrics.All.Select((metric, i) => HistoryText.Labelled(loc[TaskMetrics.Key(metric)], rates[i].Spoken, loc)));
        if (low)
        {
            spoken.Add(loc["history.tasks.sample.low"]);
        }

        var copy = string.Join('\t', new[] { name, tasks, scored, median }.Concat(rates.Select(r => r.Text)));
        return new TaskModelRow(model.Model, name, unknown, sources, sourcesTooltip, tasks, scored, scored + " / " + tasks, median,
            medianSpoken, strip, rates, low, line2, line3, HistoryText.JoinParts(spoken, loc), copy);
    }

    private static double Fraction(double score) => double.IsFinite(score) ? Math.Clamp(score / TaskScoreText.MaxScore, 0, 1) : 0;
}
