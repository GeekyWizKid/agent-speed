using AgentSpeed.Core;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>The two sources side by side; <see cref="A"/> is on the left.</summary>
public readonly record struct TaskPair(SourceKind A, SourceKind B)
{
    public bool IsValid => A != B && TaskScoredSources.Contains(A) && TaskScoredSources.Contains(B);
}

/// <summary>The pair pickers (DESIGN 12, <i>Header</i>): which two sources face each other and what each picker lists.</summary>
public static class TaskPerformancePair
{
    /// <summary>The scored sources with records, in source order: what the pickers list.</summary>
    public static IReadOnlyList<SourceKind> WithRecords(TaskComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return TaskScoredSources.All.Where(s => comparison.Of(s).HasRecords).ToArray();
    }

    /// <summary>Codex and Claude Code when both have records, else the two with the most tasks (ties in source order); null below two.</summary>
    public static TaskPair? Default(TaskComparison comparison)
    {
        var sources = WithRecords(comparison);
        if (sources.Count < 2)
        {
            return null;
        }

        if (sources.Contains(SourceKind.Codex) && sources.Contains(SourceKind.ClaudeCode))
        {
            return new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode);
        }

        var top = sources
            .Select((source, order) => (source, order, tasks: comparison.Of(source).Tasks))
            .OrderByDescending(s => s.tasks)
            .ThenBy(s => s.order)
            .Take(2)
            .OrderBy(s => s.order)
            .ToArray();
        return new TaskPair(top[0].source, top[1].source);
    }

    /// <summary>
    /// The remembered pair while at least one of its sources has records in the range (the other side then reads
    /// <c>history.tasks.side.empty</c>), else <see cref="Default"/>.
    /// </summary>
    public static TaskPair? Resolve(TaskComparison comparison, TaskPair? remembered)
    {
        var sources = WithRecords(comparison);
        if (sources.Count < 2)
        {
            return null;
        }

        return remembered is { IsValid: true } pair && (sources.Contains(pair.A) || sources.Contains(pair.B)) ? pair : Default(comparison);
    }

    /// <summary>One picker's choices: the sources with records without the other side's choice; its own choice stays listed.</summary>
    public static IReadOnlyList<SourceKind> Options(TaskComparison comparison, SourceKind own, SourceKind other)
    {
        var sources = WithRecords(comparison);
        return TaskScoredSources.All.Where(s => s != other && (s == own || sources.Contains(s))).ToArray();
    }

    /// <summary>A picker's new choice; choosing the other side's source swaps the two.</summary>
    public static TaskPair Pick(TaskPair pair, bool first, SourceKind source)
    {
        if (first)
        {
            return source == pair.B ? new TaskPair(source, pair.A) : pair with { A = source };
        }

        return source == pair.A ? new TaskPair(pair.B, source) : pair with { B = source };
    }
}
