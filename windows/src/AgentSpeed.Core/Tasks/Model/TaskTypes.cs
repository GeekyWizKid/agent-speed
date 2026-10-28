namespace AgentSpeed.Core.Tasks;

/// <summary>A task's state (METRICS 6.9 <i>States</i>).</summary>
public enum TaskState
{
    /// <summary>No effective edit and no run: the task has no score.</summary>
    None,

    /// <summary>Effective edits, no run.</summary>
    Unverified,

    /// <summary>The current block is not empty and does not fail.</summary>
    Passing,

    /// <summary>The current block fails, or it is empty and the latest non-empty block fails.</summary>
    Failing,

    /// <summary>Passed, then edited: the verification credit waits for a re-run.</summary>
    Pending,
}

/// <summary>The five dimensions (METRICS 6.9 <i>Dimensions</i>), unrounded.</summary>
/// <param name="V">驗證成功, 0–40.</param>
/// <param name="C">錯誤收斂, 0–25.</param>
/// <param name="R">錯誤恢復, 0–15.</param>
/// <param name="E">行為效率, 0–10.</param>
/// <param name="K">驗證完整性, 0–10.</param>
public readonly record struct TaskDims(double V, double C, double R, double E, double K)
{
    public double Sum => V + C + R + E + K;
}

/// <summary>The counts of a task (METRICS 6.11 <i>Task records</i>), also facts of a live task.</summary>
public sealed record TaskCounts(
    int Actions,
    int Counted,
    int Edits,
    int Oscillations,
    int LoopActions,
    int Runs,
    int FailingRuns,
    int Episodes,
    int Recovered,
    int RecoverAttempts,
    int FixAttempts,
    int FixFailures)
{
    public static TaskCounts Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// After a line with a time, the agent's model and effort (METRICS 6.9 <i>Model and effort</i>): 6.11 records take the
/// last note whose <see cref="T"/> is at most the task's end.
/// </summary>
public readonly record struct TaskModelNote(long T, string? Model, string? Effort);

/// <summary>
/// One task's record (METRICS 6.11 <i>Task records</i>), without content. <see cref="AgentId"/>, <see cref="Source"/>
/// and <see cref="Day"/> identify it for the history store, calibration and comparison.
/// </summary>
public sealed record TaskRecord
{
    public string AgentId { get; init; } = string.Empty;

    public SourceKind Source { get; init; }

    /// <summary>The local date (<c>YYYY-MM-DD</c>) of <see cref="EndMs"/>; empty when it has none (METRICS 8).</summary>
    public string Day { get; init; } = string.Empty;

    public int Ordinal { get; init; }

    public long StartMs { get; init; }

    public long EndMs { get; init; }

    public bool Complete { get; init; }

    public string? Model { get; init; }

    public string? Effort { get; init; }

    public TaskState State { get; init; }

    /// <summary>Null when the state is <see cref="TaskState.None"/>.</summary>
    public double? Score { get; init; }

    public TaskDims Dims { get; init; }

    public TaskCounts Counts { get; init; } = TaskCounts.Zero;

    public long? FirstPassMs { get; init; }

    public bool Success { get; init; }

    public bool Stalled { get; init; }

    /// <summary><c>[n, passing, failing, conv, ig, loop, osc, size, yStall]</c> rows (6.11 <i>Snapshots</i>).</summary>
    public IReadOnlyList<double[]> Snapshots { get; init; } = [];

    /// <summary><c>[attempts, errors, trend, osc, ig, yFix]</c> rows (6.11 <i>Fix rows</i>).</summary>
    public IReadOnlyList<double[]> FixRows { get; init; } = [];
}

/// <summary>The lower-case names the spec and the dumps write.</summary>
public static class TaskStateNames
{
    public static string Name(TaskState state) => state switch
    {
        TaskState.Unverified => "unverified",
        TaskState.Passing => "passing",
        TaskState.Failing => "failing",
        TaskState.Pending => "pending",
        _ => "none",
    };

    public static TaskState? Parse(string? name) => name switch
    {
        "none" => TaskState.None,
        "unverified" => TaskState.Unverified,
        "passing" => TaskState.Passing,
        "failing" => TaskState.Failing,
        "pending" => TaskState.Pending,
        _ => null,
    };
}
