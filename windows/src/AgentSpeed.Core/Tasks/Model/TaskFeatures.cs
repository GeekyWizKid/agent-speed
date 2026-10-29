namespace AgentSpeed.Core.Tasks;

/// <summary>
/// The features of a 6.9 evaluation (METRICS 6.11 <i>Features</i>): <c>passing</c> and <c>failing</c> are 1 in that
/// state, <c>conv = C / 25</c>, <c>ig</c>, <c>loop</c>, <c>osc</c>, and <c>size = min(1, log2(1 + n) / 9)</c>.
/// </summary>
public readonly record struct TaskFeatures(double Passing, double Failing, double Conv, double Ig, double Loop, double Osc, double Size)
{
    /// <summary><c>snapshot_features</c>.</summary>
    public static TaskFeatures Of(TaskEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return new TaskFeatures(
            evaluation.State == TaskState.Passing ? 1.0 : 0.0,
            evaluation.State == TaskState.Failing ? 1.0 : 0.0,
            evaluation.Dims.C / TaskEvaluation.CMax,
            evaluation.Ig,
            evaluation.Loop,
            evaluation.Osc,
            Math.Min(1.0, Math.Log2(1 + evaluation.CountedActions) / 9));
    }

    /// <summary>The success model's features in its order: passing, failing, conv, ig, loop, osc, size.</summary>
    internal void Success(Span<double> x)
    {
        x[0] = Passing;
        x[1] = Failing;
        x[2] = Conv;
        x[3] = Ig;
        x[4] = Loop;
        x[5] = Osc;
        x[6] = Size;
    }

    /// <summary>The stall model's features in its order: ig, loop, osc, failing, size.</summary>
    internal void Stall(Span<double> x)
    {
        x[0] = Ig;
        x[1] = Loop;
        x[2] = Osc;
        x[3] = Failing;
        x[4] = Size;
    }
}

/// <summary>
/// The features of the fix that follows a failing run, as they stand right after it (METRICS 6.11 <i>Features</i>):
/// <c>attempts = min(1, (a − 1) / 4)</c>, <c>errors = min(1, log2(1 + F) / 8)</c>, <c>trend = F / F_peak</c>, <c>osc</c>, <c>ig</c>.
/// </summary>
public readonly record struct TaskFixFeatures(double Attempts, double Errors, double Trend, double Osc, double Ig)
{
    /// <summary>
    /// <c>fix_features</c> of a failing run of <paramref name="task"/>: the task evaluated up to and including that run.
    /// </summary>
    internal static TaskFixFeatures Of(TaskEvaluation task, TaskAct failingRun)
    {
        var prefix = task.Prefix(failingRun.Index + 1);
        var (attempts, peak) = prefix.EpisodeOf(failingRun);
        return new TaskFixFeatures(
            Math.Min(1.0, (attempts - 1) / 4.0),
            Math.Min(1.0, Math.Log2(1.0 + failingRun.Errors) / 8),
            TaskEvaluation.Ratio(failingRun.Errors, peak),
            prefix.Osc,
            prefix.Ig);
    }

    /// <summary>The fix model's features in its order.</summary>
    internal void Fix(Span<double> x)
    {
        x[0] = Attempts;
        x[1] = Errors;
        x[2] = Trend;
        x[3] = Osc;
        x[4] = Ig;
    }
}
