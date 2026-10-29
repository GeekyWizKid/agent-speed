using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>What a task's own edits say about one file (METRICS 6.9 <i>Effective edits</i>, the reference's <c>FileEdits</c>).</summary>
internal sealed class TaskFileEdits
{
    /// <summary>Contents of whole-file writes (only membership is read).</summary>
    private readonly HashSet<string> _written = new(StringComparer.Ordinal);

    /// <summary>Texts the task's hunks produced.</summary>
    private readonly HashSet<string> _current = new(StringComparer.Ordinal);

    /// <summary>Texts the task's hunks replaced.</summary>
    private readonly HashSet<string> _superseded = new(StringComparer.Ordinal);

    /// <summary>The file's content when known (after a write, before any hunk).</summary>
    private string? _latest;

    /// <summary><c>apply_edit</c>: marks an edit effective and oscillating, updating the files' state.</summary>
    public static void Apply(Dictionary<string, TaskFileEdits> files, TaskAct act)
    {
        if (act.Outcome is { Failed: true })
        {
            return;
        }

        var hunks = act.Hunks;
        if (hunks is null || hunks.Count == 0)
        {
            act.Effective = true;
            return;
        }

        var changed = false;
        var oscillating = false;
        foreach (var hunk in hunks)
        {
            if (!files.TryGetValue(hunk.File, out var state))
            {
                state = new TaskFileEdits();
                files[hunk.File] = state;
            }

            if (state.Apply(hunk, ref oscillating))
            {
                changed = true;
            }
        }

        act.Effective = changed;
        act.Oscillating = changed && oscillating;
    }

    private bool Apply(EditHunk hunk, ref bool oscillating)
    {
        if (hunk.Old is not { } old)
        {
            if (_latest is not null && hunk.New == _latest)
            {
                return false;
            }

            if (!_written.Add(hunk.New))
            {
                oscillating = true;
            }

            _latest = hunk.New;
            _current.Clear();
            _superseded.Clear();
            return true;
        }

        if (old == hunk.New)
        {
            return false;
        }

        if (_current.Contains(old) && _superseded.Contains(hunk.New))
        {
            oscillating = true;
        }

        _superseded.Add(old);
        _current.Remove(old);
        _current.Add(hunk.New);
        _latest = null;
        return true;
    }
}
