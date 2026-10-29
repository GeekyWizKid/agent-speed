namespace AgentSpeed.Core.Tasks;

/// <summary>
/// An agent's current task as the score card shows it (METRICS 6.9 <i>Task facts</i>, 6.11 <i>Predictions</i>): when it
/// started, its evaluation (state, score, dimensions, facts; null without a current task), the three predictions while it
/// has a score, and the source's models with their "prior" / "calibrated on N tasks" flags. It changes only when an event
/// is read or the calibration changes, never with time alone.
/// </summary>
public sealed record AgentTaskStatus(long? TaskStartMs, TaskEvaluation? Result, TaskPrediction? Predictions, TaskModels Models)
{
    /// <summary>Evaluates the current task of <paramref name="events"/> with everything read (the live engine).</summary>
    public static AgentTaskStatus Of(IReadOnlyList<TaskEvent> events, TaskModels models)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(models);
        var task = TaskSegments.Current(events, null);
        var result = task is null ? null : TaskEvaluation.Of(task.Events);
        var predictions = result is { Score: not null } ? TaskPredictions.Of(result, models) : null;
        return new AgentTaskStatus(task?.StartMs, result, predictions, models);
    }

    /// <summary>The agent's value by its phase after the staleness rule (6.9 <i>Values</i>).</summary>
    public TaskValue Value(bool idle) => TaskValues.Of(Result, idle);
}
