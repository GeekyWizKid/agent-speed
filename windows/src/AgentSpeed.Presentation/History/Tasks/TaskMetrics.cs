using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>The five compared metrics, in row order (DESIGN 12, <i>Comparison rows</i>).</summary>
public enum TaskMetric
{
    Success,
    Stall,
    FixFail,
    Attempts,
    FirstPass,
}

/// <summary>How a value reads beside the other side's.</summary>
public enum TaskEmphasis
{
    /// <summary>Regular, in the label color: no side is marked.</summary>
    Plain,

    /// <summary>Semibold label color.</summary>
    Better,

    /// <summary>Regular secondary, beside a better value.</summary>
    Other,
}

/// <summary>One shown value with its bar fraction and tooltip.</summary>
/// <param name="Fraction">The track's fill, 0–1 of the metric's scale; 0 when null.</param>
/// <param name="Tooltip">A rate's counts, or the null value's reason; null when it has none.</param>
/// <param name="Spoken">What a screen reader says for it: the text, or a null value's tooltip.</param>
public sealed record TaskValue(string Text, double? Raw, double Fraction, string? Tooltip, TaskEmphasis Emphasis, string Spoken)
{
    public bool IsNull => Raw is null;

    public bool HasTooltip => !string.IsNullOrEmpty(Tooltip);
}

/// <summary>The metrics' names, hints, scales, formats and the "better" rule.</summary>
public static class TaskMetrics
{
    /// <summary>Below this many scored tasks a sample is small: its chip shows and no side is marked better.</summary>
    public const int LowSample = 10;

    /// <summary>Rates must differ by a percentage point to mark a side.</summary>
    public const double RateDifference = 0.01;

    /// <summary>Attempts and times must differ by 5 % of the larger value.</summary>
    public const double RelativeDifference = 0.05;

    public const double AttemptsScaleFloor = 3;
    public const double FirstPassScaleFloorMs = 5 * 60_000;
    public const double ScaleHeadroom = 1.25;

    public static IReadOnlyList<TaskMetric> All { get; } = Enum.GetValues<TaskMetric>();

    public static string Key(TaskMetric metric) => metric switch
    {
        TaskMetric.Success => "history.tasks.metric.success",
        TaskMetric.Stall => "history.tasks.metric.stall",
        TaskMetric.FixFail => "history.tasks.metric.fixFail",
        TaskMetric.Attempts => "history.tasks.metric.attempts",
        _ => "history.tasks.metric.firstPass",
    };

    /// <summary>The model table's header: the <c>.short</c> name where one exists.</summary>
    public static string ShortKey(TaskMetric metric) => metric switch
    {
        TaskMetric.Attempts => "history.tasks.metric.attempts.short",
        TaskMetric.FirstPass => "history.tasks.metric.firstPass.short",
        _ => Key(metric),
    };

    public static bool LowerIsBetter(TaskMetric metric) => metric != TaskMetric.Success;

    public static bool IsRate(TaskMetric metric) => metric is TaskMetric.Success or TaskMetric.Stall or TaskMetric.FixFail;

    public static double? Raw(TaskMetric metric, TaskStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (!stats.HasRecords)
        {
            return null;
        }

        return metric switch
        {
            TaskMetric.Success => stats.SuccessRate,
            TaskMetric.Stall => stats.StallRate,
            TaskMetric.FixFail => stats.FixFailureRate,
            TaskMetric.Attempts => stats.RecoverAttempts,
            _ => stats.TimeToFirstPassMs,
        };
    }

    public static string Format(TaskMetric metric, double? raw, Localizer loc) => metric switch
    {
        TaskMetric.Attempts => TaskPerformanceText.Attempts(raw, loc),
        TaskMetric.FirstPass => TaskPerformanceText.Duration(raw, loc),
        _ => TaskPerformanceText.Rate(raw),
    };

    /// <summary>
    /// The track's full length: rates 0–100 %; attempts and times the larger of the shown values × 1.25, at least 3
    /// attempts or 5 minutes.
    /// </summary>
    public static double Scale(TaskMetric metric, double? a, double? b)
    {
        if (IsRate(metric))
        {
            return 1;
        }

        var larger = Math.Max(a ?? 0, b ?? 0) * ScaleHeadroom;
        return Math.Max(metric == TaskMetric.Attempts ? AttemptsScaleFloor : FirstPassScaleFloorMs, larger);
    }

    /// <summary>
    /// Which side is better: only when both have at least <see cref="LowSample"/> scored tasks, the values differ by a
    /// percentage point (rates) or 5 % of the larger value, and the two texts as shown differ (two sides that both read
    /// <c>5 分鐘</c> are not ranked) — higher success, lower everything else. 0 none, 1 A, 2 B.
    /// </summary>
    public static int Better(TaskMetric metric, double? a, double? b, int scoredA, int scoredB, string? shownA = null, string? shownB = null)
    {
        if (a is not { } va || b is not { } vb || scoredA < LowSample || scoredB < LowSample)
        {
            return 0;
        }

        if (shownA is not null && string.Equals(shownA, shownB, StringComparison.Ordinal))
        {
            return 0;
        }

        var gap = Math.Abs(va - vb);
        var needed = IsRate(metric) ? RateDifference : RelativeDifference * Math.Max(Math.Abs(va), Math.Abs(vb));
        if (gap < needed - 1e-12 || gap == 0)
        {
            return 0;
        }

        var aWins = LowerIsBetter(metric) ? va < vb : va > vb;
        return aWins ? 1 : 2;
    }

    /// <summary>A value as shown, with its fraction of <paramref name="scale"/>, its tooltip and its spoken form.</summary>
    public static TaskValue Value(TaskMetric metric, TaskStats stats, double scale, TaskEmphasis emphasis, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var raw = Raw(metric, stats);
        var text = Format(metric, raw, loc);
        var tooltip = Tooltip(metric, stats, raw, loc);
        var fraction = raw is { } r && scale > 0 ? Math.Clamp(r / scale, 0, 1) : 0;
        return new TaskValue(text, raw, fraction, tooltip, raw is null ? TaskEmphasis.Plain : emphasis, raw is null && tooltip is not null ? tooltip : text);
    }

    private static string? Tooltip(TaskMetric metric, TaskStats stats, double? raw, Localizer loc)
    {
        if (!stats.HasRecords)
        {
            return null;
        }

        return metric switch
        {
            TaskMetric.Success when raw is not null && stats.Successes is { } n => loc.Format("history.tasks.rate.scored", n, stats.ScoredTasks),
            TaskMetric.Stall when raw is not null && stats.Stalls is { } n => loc.Format("history.tasks.rate.tasks", n, stats.Tasks),
            TaskMetric.FixFail when raw is not null && stats is { FixFailures: { } n, FixAttempts: { } d } => loc.Format("history.tasks.rate.fixes", n, d),
            TaskMetric.Attempts when raw is null => loc["history.tasks.noValue.attempts"],
            TaskMetric.FirstPass when raw is null => loc["history.tasks.noValue.firstPass"],
            _ => null,
        };
    }
}
