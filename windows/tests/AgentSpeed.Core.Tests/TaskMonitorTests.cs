using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The task score in the live monitor (METRICS 6.9 <i>Event stream</i>, <i>Values</i>): seeded lines compose like live
/// ones, the snapshot carries each agent's value and task and each source's highest score, the history's calibration
/// reaches the predictions, and a replaced file starts its stream over.
/// </summary>
public sealed class TaskMonitorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly long _todayStart;

    public TaskMonitorTests()
    {
        _todayStart = _now - 3_600_000;
        Directory.CreateDirectory(CodexRoot);
        Directory.CreateDirectory(ClaudeRoot);
    }

    private string CodexRoot => _temp.Combine("codex", "sessions");

    private string ClaudeRoot => _temp.Combine("claude", "projects");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void SeededLinesComposeTheCurrentTaskAndTheSnapshotCarriesItsScore()
    {
        var t = _now - 120_000;
        WriteClaude("p/a.jsonl",
            ClaudeLines.UserText(t, "fix the parser"),
            Call(t + 1000, "m1", "e1", "Edit", """{"file_path":"/w/src/a.rs","old_string":"a","new_string":"b"}"""),
            Result(t + 1500, "e1", false, "ok"),
            Call(t + 2000, "m2", "b1", "Bash", """{"command":"cargo test"}"""),
            Result(t + 9000, "b1", false, "test result: ok. 12 passed; 0 failed; 0 ignored"),
            ClaudeLines.Assistant(t + 10_000, "m3", 20));

        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        var snapshot = core.BuildSnapshot(_now, _todayStart, MonitorState.Running, observeLive: true).Snapshot;
        var agent = Assert.Single(snapshot.Agents);

        // Seeding keeps the task stream (unlike the work signals' actions, METRICS 6.10).
        var task = Assert.IsType<AgentTaskStatus>(agent.Task);
        Assert.Equal(t, task.TaskStartMs);
        Assert.Equal(TaskState.Passing, task.Result!.State);
        Assert.Equal(TaskState.Passing, agent.Intelligence.State);
        Assert.Equal(AgentIntelligenceForm.Resting, agent.Intelligence.Form);
        Assert.Equal(task.Result.Shown, agent.Intelligence.Shown);
        Assert.NotNull(task.Predictions);
        Assert.False(task.Models.Success.Calibrated);

        var claude = snapshot.Sources.Single(s => s.Kind == SourceKind.ClaudeCode).Intelligence;
        Assert.Equal(agent.Intelligence.Shown, claude.Shown);
        Assert.Equal(agent.Id, claude.Score.AgentId);
        Assert.Null(snapshot.Sources.Single(s => s.Kind == SourceKind.Codex).Intelligence.Shown);
    }

    [Fact]
    public void ANewPromptStartsANewTaskAtNoneAndAContinuationKeepsIt()
    {
        var t = _now - 120_000;
        var path = WriteClaude("p/a.jsonl",
            ClaudeLines.UserText(t, "fix it"),
            Call(t + 1000, "m1", "e1", "Edit", """{"file_path":"/w/a.py","old_string":"a","new_string":"b"}"""),
            Result(t + 1200, "e1", false, "ok"));
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        Assert.Equal(TaskState.Unverified, Agent(core).Task!.Result!.State);

        Append(path, ClaudeLines.UserText(_now - 5000, "Continue."));
        Assert.True(core.ProcessPaths([path], _now, _todayStart));
        Assert.Equal(TaskState.Unverified, Agent(core).Task!.Result!.State);
        Assert.Equal(t, Agent(core).Task!.TaskStartMs);

        Append(path, ClaudeLines.UserText(_now - 4000, "now write the docs"), ClaudeLines.Assistant(_now - 3000, "m9", 40));
        Assert.True(core.ProcessPaths([path], _now, _todayStart));
        var agent = Agent(core);
        Assert.Equal(TaskState.None, agent.Task!.Result!.State);
        Assert.Equal(_now - 4000, agent.Task.TaskStartMs);
        Assert.Null(agent.Intelligence.Shown);

        // Once no withdrawal can bring the earlier task back, a live composer keeps only the current task's events.
        var parser = core.TaskAgents.Single();
        Assert.IsType<TaskPrompt>(Assert.Single(parser.Tasks!.Events));
    }

    [Fact]
    public void TheHistorysCalibrationReachesThePredictions()
    {
        var t = _now - 120_000;
        WriteClaude("p/a.jsonl",
            ClaudeLines.UserText(t, "fix it"),
            Call(t + 1000, "m1", "e1", "Edit", """{"file_path":"/w/a.py","old_string":"a","new_string":"b"}"""),
            Result(t + 1200, "e1", false, "ok"));
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        var prior = Agent(core).Task!.Predictions!.Success;

        var calibrated = new TaskModels(
            new TaskModel([3, 0, 0, 0, 0, 0, 0, 0], true, 57), TaskModels.Prior.Stall, TaskModels.Prior.Fix);
        core.SetTaskModels(new Dictionary<SourceKind, TaskModels> { [SourceKind.ClaudeCode] = calibrated });
        var task = Agent(core).Task!;
        Assert.True(task.Models.Success.Calibrated);
        Assert.Equal(57, task.Models.Success.Tasks);
        Assert.NotEqual(prior, task.Predictions!.Success);
        Assert.Equal(TaskPredictions.Sigmoid(3), task.Predictions.Success, 12);
    }

    [Fact]
    public void AReplacedFileStartsItsTaskStreamOver()
    {
        var t = _now - 120_000;
        var path = WriteClaude("p/a.jsonl",
            ClaudeLines.UserText(t, "fix it"),
            Call(t + 1000, "m1", "e1", "Edit", """{"file_path":"/w/a.py","old_string":"a","new_string":"b"}"""),
            Result(t + 1200, "e1", false, "ok"),
            ClaudeLines.Assistant(t + 2000, "m2", 50));
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        Assert.Equal(TaskState.Unverified, Agent(core).Task!.Result!.State);

        // Shorter content: the tailer reads it from the start; the old call's result must not complete anything.
        File.WriteAllText(path, ClaudeLines.UserText(_now - 3000, "something else") + "\n" + Result(_now - 2000, "e1", false, "ok") + "\n");
        Assert.True(core.ProcessPaths([path], _now, _todayStart));
        var parser = core.TaskAgents.Single();
        Assert.IsType<TaskPrompt>(Assert.Single(parser.Tasks!.Events));
        Assert.Equal(1, parser.Tasks.Stats.OrphanResults);
        Assert.Equal(TaskState.None, Agent(core).Task!.Result!.State);
    }

    [Fact]
    public void ACodexCommandThatOutlivesItsCallIsStitchedFromWriteStdin()
    {
        var t = _now - 120_000;
        var path = WriteCodex("2026/10/28/rollout-a.jsonl",
            CodexLines.SessionMeta(t, cwd: "/w/proj"),
            CodexLines.Envelope(t + 100, "response_item", """{"type":"message","role":"user","content":[{"type":"input_text","text":"run the tests"}]}"""),
            CodexLines.Envelope(t + 1000, "response_item",
                """{"type":"function_call","name":"exec_command","arguments":"{\"cmd\":\"cargo test\"}","call_id":"c1"}"""),
            CodexLines.Envelope(t + 11_000, "response_item",
                """{"type":"function_call_output","call_id":"c1","output":"Process running with session ID 7\nOutput:\nrunning 12 tests\n"}"""));
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        var action = core.TaskAgents.Single().Tasks!.Events.OfType<TaskAction>().Single();
        Assert.NotNull(action.Outcome);
        Assert.Null(action.Outcome!.Value.Failed);

        Append(path,
            CodexLines.Envelope(_now - 5000, "response_item",
                """{"type":"function_call","name":"write_stdin","arguments":"{\"session_id\":7,\"chars\":\"\"}","call_id":"c2"}"""),
            CodexLines.Envelope(_now - 4000, "response_item",
                """{"type":"function_call_output","call_id":"c2","output":"Process exited with code 0\nOutput:\ntest result: ok. 12 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.52s\n"}"""));
        Assert.True(core.ProcessPaths([path], _now, _todayStart));
        var composer = core.TaskAgents.Single().Tasks!;
        Assert.Equal(1, composer.Stats.Stitched);
        var run = composer.Events.OfType<TaskAction>().First();
        Assert.Equal((0L, false), (run.Outcome!.Value.ExitCode, run.Outcome.Value.Failed));
        Assert.Equal(12, run.Failures!.PassedCount);
    }

    private AgentStatus Agent(MonitorCore core) =>
        Assert.Single(core.BuildSnapshot(_now, _todayStart, MonitorState.Running, observeLive: true).Snapshot.Agents);

    private static string Call(long ms, string message, string id, string tool, string inputJson) =>
        ClaudeLines.Assistant(ms, message, 10, "\"tool_use\"").Replace(
            """[{"type":"text","text":"x"}]""",
            $$$"""[{"type":"tool_use","id":"{{{id}}}","name":"{{{tool}}}","input":{{{inputJson}}} }]""", StringComparison.Ordinal);

    private static string Result(long ms, string id, bool isError, string content) =>
        ClaudeLines.User(ms, $$"""[{"type":"tool_result","tool_use_id":"{{id}}","is_error":{{(isError ? "true" : "false")}},"content":"{{content}}"} ]""");

    private MonitorCore NewCore() => new(MonitorConfiguration.Default, CodexRoot, ClaudeRoot, _temp.Combine("dsh"), _todayStart);

    private string WriteClaude(string relative, params string[] lines) => Write(Path.Combine([ClaudeRoot, .. relative.Split('/')]), lines);

    private string WriteCodex(string relative, params string[] lines) => Write(Path.Combine([CodexRoot, .. relative.Split('/')]), lines);

    private static string Write(string path, string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
        return path;
    }

    private static void Append(string path, params string[] lines) => File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n")));
}
