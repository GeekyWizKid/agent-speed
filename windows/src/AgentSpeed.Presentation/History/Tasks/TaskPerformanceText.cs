using System.Globalization;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>The task performance section's numbers (DESIGN 12, <i>Comparison rows</i>).</summary>
public static class TaskPerformanceText
{
    /// <summary>A rate as a whole percent, rounded half away from zero; <c>–</c> when null.</summary>
    public static string Rate(double? rate) => rate is { } r && double.IsFinite(r)
        ? Math.Round(Math.Clamp(r, 0, 1) * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
        : TaskScoreText.Dash;

    /// <summary>Mean attempts with one decimal in <c>history.tasks.attempts</c> (<c>1.4 次</c>); <c>–</c> when null.</summary>
    public static string Attempts(double? attempts, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return attempts is { } a && double.IsFinite(a)
            ? loc.Format("history.tasks.attempts", Math.Round(a, 1, MidpointRounding.AwayFromZero).ToString("0.0", loc.Culture))
            : TaskScoreText.Dash;
    }

    /// <summary>A time as <c>time.seconds</c> below a minute, whole <c>time.minutes</c> below an hour, then hours; <c>–</c> when null.</summary>
    public static string Duration(double? milliseconds, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (milliseconds is not { } ms || !double.IsFinite(ms))
        {
            return TaskScoreText.Dash;
        }

        var seconds = (long)Math.Round(Math.Max(0, ms) / 1000, MidpointRounding.AwayFromZero);
        if (seconds < 60)
        {
            return loc.Format("time.seconds", seconds);
        }

        var minutes = (long)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);
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
