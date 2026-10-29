using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The task ledger's engine side in the live monitor (METRICS 11.10, Watch and Counting): a task counts once, only when its
/// first event is read live while the watch is on, when a later task settles or after 30 minutes of quiet; history, a break
/// and a seeded task never count. The snapshot's report counters carry the rows; the live facts carry each agent's value.
/// </summary>
public sealed class TaskReportWatchTests : IDisposable
{
    private static readonly ReportTaskWatch Watching = new(1, true);

    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly long _todayStart;

    public TaskReportWatchTests()
    {
        _todayStart = _now - 3_600_000;
        Directory.CreateDirectory(CodexRoot);
        Directory.CreateDirectory(ClaudeRoot);
    }

    private string CodexRoot => _temp.Combine("codex", "sessions");

    private string ClaudeRoot => _temp.Combine("claude", "projects");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AWatchedTaskCountsOnceWhenTheNextPromptSettles()
    {
        var t = _now - 600_000;
        var path = WriteClaude("p/a.jsonl", ClaudeLines.UserText(t, "seeded task"), Edit(t + 1000, "m0", "e0"), Result(t + 1500, "e0", false, "ok"));
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        core.TaskWatch = Watching;

        Append(path, ClaudeLines.UserText(_now - 9000, "fix the parser"), Edit(_now - 8000, "m1", "e1"), Result(_now - 7500, "e1", false, "ok"),
            Bash(_now - 7000, "m2", "b1", "cargo test"), Result(_now - 6000, "b1", false, "test result: ok. 12 passed; 0 failed; 0 ignored"));
        Assert.True(core.ProcessPaths([path], _now, _todayStart));
        Assert.Empty(Rows(core));
        var live = Facts(core).Tasks;
        Assert.Equal(TaskValueForm.Live, Assert.Single(live.Agents).Form);
        Assert.Equal("claude-opus-5-5", live.Agents[0].ModelId);

        // A new prompt is still withdrawable until the next assistant line; then the task before it completes.
        Append(path, ClaudeLines.UserText(_now - 5000, "now the docs"));
        core.ProcessPaths([path], _now, _todayStart);
        Assert.Empty(Rows(core));
        Append(path, ClaudeLines.Assistant(_now - 4000, "m3", 40));
        core.ProcessPaths([path], _now, _todayStart);
        var (key, row) = Assert.Single(Rows(core));
        Assert.Equal(new ReportModelKey("claude-opus-5-5", SourceKind.ClaudeCode), key);
        Assert.Equal((1L, 1L, 1L, 0L), (row.Completed, row.Scored, row.Successes, row.Stalls));
        Assert.Equal(1, row.ScoreBins.Sum());

        // Counted once: later snapshots and continuations add nothing, and the docs task has no action yet.
        Append(path, ClaudeLines.UserText(_now - 3000, "Continue."), ClaudeLines.Assistant(_now - 2000, "m4", 10));
        core.ProcessPaths([path], _now, _todayStart);
        Assert.Equal(1, Assert.Single(Rows(core)).Value.Completed);
        Assert.Equal(1, Facts(core).Counters.TaskCompleted);
    }

    [Fact]
    public void AFileReadAgainFromItsStartNeverCountsATaskTwice()
    {
        var path = WriteClaude("p/r.jsonl");
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        core.TaskWatch = Watching;
        string[] task = [ClaudeLines.UserText(_now - 9000, "fix it"), Edit(_now - 8000, "m1", "e1"), Result(_now - 7500, "e1", false, "ok"),
            ClaudeLines.UserText(_now - 5000, "next"), ClaudeLines.Assistant(_now - 4000, "m2", 40)];
        Append(path, [.. task, ClaudeLines.UserText(_now - 3000, "Continue."), ClaudeLines.Assistant(_now - 2000, "m3", 10)]);
        core.ProcessPaths([path], _now, _todayStart);
        Assert.Equal(1, Facts(core).Counters.TaskCompleted);

        // Shorter content: the tailer reads the file from its start and composes the same task again.
        File.WriteAllText(path, string.Concat(task.Select(l => l + "\n")));
        Assert.True(core.ProcessPaths([path], _now, _todayStart));
        Append(path, ClaudeLines.UserText(_now - 1000, "and then"), ClaudeLines.Assistant(_now - 500, "m4", 10));
        core.ProcessPaths([path], _now, _todayStart);
        Assert.Equal(1, Facts(core).Counters.TaskCompleted);
    }

    [Fact]
    public void AQuietAgentsTaskCompletesAfterThirtyMinutes()
    {
        var path = WriteClaude("p/b.jsonl");
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        core.TaskWatch = Watching;
        Append(path, ClaudeLines.UserText(_now - 9000, "fix it"), Edit(_now - 8000, "m1", "e1"), Result(_now - 7000, "e1", true, "no match"));
        core.ProcessPaths([path], _now, _todayStart);

        core.BuildSnapshot(_now + TaskComposer.CompleteIdleMs - 10_000, _todayStart, MonitorState.Running, observeLive: true);
        Assert.Empty(Rows(core));
        var snapshot = core.BuildSnapshot(_now + TaskComposer.CompleteIdleMs, _todayStart, MonitorState.Running, observeLive: true).Snapshot;
        var row = Assert.Single(snapshot.Report.Counters.TaskRows).Value;
        Assert.Equal((1L, 0L), (row.Completed, row.Scored));
        core.BuildSnapshot(_now + TaskComposer.CompleteIdleMs * 2, _todayStart, MonitorState.Running, observeLive: true);
        Assert.Equal(1, Facts(core).Counters.TaskCompleted);
    }

    [Fact]
    public void HistoryABreakAndAnOldFirstEventNeverCount()
    {
        var path = WriteClaude("p/c.jsonl");
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);

        // Read while the watch is off: history, even when it completes later with the watch on.
        Append(path, ClaudeLines.UserText(_now - 20_000, "before reporting"), Edit(_now - 19_000, "m1", "e1"));
        core.ProcessPaths([path], _now, _todayStart);
        core.TaskWatch = Watching;

        // A first event more than 2 minutes old when read: a session found late.
        Append(path, ClaudeLines.UserText(_now - 130_000, "late"), ClaudeLines.Assistant(_now - 129_000, "m2", 5), Edit(_now - 128_000, "m3", "e3"));
        core.ProcessPaths([path], _now, _todayStart);

        // Watched, then a break (a new generation) before it completes.
        Append(path, ClaudeLines.UserText(_now - 9000, "watched"), ClaudeLines.Assistant(_now - 8500, "m4", 5), Edit(_now - 8000, "m5", "e5"));
        core.ProcessPaths([path], _now, _todayStart);
        core.TaskWatch = Watching with { On = false };
        Facts(core);
        core.TaskWatch = new ReportTaskWatch(2, true);
        Append(path, ClaudeLines.UserText(_now - 5000, "after the break"), ClaudeLines.Assistant(_now - 4500, "m6", 5));
        core.ProcessPaths([path], _now, _todayStart);
        Assert.Empty(Rows(core));
        Assert.Equal(2, Facts(core).Counters.TaskGeneration);
    }

    [Fact]
    public void ACodexTaskCompletesAtTheNextPrompt()
    {
        var path = WriteCodex("2026/10/28/rollout-a.jsonl", CodexLines.SessionMeta(_now - 600_000, cwd: "/w/proj"),
            CodexLines.TurnContext(_now - 600_000, model: "gpt-5.5"));
        using var core = NewCore();
        core.Seed(_now, _todayStart, CancellationToken.None);
        core.TaskWatch = Watching;
        Append(path, CodexPrompt(_now - 9000, "run the tests"),
            CodexLines.Envelope(_now - 8000, "response_item", """{"type":"function_call","name":"exec_command","arguments":"{\"cmd\":\"cargo test\"}","call_id":"c1"}"""),
            CodexLines.Envelope(_now - 7000, "response_item",
                """{"type":"function_call_output","call_id":"c1","output":"Process exited with code 101\nOutput:\ntest result: FAILED. 11 passed; 1 failed; 0 ignored\n"}"""));
        core.ProcessPaths([path], _now, _todayStart);
        Assert.Empty(Rows(core));
        Append(path, CodexPrompt(_now - 5000, "try again"));
        core.ProcessPaths([path], _now, _todayStart);
        var (key, row) = Assert.Single(Rows(core));
        Assert.Equal(SourceKind.Codex, key.Source);
        Assert.Equal("gpt-5.5", key.ModelId);
        Assert.Equal((1L, 1L, 0L), (row.Completed, row.Scored, row.Successes));
    }

    private ReportLiveFacts Facts(MonitorCore core) => core.BuildSnapshot(_now, _todayStart, MonitorState.Running, observeLive: true).Snapshot.Report;

    private IReadOnlyDictionary<ReportModelKey, ReportTaskRow> Rows(MonitorCore core) => Facts(core).Counters.TaskRows;

    private static string Edit(long ms, string message, string id) => Call(ms, message, id, "Edit",
        """{"file_path":"/w/src/a.rs","old_string":"a","new_string":"b"}""");

    private static string Bash(long ms, string message, string id, string command) => Call(ms, message, id, "Bash", $$"""{"command":"{{command}}"}""");

    private static string Call(long ms, string message, string id, string tool, string inputJson) =>
        ClaudeLines.Assistant(ms, message, 10, "\"tool_use\"").Replace(
            """[{"type":"text","text":"x"}]""",
            $$$"""[{"type":"tool_use","id":"{{{id}}}","name":"{{{tool}}}","input":{{{inputJson}}} }]""", StringComparison.Ordinal);

    private static string Result(long ms, string id, bool isError, string content) =>
        ClaudeLines.User(ms, $$"""[{"type":"tool_result","tool_use_id":"{{id}}","is_error":{{(isError ? "true" : "false")}},"content":"{{content}}"} ]""");

    private static string CodexPrompt(long ms, string text) => CodexLines.Envelope(ms, "response_item",
        $$"""{"type":"message","role":"user","content":[{"type":"input_text","text":"{{text}}"}]}""");

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
