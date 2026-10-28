using System.Globalization;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>The task performance section's numbers (DESIGN 12, <i>Comparison rows</i>).</summary>
public static class TaskPerformanceText
{
    /// <summary>
    /// A rate as a whole percent, rounded half away from zero after the float noise of the division is removed (so
    /// 29 / 200 reads 15 %, not 14 %); <c>–</c> when null.
    /// </summary>
    public static string Rate(double? rate) => rate is { } r && double.IsFinite(r)
        ? Math.Round(Math.Round(Math.Clamp(r, 0, 1) * 100, 9), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
        : TaskScoreText.Dash;

    /// <summary>Mean attempts with one decimal in <c>history.tasks.attempts</c> (<c>1.4 次</c>); <c>–</c> when null.</summary>
    public static string Attempts(double? attempts, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return attempts is { } a && double.IsFinite(a)
            ? loc.Format("history.tasks.attempts", Math.Round(a, 1, MidpointRounding.AwayFromZero).ToString("0.0", loc.Culture))
            : TaskScoreText.Dash;
    }

    /// <summary>
    /// A time as <c>time.seconds</c> below a minute, whole <c>time.minutes</c> below an hour, then hours and minutes; each
    /// rounding taken once from milliseconds, half away from zero, the unit following the rounded value (DESIGN 12): 59.4 s
    /// reads 59 秒, 59.6 s reads 1 分鐘. <c>–</c> when null.
    /// </summary>
    public static string Duration(double? milliseconds, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (milliseconds is not { } ms || !double.IsFinite(ms))
        {
            return TaskScoreText.Dash;
        }

        ms = Math.Max(0, ms);
        var seconds = (long)Math.Round(ms / 1000, MidpointRounding.AwayFromZero);
        if (seconds < 60)
        {
            return loc.Format("time.seconds", seconds);
        }

        var minutes = (long)Math.Round(ms / 60_000, MidpointRounding.AwayFromZero);
        if (minutes < 60)
        {
            return loc.Format("time.minutes", minutes);
        }

        var (hours, rest) = Math.DivRem(minutes, 60);
        return rest == 0 ? loc.Format("time.hours", hours) : loc.Format("time.hoursMinutes", hours, rest);
    }

    /// <summary>A score statistic as shown (rounded half away from zero), <c>–</c> when null.</summary>
    public static string Score(double? score) => score is { } s ? TaskScoreText.Number(TaskScoreText.Round(s)) : TaskScoreText.Dash;

    /// <summary>A count with grouping separators.</summary>
    public static string Count(int value, Localizer loc) => HistoryText.FullNumber(value, loc);
}
