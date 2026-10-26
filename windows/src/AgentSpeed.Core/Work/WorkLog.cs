namespace AgentSpeed.Core.Work;

/// <summary>The last <see cref="MaxActions"/> actions of one file (METRICS 6.10); results find their call by id. Not thread-safe.</summary>
internal sealed class WorkLog
{
    public const int MaxActions = 64;

    private readonly List<(string CallId, WorkAction Action)> _entries = new(MaxActions + 1);
    private readonly Dictionary<string, long> _sequenceById = new(StringComparer.Ordinal);
    private long _firstSequence;
    private WorkAction[]? _actions;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<WorkAction> Actions => _actions ??= _entries.ConvertAll(e => e.Action).ToArray();

    /// <summary>Logs a line's calls, then its outcomes; true when the log changed.</summary>
    public bool Apply(in LineTools tools, long atMs)
    {
        var changed = false;
        foreach (var call in tools.Calls ?? [])
        {
            changed |= Call(call, atMs);
        }

        foreach (var outcome in tools.Outcomes ?? [])
        {
            changed |= Outcome(outcome);
        }

        return changed;
    }

    /// <summary>Adds a call unless its id is already logged or it names no tool; true when the log changed.</summary>
    public bool Call(in ToolCall call, long atMs)
    {
        if (call.Tool is not { } tool || _sequenceById.ContainsKey(call.Id))
        {
            return false;
        }

        _sequenceById[call.Id] = _firstSequence + _entries.Count;
        _entries.Add((call.Id, new WorkAction(atMs, tool, call.Digest)));
        if (_entries.Count > MaxActions)
        {
            _sequenceById.Remove(_entries[0].CallId);
            _entries.RemoveAt(0);
            _firstSequence++;
        }

        _actions = null;
        return true;
    }

    /// <summary>Sets the outcome of a logged call; an unknown outcome changes nothing. True when the log changed.</summary>
    public bool Outcome(in ToolOutcome outcome)
    {
        if (outcome.Failed is not { } failed || !_sequenceById.TryGetValue(outcome.Id, out var sequence))
        {
            return false;
        }

        var index = (int)(sequence - _firstSequence);
        var (id, action) = _entries[index];
        if (action.Failed == failed)
        {
            return false;
        }

        _entries[index] = (id, action with { Failed = failed });
        _actions = null;
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _sequenceById.Clear();
        _firstSequence = 0;
        _actions = null;
    }
}
