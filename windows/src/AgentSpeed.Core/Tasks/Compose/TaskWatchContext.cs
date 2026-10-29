using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tasks;

/// <summary>A watched task that completed in a composer (METRICS 11.10, Counting): its watch generation, model and record.</summary>
internal readonly record struct TaskDone(long Generation, string? Model, ReportTaskRecord Record);

/// <summary>A completed watched task with its agent, for the engine's task ledger.</summary>
internal readonly record struct TaskCompletion(string AgentId, SourceKind Source, string? Model, long Generation, ReportTaskRow? Row);

/// <summary>
/// Live reporting's watch as the composers of one monitor see it (METRICS 11.10, Watch): the published watch, whether the
/// lines being read are seeded, the clock, the agents that hold a watched task and the completions waiting for the engine.
/// Owned by the monitor thread.
/// </summary>
internal sealed class TaskWatchContext(Func<long> clock)
{
    private readonly Dictionary<string, TranscriptParser> _watching = new(StringComparer.Ordinal);
    private readonly List<TaskCompletion> _completions = [];

    public ReportTaskWatch Watch { get; set; } = ReportTaskWatch.Off;

    /// <summary>The lines being read are seeded: no task they start is watched.</summary>
    public bool Seeding { get; set; }

    /// <summary>The generation a task whose first event is at <paramref name="eventMs"/> is watched in, read now; 0 for history.</summary>
    public long Mark(long eventMs) => Seeding ? 0 : Watch.Mark(eventMs, clock());

    /// <summary>Takes a parser's completed tasks and follows whether it still holds a watched task.</summary>
    public void Collect(TranscriptParser parser)
    {
        if (parser.Tasks is not { } tasks)
        {
            return;
        }

        foreach (var done in tasks.TakeDone())
        {
            _completions.Add(new TaskCompletion(parser.AgentId, parser.Source, done.Model, done.Generation, ReportTaskRow.Of(done.Record)));
        }

        if (tasks.HasWatchedTask)
        {
            _watching[parser.AgentId] = parser;
        }
        else
        {
            _watching.Remove(parser.AgentId);
        }
    }

    /// <summary>
    /// The per-tick part (rule 2 of METRICS 11.10 <i>Counting</i>): tasks of a stale generation are unwatched and a watched
    /// task whose agent has been quiet for 30 minutes completes.
    /// </summary>
    public void Tick(long nowMs)
    {
        if (_watching.Count == 0)
        {
            return;
        }

        var generation = Watch.On ? Watch.Generation : 0;
        foreach (var parser in _watching.Values.ToArray())
        {
            parser.Tasks?.Unwatch(generation);
            parser.Tasks?.CompleteIfQuiet(nowMs, parser.LastLineMs ?? long.MinValue / 2);
            Collect(parser);
        }
    }

    /// <summary>The agent's file went away or its source was disabled: its task is unwatched.</summary>
    public void Forget(string agentId)
    {
        if (_watching.Remove(agentId, out var parser))
        {
            parser.Tasks?.Unwatch(0);
        }
    }

    public List<TaskCompletion> TakeCompletions()
    {
        if (_completions.Count == 0)
        {
            return [];
        }

        var taken = new List<TaskCompletion>(_completions);
        _completions.Clear();
        return taken;
    }
}
