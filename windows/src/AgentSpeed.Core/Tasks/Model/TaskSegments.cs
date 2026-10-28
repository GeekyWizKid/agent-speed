namespace AgentSpeed.Core.Tasks;

/// <summary>One task of an agent's stream: its start and its events in file order.</summary>
public sealed record TaskSlice(long StartMs, IReadOnlyList<TaskEvent> Events);

/// <summary>
/// Tasks of one agent's stream (METRICS 6.9 <i>Tasks</i>): a prompt starts one, a continuation joins the current one,
/// and the events before the first prompt form a task of their own, which starts at its first event.
/// </summary>
public static class TaskSegments
{
    /// <summary><c>split_tasks</c>.</summary>
    public static List<TaskSlice> Split(IReadOnlyList<TaskEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var tasks = new List<TaskSlice>();
        foreach (var (start, end) in Ranges(events))
        {
            tasks.Add(new TaskSlice(events[start].T, Copy(events, start, end)));
        }

        return tasks;
    }

    /// <summary>
    /// <c>current_task</c>: the latest task of the events with <c>t ≤ nowMs</c> (every event when
    /// <paramref name="nowMs"/> is null), or null when there is none.
    /// </summary>
    public static TaskSlice? Current(IReadOnlyList<TaskEvent> events, long? nowMs)
    {
        ArgumentNullException.ThrowIfNull(events);
        var visible = nowMs is { } now && !AllVisible(events, now) ? Visible(events, now) : events;
        if (visible.Count == 0)
        {
            return null;
        }

        var start = 0;
        for (var i = visible.Count - 1; i > 0; i--)
        {
            if (visible[i] is TaskPrompt { Continuation: false })
            {
                start = i;
                break;
            }
        }

        return new TaskSlice(visible[start].T, Copy(visible, start, visible.Count));
    }

    /// <summary><c>task_end</c>: the latest <c>t</c> of the task's start, its events and their outcomes.</summary>
    public static long End(TaskSlice task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return End(task.StartMs, task.Events, 0, task.Events.Count);
    }

    internal static long End(long startMs, IReadOnlyList<TaskEvent> events, int start, int end)
    {
        var last = startMs;
        for (var i = start; i < end; i++)
        {
            var e = events[i];
            last = Math.Max(last, e.T);
            if (e is TaskAction { Outcome.T: { } t })
            {
                last = Math.Max(last, t);
            }
        }

        return last;
    }

    /// <summary>The <c>[start, end)</c> index ranges of the tasks of <paramref name="events"/>, in order.</summary>
    internal static List<(int Start, int End)> Ranges(IReadOnlyList<TaskEvent> events)
    {
        var ranges = new List<(int Start, int End)>();
        var start = 0;
        for (var i = 1; i < events.Count; i++)
        {
            if (events[i] is TaskPrompt { Continuation: false })
            {
                ranges.Add((start, i));
                start = i;
            }
        }

        if (events.Count > 0)
        {
            ranges.Add((start, events.Count));
        }

        return ranges;
    }

    private static bool AllVisible(IReadOnlyList<TaskEvent> events, long now)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].T > now)
            {
                return false;
            }
        }

        return true;
    }

    private static List<TaskEvent> Visible(IReadOnlyList<TaskEvent> events, long now)
    {
        var visible = new List<TaskEvent>(events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].T <= now)
            {
                visible.Add(events[i]);
            }
        }

        return visible;
    }

    private static TaskEvent[] Copy(IReadOnlyList<TaskEvent> events, int start, int end)
    {
        var copy = new TaskEvent[end - start];
        for (var i = start; i < end; i++)
        {
            copy[i - start] = events[i];
        }

        return copy;
    }
}
