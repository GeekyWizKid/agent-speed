namespace AgentSpeed.Core.Tasks;

/// <summary>The form of a value (METRICS 6.9 <i>Values</i>).</summary>
public enum TaskValueForm
{
    /// <summary>No current task, or one without a score.</summary>
    None,

    /// <summary>The agent is idle: its current task's final score, shown pale.</summary>
    Resting,

    /// <summary>The agent is working: its current task's score.</summary>
    Live,
}

/// <summary>An agent's value: its form, score and shown score, and its current task's state (null without a task).</summary>
public readonly record struct TaskValue(TaskValueForm Form, double? Score, int? Shown, TaskState? State)
{
    public static TaskValue None => default;
}

/// <summary>One agent of a set, for <see cref="TaskValues.Highest"/>.</summary>
public readonly record struct TaskCandidate(string AgentId, long LastLineMs, TaskValue Value);

/// <summary>
/// A set's or group's value and the agent that supplied it (null for none), with that agent's latest line, so values of
/// parts of a set merge into the value of the whole (<see cref="Merge"/>).
/// </summary>
public readonly record struct TaskSetValue(TaskValueForm Form, double? Score, int? Shown, string? AgentId)
{
    public static TaskSetValue None => default;

    /// <summary>The supplying agent's <c>lastLineMs</c> (ties prefer the latest).</summary>
    public long LastLineMs { get; init; }

    /// <summary>One agent's own value as a set of one.</summary>
    public static TaskSetValue Of(string agentId, long lastLineMs, TaskValue value) =>
        value.Score is null ? None : new TaskSetValue(value.Form, value.Score, value.Shown, agentId) { LastLineMs = lastLineMs };

    /// <summary>The highest rule over two disjoint parts: the value of their union.</summary>
    public static TaskSetValue Merge(TaskSetValue a, TaskSetValue b)
    {
        if (b.Score is null)
        {
            return a;
        }

        if (a.Score is null)
        {
            return b;
        }

        return TaskValues.Highest([a.AsCandidate(), b.AsCandidate()]);
    }

    private TaskCandidate AsCandidate() => new(AgentId ?? string.Empty, LastLineMs, new TaskValue(Form, Score, Shown, null));
}

/// <summary>Agent, group and set values (METRICS 6.9 <i>Values</i>).</summary>
public static class TaskValues
{
    /// <summary><c>agent_value</c>: the current task's score by the agent's phase (<paramref name="idle"/> after staleness).</summary>
    public static TaskValue Agent(IReadOnlyList<TaskEvent> events, bool idle, long? nowMs)
    {
        ArgumentNullException.ThrowIfNull(events);
        var task = TaskSegments.Current(events, nowMs);
        return Of(task is null ? null : TaskEvaluation.Of(task.Events, nowMs), idle);
    }

    /// <summary>The same, from the current task's evaluation (null when there is no current task).</summary>
    public static TaskValue Of(TaskEvaluation? currentTask, bool idle)
    {
        if (currentTask is null)
        {
            return TaskValue.None;
        }

        if (currentTask.Score is not { } score)
        {
            return new TaskValue(TaskValueForm.None, null, null, currentTask.State);
        }

        return new TaskValue(idle ? TaskValueForm.Resting : TaskValueForm.Live, score, currentTask.Shown, currentTask.State);
    }

    /// <summary>
    /// <c>highest</c>: the highest live value, else the highest resting value, else none. Among the agents with that exact
    /// score in that form, the one with the latest <see cref="TaskCandidate.LastLineMs"/>, then the smallest id (ordinal).
    /// </summary>
    public static TaskSetValue Highest(IEnumerable<TaskCandidate> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        TaskCandidate? live = null;
        TaskCandidate? resting = null;
        foreach (var agent in agents)
        {
            if (agent.Value.Score is null)
            {
                continue;
            }

            switch (agent.Value.Form)
            {
                case TaskValueForm.Live:
                    live = Better(live, agent);
                    break;
                case TaskValueForm.Resting:
                    resting = Better(resting, agent);
                    break;
            }
        }

        if ((live ?? resting) is not { } best)
        {
            return default;
        }

        var value = best.Value.Score!.Value;
        return new TaskSetValue(best.Value.Form, value, TaskRounding.HalfAwayFromZero(value), best.AgentId) { LastLineMs = best.LastLineMs };
    }

    private static TaskCandidate Better(TaskCandidate? current, TaskCandidate agent)
    {
        if (current is not { } held)
        {
            return agent;
        }

        var score = agent.Value.Score!.Value;
        var heldScore = held.Value.Score!.Value;
        if (score != heldScore)
        {
            return score > heldScore ? agent : held;
        }

        if (agent.LastLineMs != held.LastLineMs)
        {
            return agent.LastLineMs > held.LastLineMs ? agent : held;
        }

        return string.CompareOrdinal(agent.AgentId, held.AgentId) < 0 ? agent : held;
    }
}
