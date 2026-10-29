namespace AgentSpeed.Core.Tasks;

/// <summary>
/// P(success), P(stall) and, while failing, P(fix fails) of an agent's current task, with their percentages rounded half
/// away from zero and the features they came from (METRICS 6.11 <i>Predictions</i>).
/// </summary>
public sealed record TaskPrediction(
    double Success,
    double Stall,
    double? Fix,
    int SuccessShown,
    int StallShown,
    int? FixShown,
    TaskFeatures Features,
    TaskFixFeatures? FixFeatures);

/// <summary><c>predictions</c> (METRICS 6.11).</summary>
public static class TaskPredictions
{
    private const double EtaLimit = 35.0;

    /// <summary>The predictions of the current task of <paramref name="events"/> at <paramref name="nowMs"/>; null without a score.</summary>
    public static TaskPrediction? Of(IReadOnlyList<TaskEvent> events, long? nowMs, TaskModels models)
    {
        ArgumentNullException.ThrowIfNull(events);
        var task = TaskSegments.Current(events, nowMs);
        return task is null ? null : Of(TaskEvaluation.Of(task.Events, nowMs), models);
    }

    /// <summary>The predictions of a current task already evaluated; null without a score.</summary>
    public static TaskPrediction? Of(TaskEvaluation currentTask, TaskModels models)
    {
        ArgumentNullException.ThrowIfNull(currentTask);
        ArgumentNullException.ThrowIfNull(models);
        if (currentTask.Score is null)
        {
            return null;
        }

        var features = currentTask.Features;
        Span<double> x = stackalloc double[7];
        features.Success(x);
        var success = Sigmoid(Linear(models.Success.Coefficients, x[..7]));
        features.Stall(x);
        var stall = Sigmoid(Linear(models.Stall.Coefficients, x[..5]));

        double? fix = null;
        TaskFixFeatures? fixFeatures = null;
        if (currentTask.State == TaskState.Failing && currentTask.OpenFailureRun() is { } open)
        {
            var f = TaskFixFeatures.Of(currentTask, open);
            f.Fix(x);
            fix = Sigmoid(Linear(models.Fix.Coefficients, x[..5]));
            fixFeatures = f;
        }

        return new TaskPrediction(
            success,
            stall,
            fix,
            TaskRounding.HalfAwayFromZero(success * 100),
            TaskRounding.HalfAwayFromZero(stall * 100),
            fix is { } p ? TaskRounding.HalfAwayFromZero(p * 100) : null,
            features,
            fixFeatures);
    }

    /// <summary><c>σ(η) = 1 / (1 + e^(−η))</c> with <c>η</c> clamped to [−35, 35] (as Python's <c>max(−35, min(35, η))</c>).</summary>
    public static double Sigmoid(double eta)
    {
        var upper = eta < EtaLimit ? eta : EtaLimit;
        var clamped = upper > -EtaLimit ? upper : -EtaLimit;
        return 1.0 / (1.0 + Math.Exp(-clamped));
    }

    /// <summary><c>β₀ + Σ βᵢ·xᵢ</c>, the sum formed before the intercept is added, pairs beyond either list ignored.</summary>
    internal static double Linear(IReadOnlyList<double> coefficients, ReadOnlySpan<double> features)
    {
        var sum = 0.0;
        var pairs = Math.Min(coefficients.Count - 1, features.Length);
        for (var i = 0; i < pairs; i++)
        {
            sum += coefficients[i + 1] * features[i];
        }

        return coefficients[0] + sum;
    }
}
