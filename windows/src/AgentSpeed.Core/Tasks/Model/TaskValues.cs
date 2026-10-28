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

/// <summary>A set's or group's value and the agent that supplied it (null for none).</summary>
public readonly record struct TaskSetValue(TaskValueForm Form, double? Score, int? Shown, string? AgentId);

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
        return new TaskSetValue(best.Value.Form, value, TaskRounding.HalfAwayFromZero(value), best.AgentId);
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
