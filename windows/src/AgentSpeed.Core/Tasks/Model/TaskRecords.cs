namespace AgentSpeed.Core.Tasks;

/// <summary><c>task_records</c> (METRICS 6.11 <i>Task records</i>): the history analysis's records of one agent's stream.</summary>
public static class TaskRecords
{
    private const long CompleteIdleMs = 30 * 60_000;
    private static readonly int[] Snapshots = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512];

    /// <summary>
    /// A record for every task of the whole stream with at least one action. <see cref="TaskRecord.AgentId"/>,
    /// <see cref="TaskRecord.Source"/> and <see cref="TaskRecord.Day"/> are left for the caller. A record is complete
    /// when another task follows it or <c>analysisMs − lastLineMs ≥ 30 min</c>; its model and effort come from the last
    /// note whose time is at most its end.
    /// </summary>
    public static List<TaskRecord> Of(IReadOnlyList<TaskEvent> events, long analysisMs, long lastLineMs, IReadOnlyList<TaskModelNote> notes)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(notes);
        var ranges = TaskSegments.Ranges(events);
        var records = new List<TaskRecord>();
        for (var i = 0; i < ranges.Count; i++)
        {
            var (start, end) = ranges[i];
            if (!HasAction(events, start, end))
            {
                continue;
            }

            var startMs = events[start].T;
            var ev = TaskEvaluation.Of(TaskActs.Of(events, start, end, null));
            var endMs = TaskSegments.End(startMs, events, start, end);
            string? model = null;
            string? effort = null;
            foreach (var note in notes)
            {
                if (note.T <= endMs)
                {
                    (model, effort) = (note.Model, note.Effort);
                }
            }

            records.Add(new TaskRecord
            {
                Ordinal = records.Count,
                StartMs = startMs,
                EndMs = endMs,
                Complete = i < ranges.Count - 1 || analysisMs - lastLineMs >= CompleteIdleMs,
                Model = model,
                Effort = effort,
                State = ev.State,
                Score = ev.Score,
                Dims = ev.Dims,
                Counts = ev.Counts,
                FirstPassMs = ev.FirstPassMs,
                Success = ev.State == TaskState.Passing,
                Stalled = ev.StallEnds.Count > 0,
                Snapshots = SnapshotRows(ev),
                FixRows = FixRows(ev),
            });
        }

        return records;
    }

    private static bool HasAction(IReadOnlyList<TaskEvent> events, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (events[i] is TaskAction)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>[n, passing, failing, conv, ig, loop, osc, size, yStall]</c> for every <c>n</c> in 1, 2, 4, … 512 up to the
    /// task's counted actions, from the task up to its <c>n</c>-th counted action, when that state is not none.
    /// </summary>
    private static List<double[]> SnapshotRows(TaskEvaluation task)
    {
        var rows = new List<double[]>();
        var counted = task.Source.Counted;
        foreach (var n in Snapshots)
        {
            if (n > counted.Count)
            {
                break;
            }

            var prefix = task.Prefix(counted[n - 1].Index + 1);
            if (prefix.State == TaskState.None)
            {
                continue;
            }

            var f = prefix.Features;
            var stalls = false;
            foreach (var end in task.StallEnds)
            {
                stalls |= end > n;
            }

            rows.Add([n, f.Passing, f.Failing, f.Conv, f.Ig, f.Loop, f.Osc, f.Size, stalls ? 1 : 0]);
        }

        return rows;
    }

    /// <summary><c>[attempts, errors, trend, osc, ig, yFix]</c> for every failing run, from the task up to that run.</summary>
    private static List<double[]> FixRows(TaskEvaluation task)
    {
        var rows = new List<double[]>();
        foreach (var run in task.FailingRuns())
        {
            var f = TaskFixFeatures.Of(task, run);
            rows.Add([f.Attempts, f.Errors, f.Trend, f.Osc, f.Ig, task.FixLabel(run)]);
        }

        return rows;
    }
}
