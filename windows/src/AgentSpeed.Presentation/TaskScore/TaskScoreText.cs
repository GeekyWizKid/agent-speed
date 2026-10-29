using System.Globalization;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.TaskScore;

/// <summary>A piece of a line; <see cref="IsCode"/> marks a check's fixed suite name (<c>cargo test</c>), set in the monospaced face.</summary>
public readonly record struct TaskTextRun(string Text, bool IsCode = false);

/// <summary>The task score's numbers and words on every surface (DESIGN 12, <i>Values and forms</i>, <i>Accessibility</i>).</summary>
public static class TaskScoreText
{
    /// <summary>The app's absent mark (U+2013): never <c>0</c>, which is a real score.</summary>
    public const string Dash = "–";

    /// <summary>The widest number: every 智力 cell is measured with it.</summary>
    public const string WidestNumber = "888";

    public const int MaxScore = 100;

    /// <summary>A score as shown: rounded half away from zero, 0–100.</summary>
    public static int Round(double score) =>
        double.IsFinite(score) ? (int)Math.Clamp(Math.Round(score, MidpointRounding.AwayFromZero), 0, MaxScore) : 0;

    /// <summary><c>unverified</c> and <c>pending</c> have no verified result behind the number.</summary>
    public static bool IsUnproven(TaskState state) => state is TaskState.Unverified or TaskState.Pending;

    public static string Number(int? shown) => shown is { } value ? value.ToString(CultureInfo.InvariantCulture) : Dash;

    /// <summary>
    /// The score words (DESIGN 12, <i>Accessibility</i>): the number as shown, wrapped for an unproven state, for a resting
    /// value and for a set of <paramref name="count"/> ≥ 2 scored agents — <c>智力 72，閒置中，2 個 agent 中最高</c>.
    /// </summary>
    public static string Words(int shown, TaskState state, TaskForm form, int count, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var words = shown.ToString(loc.Culture);
        words = state switch
        {
            TaskState.Unverified => loc.Format("a11y.task.unverified", words),
            TaskState.Pending => loc.Format("a11y.task.pending", words),
            _ => words,
        };
        if (form == TaskForm.Resting)
        {
            words = loc.Format("a11y.task.idle", words);
        }

        return count >= 2 ? loc.Format("a11y.task.top", words, count) : words;
    }

    /// <summary>The score words of <paramref name="facts"/>, or null while none.</summary>
    public static string? Words(TaskScoreFacts facts, int count, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Shown is { } shown ? Words(shown, facts.State, facts.Form, count, loc) : null;
    }

    /// <summary>An agent row's addition to its label (<c>a11y.agent.intel</c>); empty while none.</summary>
    public static string AgentLabel(TaskScoreFacts facts, int count, Localizer loc) =>
        Words(facts, count, loc) is { } words ? loc.Format("a11y.agent.intel", words) : string.Empty;

    /// <summary>The state chip's text (<c>task.state.*</c>).</summary>
    public static string StateName(TaskState state, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return loc[state switch
        {
            TaskState.Unverified => "task.state.unverified",
            TaskState.Failing => "task.state.failing",
            TaskState.Passing => "task.state.passing",
            TaskState.Pending => "task.state.pending",
            _ => "task.state.none",
        }];
    }

    /// <summary>A check by its fixed suite name, else by its class (<c>task.check.*</c>).</summary>
    public static TaskTextRun Check(string? suite, TaskCheckClass checkClass, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (!string.IsNullOrWhiteSpace(suite))
        {
            return new TaskTextRun(suite, IsCode: true);
        }

        return new TaskTextRun(loc[checkClass switch
        {
            TaskCheckClass.Build => "task.check.build",
            TaskCheckClass.Check => "task.check.check",
            _ => "task.check.test",
        }]);
    }

    /// <summary>The card's status line under the number (DESIGN 12, <i>Score card</i>, 2), with the check as its own run.</summary>
    public static IReadOnlyList<TaskTextRun> Status(TaskScoreFacts facts, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(loc);
        switch (facts.State)
        {
            case TaskState.Unverified:
                return [new TaskTextRun(loc["task.status.unverified"])];
            case TaskState.Failing:
                if (facts.OpenFailure is not { } failure)
                {
                    return WithCheck(loc, "task.status.failing.noCount", Check(null, TaskCheckClass.Check, loc));
                }

                var failing = Check(failure.Suite, failure.Class, loc);
                if (failure.Edited)
                {
                    return WithCheck(loc, "task.status.failing.edited", failing);
                }

                return failure.Counted
                    ? WithCheck(loc, "task.status.failing", failing, failure.Errors, Math.Max(failure.Errors, failure.Peak))
                    : WithCheck(loc, "task.status.failing.noCount", failing);
            case TaskState.Passing:
                var passed = facts.LastPass is { } pass ? Check(pass.Suite, pass.Class, loc) : Check(null, TaskCheckClass.Check, loc);
                return WithCheck(loc, facts.LastPass?.BuildOnly == true ? "task.status.passing.buildOnly" : "task.status.passing", passed);
            case TaskState.Pending:
                var before = facts.LastPass is { } last ? Check(last.Suite, last.Class, loc) : Check(null, TaskCheckClass.Check, loc);
                return WithCheck(loc, "task.status.pending", before);
            default:
                return [new TaskTextRun(loc["task.status.none"])];
        }
    }

    public static string Plain(IEnumerable<TaskTextRun> runs) => string.Concat(runs.Select(r => r.Text));

    /// <summary>
    /// A probability as the card shows it: a whole percent rounded half away from zero, <c>&lt;1%</c> below 0.5 % and
    /// <c>&gt;99%</c> from 99.5 %.
    /// </summary>
    public static string Percent(double probability)
    {
        var percent = Clamped(probability) * 100;
        if (percent < 0.5)
        {
            return "<1%";
        }

        return percent >= 99.5
            ? ">99%"
            : Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>The percentage spoken as shown (<c>a11y.task.below</c> / <c>a11y.task.above</c> for the clamps).</summary>
    public static string PercentWords(double probability, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var percent = Clamped(probability) * 100;
        if (percent < 0.5)
        {
            return loc.Format("a11y.task.below", 1);
        }

        return percent >= 99.5 ? loc.Format("a11y.task.above", 99) : Percent(probability);
    }

    private static double Clamped(double probability) => double.IsFinite(probability) ? Math.Clamp(probability, 0, 1) : 0;

    /// <summary>Formats <paramref name="key"/> with the check first, keeping the check's run apart in every language.</summary>
    private static IReadOnlyList<TaskTextRun> WithCheck(Localizer loc, string key, TaskTextRun check, params object[] rest)
    {
        const string Marker = "\u0001";
        var text = loc.Format(key, [Marker, .. rest]);
        var at = text.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return [new TaskTextRun(text)];
        }

        var runs = new List<TaskTextRun>(3);
        if (at > 0)
        {
            runs.Add(new TaskTextRun(text[..at]));
        }

        runs.Add(check);
        if (at + Marker.Length < text.Length)
        {
            runs.Add(new TaskTextRun(text[(at + Marker.Length)..]));
        }

        return runs;
    }
}
