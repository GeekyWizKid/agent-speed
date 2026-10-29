namespace AgentSpeed.Core.Tasks;

/// <summary>
/// A task's actions at one instant with everything 6.9 derives from them in order: effective and oscillating edits,
/// blocks, new evidence <c>ΔI</c>, counted indices, interned keys and checks, and the stall windows. Each of these
/// depends only on the actions before it, so every prefix evaluation (6.11 snapshots and fix rows) reads the same acts.
/// </summary>
internal sealed class TaskActs
{
    private TaskActs(List<TaskAct> acts, List<TaskAct> counted, List<TaskAct> runs)
    {
        Acts = acts;
        Counted = counted;
        Runs = runs;
        CountedKeys = counted.ConvertAll(act => act.KeyId);
        StallEnds = TaskLoops.StallEnds(counted);
        foreach (var run in runs)
        {
            if (run is { Result: RunResult.Pass, Targeted: false, Suite: { } suite })
            {
                if (!FullPasses.TryGetValue(suite, out var passes))
                {
                    passes = [];
                    FullPasses[suite] = passes;
                }

                passes.Add(run);
            }
        }
    }

    public List<TaskAct> Acts { get; }

    /// <summary>The acts with <c>w &gt; 0</c>, in order.</summary>
    public List<TaskAct> Counted { get; }

    /// <summary>The interned keys of <see cref="Counted"/>.</summary>
    public List<int> CountedKeys { get; }

    /// <summary>The acts that are runs, in order.</summary>
    public List<TaskAct> Runs { get; }

    /// <summary><c>stall_ends</c> over every counted act: 1-based counted indices that end a stall window.</summary>
    public List<int> StallEnds { get; }

    /// <summary>Per suite, its passing non-targeted runs in order (what a targeted check's sequence also takes).</summary>
    public Dictionary<string, List<TaskAct>> FullPasses { get; } = new(StringComparer.Ordinal);

    /// <summary>The actions among <c>events[start..end)</c> with <c>t ≤ now</c> (every action when null).</summary>
    public static TaskActs Of(IReadOnlyList<TaskEvent> events, int start, int end, long? now)
    {
        var acts = new List<TaskAct>();
        var keys = new Dictionary<(string Tool, string Digest), int>();
        for (var i = start; i < end; i++)
        {
            if (events[i] is not TaskAction action || (now is { } limit && action.T > limit))
            {
                continue;
            }

            var key = (action.Tool, action.Digest);
            if (!keys.TryGetValue(key, out var keyId))
            {
                keyId = keys.Count;
                keys[key] = keyId;
            }

            acts.Add(new TaskAct(acts.Count, action, keyId, now));
        }

        var counted = new List<TaskAct>();
        var runs = new List<TaskAct>();
        Pass(acts, counted, runs);
        return new TaskActs(acts, counted, runs);
    }

    /// <summary>The reference's <c>_edits_and_gain</c> loop, plus the run and check numbering.</summary>
    private static void Pass(List<TaskAct> acts, List<TaskAct> counted, List<TaskAct> runs)
    {
        Dictionary<string, TaskFileEdits>? files = null;
        var lastSeen = new Dictionary<int, int>();
        var lastRun = new Dictionary<string, (int Edits, TaskAct Run)>(StringComparer.Ordinal);
        var checks = new Dictionary<CheckId, int>();
        var edits = 0;
        foreach (var act in acts)
        {
            act.Block = edits;
            if (act.Kind == TaskParsers.ActionKind.Edit)
            {
                TaskFileEdits.Apply(files ??= new Dictionary<string, TaskFileEdits>(StringComparer.Ordinal), act);
                act.Gain = act.Effective && !act.Oscillating ? 1 : 0;
                if (act.Effective)
                {
                    edits++;
                }
            }
            else if (act.IsRun)
            {
                var key = act.Command!.Key;
                act.Gain = lastRun.TryGetValue(key, out var previous) && previous.Edits == edits && SameSignature(previous.Run, act) ? 0 : 1;
                lastRun[key] = (edits, act);
                var check = act.Check;
                if (!checks.TryGetValue(check, out var checkNo))
                {
                    checkNo = checks.Count;
                    checks[check] = checkNo;
                }

                act.CheckNo = checkNo;
                act.RunOrdinal = runs.Count;
                runs.Add(act);
            }
            else if (act.Weight > 0)
            {
                act.Gain = lastSeen.TryGetValue(act.KeyId, out var seen) && seen == edits ? 0 : 1;
            }

            if (act.Kind != TaskParsers.ActionKind.Edit && act.Weight > 0 && !act.IsRun)
            {
                lastSeen[act.KeyId] = edits;
            }

            if (act.Weight > 0)
            {
                counted.Add(act);
            }
        }
    }

    /// <summary>The run signature: result, the set of fingerprints and <c>failedCount</c>.</summary>
    private static bool SameSignature(TaskAct a, TaskAct b)
    {
        if (a.Result != b.Result || a.FailedCount != b.FailedCount)
        {
            return false;
        }

        return a.Fingerprints is null ? b.Fingerprints is null : b.Fingerprints is not null && a.Fingerprints.SetEquals(b.Fingerprints);
    }
}
