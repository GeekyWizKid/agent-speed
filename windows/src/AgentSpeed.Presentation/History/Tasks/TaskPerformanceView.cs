using AgentSpeed.Core;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>What the section shows (DESIGN 12, <i>Usage History: task performance</i>, States).</summary>
public enum TaskPerformanceMode
{
    /// <summary>The first launch re-analyses every log and the range has no records yet: static placeholders.</summary>
    Analyzing,

    /// <summary>No record in the range for any scored source: the muted gauge, <c>history.tasks.empty</c> and its hint.</summary>
    Empty,

    /// <summary>Two sources side by side.</summary>
    Pair,

    /// <summary>One source full width: chosen in the window's picker, or the only one with records.</summary>
    Single,
}

/// <summary>A side's distribution: 10 bins with the median's bin at full color and a tick at the unrounded median.</summary>
/// <param name="Counts">The ten bins' counts.</param>
/// <param name="MedianBin">The median's bin, or -1 without a scored task.</param>
/// <param name="MedianAt">The median tick's place, 0–1 of the width; null without a scored task.</param>
/// <param name="Spoken"><c>a11y.history.tasks.distribution</c> with the quartiles and median as shown integers.</param>
public sealed record TaskHistogram(IReadOnlyList<int> Counts, int MedianBin, double? MedianAt, string Spoken)
{
    public int MaxCount => Counts.Count == 0 ? 0 : Counts.Max();

    /// <summary>The bin a score falls in: the number of edges 10 … 90 it reaches (90 and 100 are in bin 9).</summary>
    public static int BinOf(double score)
    {
        var bin = 0;
        for (var edge = 10; edge <= 90; edge += 10)
        {
            if (score >= edge)
            {
                bin++;
            }
        }

        return bin;
    }
}

/// <summary>A side header (wide and medium) or side card (narrow).</summary>
/// <param name="IsEmpty">The source has no record in the range: <c>history.tasks.side.empty</c>, values <c>–</c>.</param>
/// <param name="Counts"><c>history.tasks.counts</c>.</param>
/// <param name="Median">The median score as shown, or <c>–</c>.</param>
/// <param name="LowSample">Fewer than 10 scored tasks: the counts line ends with the chip.</param>
/// <param name="LowSampleHint">The chip's tooltip.</param>
/// <param name="Spoken"><c>a11y.history.tasks.side</c>: the header is one element.</param>
public sealed record TaskSide(
    SourceKind Source,
    string Name,
    bool IsEmpty,
    string Counts,
    string Median,
    string MedianCaption,
    TaskHistogram Histogram,
    bool LowSample,
    string LowSampleText,
    string LowSampleHint,
    string EmptyText,
    string Spoken);

/// <summary>One butterfly row: the metric in the centre and each side's value and track.</summary>
/// <param name="Better">0 none, 1 the left side, 2 the right side.</param>
/// <param name="Spoken"><c>a11y.history.tasks.compare</c>, wrapped in <c>a11y.history.tasks.better</c> when a side is marked.</param>
public sealed record TaskCompareRow(
    TaskMetric Metric,
    string Name,
    string Hint,
    bool LowerIsBetter,
    string LowerBetterText,
    TaskValue A,
    TaskValue B,
    int Better,
    string Spoken);

/// <summary>A one-source row: label, bar and value.</summary>
public sealed record TaskSingleRow(TaskMetric Metric, string Name, string Hint, bool LowerIsBetter, string LowerBetterText, TaskValue Value, string Spoken);

/// <summary>A definition in the ⓘ popover: a metric's full name and its hint.</summary>
public sealed record TaskInfoTerm(string Term, string Hint);

/// <summary>What builds the section: the range's comparison and the window's choices.</summary>
/// <param name="Comparison">The range's records compared — over the one source when the window's picker selects one.</param>
/// <param name="SelectedSource">The window's source picker; null for all sources.</param>
/// <param name="RememberedPair">The pair the window remembers.</param>
/// <param name="Analyzing">The history is being analysed (<c>history.analyzing</c>).</param>
public sealed record TaskPerformanceInput(
    TaskComparison Comparison,
    SourceKind? SelectedSource = null,
    TaskPair? RememberedPair = null,
    bool Analyzing = false,
    TaskModelColumn Sort = TaskModelTable.DefaultSort,
    bool? SortDescending = null);

/// <summary>The Usage History window's task performance section (DESIGN 12, section 3c of 4b), in the selected language.</summary>
/// <param name="Pair">The sources side by side (Pair mode).</param>
/// <param name="OptionsA">The left picker's choices.</param>
/// <param name="OnlyOne"><c>history.tasks.onlyOne</c>, replacing the pickers when one source alone has records; else null.</param>
/// <param name="Single">The one source's header (Single mode).</param>
/// <param name="Rows">The butterfly rows (Pair mode).</param>
/// <param name="SingleRows">One bar per metric (Single mode).</param>
public sealed record TaskPerformanceView(
    TaskPerformanceMode Mode,
    TaskPerformanceWidth Width,
    string Title,
    string Info,
    IReadOnlyList<TaskInfoTerm> InfoTerms,
    TaskPair? Pair,
    IReadOnlyList<SourceKind> OptionsA,
    IReadOnlyList<SourceKind> OptionsB,
    string Versus,
    string PickA,
    string PickB,
    string? OnlyOne,
    TaskSide? SideA,
    TaskSide? SideB,
    TaskSide? Single,
    IReadOnlyList<TaskCompareRow> Rows,
    IReadOnlyList<TaskSingleRow> SingleRows,
    string ByModel,
    TaskModelTable Models,
    string Footnote,
    string AnalyzingText,
    string EmptyText,
    string EmptyHint)
{
    public bool ShowsPickers => Mode == TaskPerformanceMode.Pair;

    public bool HasModels => Models.Rows.Count > 0;

    public static TaskPerformanceView Build(TaskPerformanceInput input, double contentWidth, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(loc);
        var comparison = input.Comparison;
        var width = TaskPerformanceLayout.Classify(contentWidth);
        var withRecords = TaskPerformancePair.WithRecords(comparison);
        var mode = ModeOf(input, withRecords);

        TaskPair? pair = null;
        IReadOnlyList<SourceKind> optionsA = [], optionsB = [];
        string? onlyOne = null;
        TaskSide? sideA = null, sideB = null, single = null;
        IReadOnlyList<TaskCompareRow> rows = [];
        IReadOnlyList<TaskSingleRow> singleRows = [];
        var models = TaskModelTable.Empty;

        if (mode == TaskPerformanceMode.Pair && TaskPerformancePair.Resolve(comparison, input.RememberedPair) is { } chosen)
        {
            pair = chosen;
            optionsA = TaskPerformancePair.Options(comparison, chosen.A, chosen.B);
            optionsB = TaskPerformancePair.Options(comparison, chosen.B, chosen.A);
            sideA = Side(chosen.A, comparison.Of(chosen.A), loc);
            sideB = Side(chosen.B, comparison.Of(chosen.B), loc);
            rows = TaskMetrics.All.Select(m => Compare(m, chosen, comparison, loc)).ToArray();
        }
        else if (mode == TaskPerformanceMode.Single)
        {
            var source = input.SelectedSource ?? withRecords[0];
            if (input.SelectedSource is null)
            {
                onlyOne = loc.Format("history.tasks.onlyOne", Name(source, loc));
            }

            var stats = comparison.Of(source);
            single = Side(source, stats, loc);
            singleRows = TaskMetrics.All.Select(m => SingleRow(m, stats, loc)).ToArray();
        }

        if (mode is TaskPerformanceMode.Pair or TaskPerformanceMode.Single)
        {
            var listed = mode == TaskPerformanceMode.Single && single is { } one
                ? comparison.ByModel.Where(m => m.Sources.Contains(one.Source)).ToArray()
                : comparison.ByModel;
            models = TaskModelTable.Build(listed, loc, width, input.Sort, input.SortDescending);
        }

        return new TaskPerformanceView(
            mode,
            width,
            loc["history.tasks.title"],
            loc["history.tasks.info"],
            InfoTermsOf(loc),
            pair,
            optionsA,
            optionsB,
            loc["history.tasks.vs"],
            loc["history.tasks.pickA.a11y"],
            loc["history.tasks.pickB.a11y"],
            onlyOne,
            sideA,
            sideB,
            single,
            rows,
            singleRows,
            loc["history.tasks.byModel"],
            models,
            loc["history.tasks.footnote"],
            loc["history.tasks.analyzing"],
            loc["history.tasks.empty"],
            loc["history.tasks.empty.hint"]);
    }

    public static string Name(SourceKind source, Localizer loc) => loc[DisplayText.SourceKey(source)];

    public static TaskSide Side(SourceKind source, TaskStats stats, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(loc);
        var name = Name(source, loc);
        var median = TaskPerformanceText.Score(stats.MedianScore);
        var histogram = new TaskHistogram(
            stats.Histogram.Count == TaskStats.Bins ? stats.Histogram : [.. stats.Histogram.Take(TaskStats.Bins), .. new int[Math.Max(0, TaskStats.Bins - stats.Histogram.Count)]],
            stats.MedianScore is { } m ? TaskHistogram.BinOf(m) : -1,
            stats.MedianScore is { } at ? Math.Clamp(at / TaskScoreText.MaxScore, 0, 1) : null,
            stats.MedianScore is null
                ? loc["history.tasks.side.empty"]
                : loc.Format("a11y.history.tasks.distribution", TaskPerformanceText.Score(stats.LowerQuartile), median,
                    TaskPerformanceText.Score(stats.UpperQuartile)));
        var empty = !stats.HasRecords;
        var spoken = empty
            ? HistoryText.JoinParts([name, loc["history.tasks.side.empty"]], loc)
            : loc.Format("a11y.history.tasks.side", name, stats.Tasks, stats.ScoredTasks, median);
        var low = !empty && stats.ScoredTasks < TaskMetrics.LowSample;
        if (low)
        {
            spoken = HistoryText.JoinParts([spoken, loc["history.tasks.sample.low"]], loc);
        }

        return new TaskSide(
            source,
            name,
            empty,
            empty ? string.Empty : loc.Format("history.tasks.counts", stats.ScoredTasks, stats.Tasks),
            median,
            loc["history.tasks.metric.median.short"],
            histogram,
            low,
            loc["history.tasks.sample.low"],
            loc.Format("history.tasks.sample.hint", stats.ScoredTasks, TaskMetrics.LowSample),
            loc["history.tasks.side.empty"],
            spoken);
    }

    public static IReadOnlyList<TaskInfoTerm> InfoTermsOf(Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        string[] keys =
        [
            "history.tasks.metric.scored", "history.tasks.metric.median", .. TaskMetrics.All.Select(TaskMetrics.Key),
        ];
        return keys.Select(k => new TaskInfoTerm(loc[k], loc[k + ".hint"])).ToArray();
    }

    private static TaskPerformanceMode ModeOf(TaskPerformanceInput input, IReadOnlyList<SourceKind> withRecords)
    {
        if (input.SelectedSource is { } selected)
        {
            if (input.Comparison.Of(selected).HasRecords && TaskScoredSources.Contains(selected))
            {
                return TaskPerformanceMode.Single;
            }

            return input.Analyzing ? TaskPerformanceMode.Analyzing : TaskPerformanceMode.Empty;
        }

        return withRecords.Count switch
        {
            0 => input.Analyzing ? TaskPerformanceMode.Analyzing : TaskPerformanceMode.Empty,
            1 => TaskPerformanceMode.Single,
            _ => TaskPerformanceMode.Pair,
        };
    }

    private static TaskCompareRow Compare(TaskMetric metric, TaskPair pair, TaskComparison comparison, Localizer loc)
    {
        var a = comparison.Of(pair.A);
        var b = comparison.Of(pair.B);
        var rawA = TaskMetrics.Raw(metric, a);
        var rawB = TaskMetrics.Raw(metric, b);
        var scale = TaskMetrics.Scale(metric, rawA, rawB);
        var better = TaskMetrics.Better(metric, rawA, rawB, a.ScoredTasks, b.ScoredTasks);
        var valueA = TaskMetrics.Value(metric, a, scale, Emphasis(better, 1), loc);
        var valueB = TaskMetrics.Value(metric, b, scale, Emphasis(better, 2), loc);
        var name = loc[TaskMetrics.Key(metric)];
        var lower = TaskMetrics.LowerIsBetter(metric);
        var spoken = loc.Format("a11y.history.tasks.compare", lower ? loc.Format("a11y.history.tasks.lowerBetter", name) : name,
            Name(pair.A, loc), valueA.Spoken, Name(pair.B, loc), valueB.Spoken);
        if (better != 0)
        {
            spoken = loc.Format("a11y.history.tasks.better", spoken, Name(better == 1 ? pair.A : pair.B, loc));
        }

        return new TaskCompareRow(metric, name, loc[TaskMetrics.Key(metric) + ".hint"], lower, loc["history.tasks.lowerBetter"], valueA, valueB,
            better, spoken);
    }

    private static TaskSingleRow SingleRow(TaskMetric metric, TaskStats stats, Localizer loc)
    {
        var raw = TaskMetrics.Raw(metric, stats);
        var value = TaskMetrics.Value(metric, stats, TaskMetrics.Scale(metric, raw, null), TaskEmphasis.Plain, loc);
        var name = loc[TaskMetrics.Key(metric)];
        var lower = TaskMetrics.LowerIsBetter(metric);
        var label = lower ? loc.Format("a11y.history.tasks.lowerBetter", name) : name;
        return new TaskSingleRow(metric, name, loc[TaskMetrics.Key(metric) + ".hint"], lower, loc["history.tasks.lowerBetter"], value,
            HistoryText.Labelled(label, value.Spoken, loc));
    }

    private static TaskEmphasis Emphasis(int better, int side) => better == 0 ? TaskEmphasis.Plain : better == side ? TaskEmphasis.Better : TaskEmphasis.Other;
}
