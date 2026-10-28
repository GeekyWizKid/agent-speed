using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.Tests.TestSupport;

/// <summary>Mock task scores and comparisons, shaped like the prototype's (<c>spec/prototypes/task-score</c>).</summary>
internal static class TaskData
{
    private static readonly StringTable Table = Localizer.LoadEmbeddedTable();

    public static Localizer Loc(string language = LanguageCodes.ZhHant) => new(Table, language);

    public static TaskPrediction Prior(double p, int tasks) => new(p, new TaskModelFlag(false, tasks));

    public static TaskPrediction Calibrated(double p, int tasks) => new(p, new TaskModelFlag(true, tasks));

    /// <summary>nova: edited, nothing run yet (E alone).</summary>
    public static TaskScoreFacts Unverified { get; } = new(TaskForm.Live, TaskState.Unverified, 8.75, new TaskDimensions(0, 0, 0, 8.75, 0),
        Predictions: new TaskPredictions(Calibrated(0.62, 212), Prior(0.14, 12)));

    /// <summary>rocket: cargo test failing with 3 of a peak of 9.</summary>
    public static TaskScoreFacts Failing { get; } = new(TaskForm.Live, TaskState.Failing, 41.25, new TaskDimensions(0, 16.67, 9.4, 7.18, 8),
        OpenFailure: new TaskOpenFailure("cargo test", TaskCheckClass.Test, 3, 9, Counted: true, Edited: false), Oscillations: 2,
        Predictions: new TaskPredictions(Calibrated(0.38, 54), Calibrated(0.41, 54), Prior(0.52, 23)));

    /// <summary>nova: swift test passing.</summary>
    public static TaskScoreFacts Passing { get; } = new(TaskForm.Live, TaskState.Passing, 86.25, new TaskDimensions(40, 20, 11.25, 7, 8),
        LastPass: new TaskLastPass("swift test", TaskCheckClass.Test, BuildOnly: false),
        Predictions: new TaskPredictions(Calibrated(0.97, 212), Calibrated(0.05, 212)));

    /// <summary>quasar: pytest passed, then edited.</summary>
    public static TaskScoreFacts Pending { get; } = new(TaskForm.Live, TaskState.Pending, 34, new TaskDimensions(0, 18, 10, 6, 0), VHeld: 40,
        LastPass: new TaskLastPass("pytest", TaskCheckClass.Test, BuildOnly: false),
        Predictions: new TaskPredictions(Prior(0.6, 0), Prior(0.05, 0)));

    /// <summary>orbit: idle after npm test passed.</summary>
    public static TaskScoreFacts Resting { get; } = new(TaskForm.Resting, TaskState.Passing, 72.4, new TaskDimensions(40, 14, 8, 6.4, 4),
        LastPass: new TaskLastPass("npm test", TaskCheckClass.Test, BuildOnly: false),
        Predictions: new TaskPredictions(Calibrated(0.9, 60), Calibrated(0.1, 60)));

    public static TaskCardSubject Subject(TaskScoreFacts facts, string project = "nova", SourceKind source = SourceKind.ClaudeCode,
        string? model = "claude-sonnet-5-5") => new(source, project, model, facts);

    /// <summary>Comparison values of a group of records, from the histogram's counts.</summary>
    public static TaskStats Stats(int tasks, int scored, double? median, double? q1 = null, double? q3 = null, int[]? histogram = null,
        int successes = 0, int stalls = 0, int fixFailures = 0, int fixAttempts = 0, double? attempts = null, double? firstPassMs = null) =>
        new(tasks, scored, median, q1 ?? median, q3 ?? median, histogram ?? new int[TaskStats.Bins], successes, stalls, fixFailures, fixAttempts,
            attempts, firstPassMs);

    public static TaskStats Codex { get; } = Stats(128, 96, 71.2, 48.6, 86.9, [2, 3, 4, 5, 6, 8, 12, 22, 20, 14],
        successes: 75, stalls: 13, fixFailures: 25, fixAttempts: 100, attempts: 2.04, firstPassMs: 300_000);

    public static TaskStats ClaudeCode { get; } = Stats(214, 171, 65.9, 41.2, 79.5, [4, 6, 8, 10, 14, 22, 30, 36, 25, 16],
        successes: 118, stalls: 34, fixFailures: 48, fixAttempts: 150, attempts: 1.42, firstPassMs: 180_000);

    public static TaskStats Pi { get; } = Stats(6, 4, 52, histogram: [0, 0, 0, 1, 1, 1, 1, 0, 0, 0], successes: 2, stalls: 1);

    public static TaskComparison Comparison(params (SourceKind Source, TaskStats Stats)[] sources) =>
        new([.. sources.Select(s => new TaskSourceStats(s.Source, s.Stats))], Models);

    public static IReadOnlyList<TaskModelStats> Models { get; } =
    [
        new("claude-opus-5-5", [SourceKind.ClaudeCode, SourceKind.Omp], Stats(137, 113, 70.4, 62.1, 78.3, successes: 80, stalls: 19, fixFailures: 30, fixAttempts: 100, attempts: 1.3, firstPassMs: 180_000)),
        new("claude-sonnet-5-5", [SourceKind.ClaudeCode], Stats(82, 62, 57, 44, 66, successes: 40, stalls: 16, fixFailures: 36, fixAttempts: 100, attempts: 1.6, firstPassMs: 240_000)),
        new("gpt-6.1-sol", [SourceKind.Codex], Stats(74, 58, 74, 63, 82, successes: 47, stalls: 7, fixFailures: 21, fixAttempts: 100, attempts: 1.8, firstPassMs: 300_000)),
        new("gpt-6.1-terra", [SourceKind.Codex], Stats(54, 38, 63, 55, 71, successes: 28, stalls: 6, fixFailures: 29, fixAttempts: 100, attempts: 2.3, firstPassMs: 420_000)),
        new("claude-fable-5-1", [SourceKind.ClaudeDesktop], Stats(9, 6, 65.2, 58, 70, successes: 4, stalls: 1, fixFailures: 1, fixAttempts: 3, attempts: 1.5, firstPassMs: 300_000)),
    ];
}
