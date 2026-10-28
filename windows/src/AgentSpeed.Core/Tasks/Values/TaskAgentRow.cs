namespace AgentSpeed.Core.Tasks;

/// <summary>An agent as the task values see it at one instant (phase after the 300-second staleness rule, 6.7).</summary>
internal sealed record TaskAgentView(string Id, SourceKind Source, AgentPhase Phase, long? LastLineMs, string? ParentId, IReadOnlyList<TaskEvent> Events);

/// <summary>
/// One agent's task values at one instant (METRICS 6.9 <i>Values</i>, 6.11 <i>Predictions</i>): its value, its current
/// task's evaluation (state, dimensions, facts) and start, and the predictions with its source's coefficients.
/// </summary>
internal sealed record TaskAgentRow(
    TaskAgentView Agent,
    TaskValue Value,
    TaskEvaluation? Task,
    long? TaskStartMs,
    TaskPrediction? Predictions,
    TaskModels Models)
{
    public const long StaleMs = 300_000;

    /// <summary>6.7 step 1: a phase older than 300 s without a new line is idle.</summary>
    public static AgentPhase EffectivePhase(AgentPhase phase, long? lastLineMs, long nowMs) =>
        phase != AgentPhase.Idle && lastLineMs is { } last && nowMs - last > StaleMs ? AgentPhase.Idle : phase;

    /// <param name="nowMs">The instant; events after it are not read yet (null: everything read).</param>
    public static TaskAgentRow Of(TaskAgentView agent, long? nowMs, TaskModels models)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var task = TaskSegments.Current(agent.Events, nowMs);
        var evaluation = task is null ? null : TaskEvaluation.Of(task.Events, nowMs);
        var value = TaskValues.Of(evaluation, agent.Phase == AgentPhase.Idle);
        var predictions = evaluation is { Score: not null } ? TaskPredictions.Of(evaluation, models) : null;
        return new TaskAgentRow(agent, value, evaluation, task?.StartMs, predictions, models);
    }
}
