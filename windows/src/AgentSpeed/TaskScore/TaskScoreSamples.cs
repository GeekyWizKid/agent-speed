using AgentSpeed.Core;
using AgentSpeed.Core.Work;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.TaskScore;

/// <summary>
/// Sample task scores for the capture harness and the Windows tests: every card state of the prototype
/// (<c>spec/prototypes/task-score</c>) and a range with Codex, Claude Code and a small Pi sample.
/// </summary>
internal static class TaskScoreSamples
{
    public static IReadOnlyList<(string Caption, TaskCardSubject Subject)> Cards(Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        string[] effort = [loc.Format("effort.tooltip", "medium")];
        string[] fast = [loc.Format("effort.tooltip", "xhigh"), loc.Format("speed.tooltip", loc["speed.value.fast"])];
        return
        [
            ("NONE", new(SourceKind.Omp, "lumen", "claude-opus-5-5",
                new TaskScoreFacts(TaskForm.None, TaskState.None, null, new TaskDimensions(0, 0, 0, 1.25, 0)), effort)),
            ("UNVERIFIED", new(SourceKind.ClaudeCode, "nova", "claude-sonnet-5-5",
                new TaskScoreFacts(TaskForm.Live, TaskState.Unverified, 8.75, new TaskDimensions(0, 0, 0, 8.75, 0),
                    Predictions: new TaskPredictions(Calibrated(0.62, 212), Prior(0.14, 12))), effort)),
            ("FAILING", new(SourceKind.Codex, "rocket", "gpt-6.1-sol",
                new TaskScoreFacts(TaskForm.Live, TaskState.Failing, 41.25, new TaskDimensions(0, 16.67, 9.4, 7.18, 8),
                    OpenFailure: new TaskOpenFailure("cargo test", TaskCheckClass.Test, 3, 9, Counted: true, Edited: false), Oscillations: 2,
                    Predictions: new TaskPredictions(Calibrated(0.38, 54), Calibrated(0.41, 54), Prior(0.52, 23))),
                fast, new WorkSignals(4, null, 0.46, 5, 1))),
            ("FAILING · EDITED", new(SourceKind.Pi, "vega", "gpt-6.1-terra",
                new TaskScoreFacts(TaskForm.Live, TaskState.Failing, 25.4, new TaskDimensions(0, 14.2, 5.1, 6.1, 0),
                    OpenFailure: new TaskOpenFailure("go test", TaskCheckClass.Test, 2, 6, Counted: true, Edited: true), Oscillations: 1,
                    Predictions: new TaskPredictions(Prior(0.44, 12), Prior(0.18, 12), Prior(0.35, 9))),
                [loc.Format("effort.tooltip", "high")], new WorkSignals(null, null, 0.71, 3, 1))),
            ("PASSING", new(SourceKind.ClaudeCode, "nova", "claude-opus-5-5",
                new TaskScoreFacts(TaskForm.Live, TaskState.Passing, 86.25, new TaskDimensions(40, 20, 11.25, 7, 8),
                    LastPass: new TaskLastPass("swift test", TaskCheckClass.Test, BuildOnly: false),
                    Predictions: new TaskPredictions(Calibrated(0.97, 212), Calibrated(0.05, 212))), effort)),
            ("PENDING", new(SourceKind.Codex, "quasar", "gpt-6.1-terra",
                new TaskScoreFacts(TaskForm.Live, TaskState.Pending, 34, new TaskDimensions(0, 18, 10, 6, 0), VHeld: 40,
                    LastPass: new TaskLastPass("pytest", TaskCheckClass.Test, BuildOnly: false),
                    Predictions: new TaskPredictions(Prior(0.6, 0), Prior(0.05, 0))), effort)),
            ("RESTING · TOP OF 2", new(SourceKind.ClaudeDesktop, "orbit", "claude-fable-5-1",
                new TaskScoreFacts(TaskForm.Resting, TaskState.Passing, 72.4, new TaskDimensions(40, 14, 8, 6.4, 4),
                    LastPass: new TaskLastPass("npm test", TaskCheckClass.Test, BuildOnly: false)),
                effort, new WorkSignals(null, null, 0.69, 2, 1), TopCount: 2)),
            ("UNSUPPORTED", new(SourceKind.DeepSeek, "tihu", "deepseek-v4-pro", TaskScoreFacts.None, effort)),
        ];
    }

    public static TaskComparison Comparison(bool withPi = true)
    {
        TaskSourceStats[] sources =
        [
            new(SourceKind.Codex, TaskStats.FromCounts(128, 96, 71.2, 48.6, 86.9, [2, 3, 4, 5, 6, 8, 12, 22, 20, 14], 75, 13, 25, 100, 50, 102, 300_000)),
            new(SourceKind.ClaudeCode, TaskStats.FromCounts(214, 171, 65.9, 41.2, 79.5, [4, 6, 8, 10, 14, 22, 30, 36, 25, 16], 118, 34, 48, 150, 50, 71, 180_000)),
            new(SourceKind.Pi, TaskStats.FromCounts(6, 4, 52, 50, 55, [0, 0, 0, 1, 1, 1, 1, 0, 0, 0], 2, 1, 0, 0, 0, 0, null)),
        ];
        TaskModelStats[] models =
        [
            Model("claude-opus-5-5", [SourceKind.ClaudeCode, SourceKind.Omp], 137, 113, 70.4, 62.1, 78.3, 80, 19, 30, 100, 1.3, 180_000),
            Model("claude-sonnet-5-5", [SourceKind.ClaudeCode], 82, 62, 57, 44, 66, 40, 16, 36, 100, 1.6, 240_000),
            Model("gpt-6.1-sol", [SourceKind.Codex], 74, 58, 74, 63, 82, 47, 7, 21, 100, 1.8, 300_000),
            Model("gpt-6.1-terra", [SourceKind.Codex], 54, 38, 63, 55, 71, 28, 6, 29, 100, 2.3, 420_000),
            Model("claude-fable-5-1", [SourceKind.ClaudeDesktop], 9, 6, 65.2, 58, 70, 4, 1, 1, 3, 1.5, 300_000),
        ];
        return new TaskComparison(withPi ? sources : sources[..2], models);
    }

    private static TaskPrediction Prior(double p, int tasks) => new(p, new TaskModelFlag(false, tasks));

    private static TaskPrediction Calibrated(double p, int tasks) => new(p, new TaskModelFlag(true, tasks));

    private static TaskModelStats Model(string id, SourceKind[] sources, int tasks, int scored, double median, double q1, double q3, int successes,
        int stalls, int fixFailures, int fixAttempts, double attempts, double firstPassMs) =>
        new(id, sources, TaskStats.FromCounts(tasks, scored, median, q1, q3, new int[TaskStats.Bins], successes, stalls, fixFailures, fixAttempts,
            10, (int)Math.Round(attempts * 10), firstPassMs));
}
