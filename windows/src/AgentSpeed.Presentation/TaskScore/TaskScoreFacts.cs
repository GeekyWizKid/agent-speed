namespace AgentSpeed.Presentation.TaskScore;

/// <summary>A task's state (METRICS 6.9, <i>States</i>).</summary>
public enum TaskState
{
    /// <summary>No effective edit and no run.</summary>
    None,

    /// <summary>Effective edits, no run.</summary>
    Unverified,

    /// <summary>The current block fails, or it is empty after a failing one.</summary>
    Failing,

    /// <summary>The current block is not empty and does not fail.</summary>
    Passing,

    /// <summary>Passed, then edited: the credit waits for a re-run.</summary>
    Pending,
}

/// <summary>How a score shows (METRICS 6.9, <i>Values</i>): in the number color, pale, or as <c>–</c>.</summary>
public enum TaskForm
{
    None,
    Live,
    Resting,
}

/// <summary>A check's class (TASK-PARSERS): it names the check when its <c>suite</c> is unknown.</summary>
public enum TaskCheckClass
{
    Test,
    Build,
    Check,
}

/// <summary>The five dimensions, in the order the card and the composition bar show them (DESIGN 12).</summary>
public enum TaskDimension
{
    /// <summary>V 驗證成功, at most 40.</summary>
    Verify,

    /// <summary>C 錯誤收斂, at most 25.</summary>
    Converge,

    /// <summary>R 錯誤恢復, at most 15.</summary>
    Recover,

    /// <summary>E 行為效率, at most 10.</summary>
    Efficiency,

    /// <summary>K 驗證完整性, at most 10.</summary>
    Complete,
}

/// <summary>A task's dimensions, unrounded (METRICS 6.9, <i>Dimensions</i>).</summary>
public readonly record struct TaskDimensions(double V, double C, double R, double E, double K)
{
    public static IReadOnlyList<TaskDimension> Order { get; } = Enum.GetValues<TaskDimension>();

    public double this[TaskDimension dimension] => dimension switch
    {
        TaskDimension.Verify => V,
        TaskDimension.Converge => C,
        TaskDimension.Recover => R,
        TaskDimension.Efficiency => E,
        _ => K,
    };

    public double Sum => V + C + R + E + K;

    public static int Max(TaskDimension dimension) => dimension switch
    {
        TaskDimension.Verify => 40,
        TaskDimension.Converge => 25,
        TaskDimension.Recover => 15,
        _ => 10,
    };
}

/// <summary>
/// The open failure of a <see cref="TaskState.Failing"/> task (METRICS 6.9, <i>Task facts</i>): the newest failing run
/// that is still the last of its check's sequence.
/// </summary>
/// <param name="Suite">The parsers' fixed suite name (<c>cargo test</c>), or null when unknown.</param>
/// <param name="Errors">Its <c>F</c>.</param>
/// <param name="Peak">Its check's <c>F_peak</c>.</param>
/// <param name="Counted">The counts are real (fingerprints or a failed count), not the minimum of 1.</param>
/// <param name="Edited">An effective edit followed it.</param>
public sealed record TaskOpenFailure(string? Suite, TaskCheckClass Class, int Errors, int Peak, bool Counted, bool Edited);

/// <summary>The last passing run of a passing or pending task (METRICS 6.9, <i>Task facts</i>).</summary>
/// <param name="BuildOnly">Its block has no passing test run (V, or the held credit, is 20).</param>
public sealed record TaskLastPass(string? Suite, TaskCheckClass Class, bool BuildOnly);

/// <summary>Whether a prediction model uses the user's own tasks (METRICS 6.11, <i>Calibration</i>).</summary>
/// <param name="Tasks">N, the tasks with at least one row for the model.</param>
public readonly record struct TaskModelFlag(bool Calibrated, int Tasks);

/// <summary>One prediction with its model's flag (METRICS 6.11, <i>Predictions</i>).</summary>
public sealed record TaskPrediction(double Probability, TaskModelFlag Flag);

/// <summary>The live task's predictions; <see cref="FixFails"/> only while failing.</summary>
public sealed record TaskPredictions(TaskPrediction Success, TaskPrediction Stall, TaskPrediction? FixFails = null);

/// <summary>
/// One agent's task score as a surface needs it (METRICS 6.9, <i>Values</i> and <i>Task facts</i>; 6.11
/// <i>Predictions</i>). The core's values map onto this record; nothing here is computed again.
/// </summary>
/// <param name="Score">The unrounded score; null while the form is none.</param>
/// <param name="VHeld">Pending only: the credit a passing re-run restores (40, or 20 after a build-only pass).</param>
/// <param name="Oscillations">The task's oscillating effective edits.</param>
public sealed record TaskScoreFacts(
    TaskForm Form,
    TaskState State,
    double? Score,
    TaskDimensions Dimensions = default,
    double? VHeld = null,
    TaskOpenFailure? OpenFailure = null,
    TaskLastPass? LastPass = null,
    int Oscillations = 0,
    TaskPredictions? Predictions = null)
{
    public static TaskScoreFacts None { get; } = new(TaskForm.None, TaskState.None, null);

    /// <summary>The integer every surface shows; null while none.</summary>
    public int? Shown => Form == TaskForm.None || Score is not { } score ? null : TaskScoreText.Round(score);

    /// <summary>No verified result behind the number: drawn with the proof mark (DESIGN 12, <i>Proof mark</i>).</summary>
    public bool IsUnproven => Shown is not null && TaskScoreText.IsUnproven(State);
}
