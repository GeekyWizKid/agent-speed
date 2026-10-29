using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.TaskParsers;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;
using static AgentSpeed.Presentation.Tests.TestSupport.TaskData;
using CoreTasks = AgentSpeed.Core.Tasks;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The engine's task values on the surfaces (METRICS 6.9 <i>Values</i>, <i>Sets and groups</i>; 6.11 <i>Comparison</i>).</summary>
public class TaskScoreMappingTests
{
    private static AgentStatus Agent(string id, CoreTasks.TaskValueForm form, double? score, CoreTasks.TaskState? state, long lastLine = 0,
        string project = "nova", SourceKind source = SourceKind.ClaudeCode)
    {
        var value = score is { } s
            ? new CoreTasks.TaskSetValue(form, s, CoreTasks.TaskRounding.HalfAwayFromZero(s), id) { LastLineMs = lastLine }
            : CoreTasks.TaskSetValue.None;
        return new AgentStatus(id, source, project, "claude-opus-5-5", form == CoreTasks.TaskValueForm.Live ? AgentPhase.Tools : AgentPhase.Idle,
            false, Rate.Zero, null, DateTimeOffset.UnixEpoch)
        {
            Intelligence = new AgentIntelligence(IntelligenceSums.Zero with { Score = value }, value, state),
        };
    }

    [Fact]
    public void FormsStatesAndChecksMapByMeaning()
    {
        Assert.Equal(TaskForm.Resting, TaskScoreMapping.Form(AgentIntelligenceForm.Resting));
        Assert.Equal(TaskForm.Live, TaskScoreMapping.Form(CoreTasks.TaskValueForm.Live));
        Assert.Equal(TaskState.Pending, TaskScoreMapping.State(CoreTasks.TaskState.Pending));
        Assert.Equal(TaskState.Failing, TaskScoreMapping.State(CoreTasks.TaskState.Failing));
        Assert.Equal(TaskState.None, TaskScoreMapping.State(null));
        Assert.Equal(TaskCheckClass.Build, TaskScoreMapping.Check(CommandClass.Build));
        Assert.Equal(TaskCheckClass.Check, TaskScoreMapping.Check(CommandClass.Other));
    }

    [Fact]
    public void AnAgentWithoutATaskStillShowsItsScore()
    {
        var facts = TaskScoreMapping.Facts(Agent("a", CoreTasks.TaskValueForm.Resting, 72.4, CoreTasks.TaskState.Passing));
        Assert.Equal((TaskForm.Resting, TaskState.Passing, 72), (facts.Form, facts.State, facts.Shown));
        Assert.Null(facts.Predictions);
        Assert.Equal(TaskScoreFacts.None.Shown, TaskScoreMapping.Facts(Agent("b", CoreTasks.TaskValueForm.None, null, null)).Shown);
    }

    [Fact]
    public void ASetNamesItsSupplierItsStateAndN()
    {
        AgentStatus[] agents =
        [
            Agent("a", CoreTasks.TaskValueForm.Live, 86.25, CoreTasks.TaskState.Passing, 10, "nova"),
            Agent("b", CoreTasks.TaskValueForm.Live, 41.25, CoreTasks.TaskState.Failing, 20, "rocket"),
            Agent("c", CoreTasks.TaskValueForm.Resting, 95, CoreTasks.TaskState.Passing, 30, "orbit"),
            Agent("d", CoreTasks.TaskValueForm.Live, 9, CoreTasks.TaskState.Unverified, 40, "lumen"),
            Agent("e", CoreTasks.TaskValueForm.None, null, null, 50),
        ];
        var value = CoreTasks.TaskValues.Highest(agents.Select(a =>
            new CoreTasks.TaskCandidate(a.Id, a.Intelligence.Score.LastLineMs,
                new CoreTasks.TaskValue(a.Intelligence.Score.Form, a.Intelligence.Value, a.Intelligence.Shown, a.Intelligence.State))));
        var reading = TaskSetReading.Of(value, agents);
        Assert.Equal((TaskForm.Live, 86, TaskState.Passing, 3, "nova"), (reading.Form, reading.Shown, reading.State, reading.Count, reading.Agent!.Project));
        var loc = Loc();
        Assert.Equal("86，3 個 agent 中最高", reading.Words(loc));
        Assert.Equal("智力 86，3 個 agent 中最高，思考每秒 128 個 token", reading.Sentence("128", loc));
        Assert.Equal("智力 86，3 個 agent 中最高", reading.Sentence(null, loc));
        Assert.Equal("智力 86 · 最高：nova", reading.Tooltip(loc));
        Assert.False(reading.IsUnproven);
    }

    [Fact]
    public void AnUnprovenSupplierCarriesTheProofMark()
    {
        AgentStatus[] agents = [Agent("d", CoreTasks.TaskValueForm.Live, 9, CoreTasks.TaskState.Unverified, 40, "lumen")];
        var reading = TaskSetReading.Of(agents[0].Intelligence.Score, agents);
        Assert.True(reading.IsUnproven);
        Assert.Equal("智力 9，尚未驗證", reading.Sentence(null, Loc()));
    }

    [Fact]
    public void ARestingSetIsPaleAndSaysSo()
    {
        AgentStatus[] agents =
        [
            Agent("a", CoreTasks.TaskValueForm.Resting, 72, CoreTasks.TaskState.Passing, 1, "orbit"),
            Agent("b", CoreTasks.TaskValueForm.Resting, 40, CoreTasks.TaskState.Passing, 2),
        ];
        var reading = TaskSetReading.Of(agents[0].Intelligence.Score, agents);
        Assert.True(reading.IsResting);
        Assert.Equal("智力 72，閒置中，2 個 agent 中最高，思考每秒 0 個 token", reading.Sentence("0", Loc()));
    }

    [Fact]
    public void NoneReadsIntelNoneAndKeepsTheThinkingWords()
    {
        var loc = Loc();
        Assert.Equal("智力：尚無評分", TaskSetReading.None.Sentence(null, loc));
        Assert.Equal("智力：尚無評分，思考每秒 12 個 token", TaskSetReading.None.Sentence("12", loc));
        Assert.Equal("intelligence: no score yet, thinking 12 tokens per second", TaskSetReading.None.Sentence("12", Loc(LanguageCodes.En)));
        Assert.Null(TaskSetReading.None.Tooltip(loc));
        Assert.Equal("–", TaskSetReading.None.Number);
    }

    [Fact]
    public void AGroupTakesItsHighestMember()
    {
        var root = Agent("root", CoreTasks.TaskValueForm.Live, 30, CoreTasks.TaskState.Failing, 1, "rocket");
        var sub = Agent("sub", CoreTasks.TaskValueForm.Live, 64, CoreTasks.TaskState.Pending, 2, "rocket");
        var group = TaskSetReading.Group(root, [sub]);
        Assert.Equal((64, TaskState.Pending, 2, "sub"), (group.Shown, group.State, group.Count, group.Agent!.Id));
    }

    [Fact]
    public void TheComparisonKeepsItsNumerators()
    {
        CoreTasks.TaskRecord Record(SourceKind source, double? score, bool success, bool stalled, string model = "gpt-6.1-sol") => new()
        {
            AgentId = source + "-" + score, Source = source, Day = "2026-10-27", Complete = true, Model = model, Score = score,
            State = score is null ? CoreTasks.TaskState.None : success ? CoreTasks.TaskState.Passing : CoreTasks.TaskState.Failing,
            Success = success, Stalled = stalled,
            Counts = CoreTasks.TaskCounts.Zero with { FixAttempts = 2, FixFailures = 1, Recovered = 1, RecoverAttempts = 2 },
            StartMs = 0, FirstPassMs = success ? 60_000 : null,
        };
        var result = CoreTasks.TaskComparison.Compare(
            [Record(SourceKind.Codex, 80, true, false), Record(SourceKind.Codex, 40, false, true), Record(SourceKind.Codex, null, false, false),
             Record(SourceKind.ClaudeCode, 70, true, false, "claude-opus-5-5")],
            "2026-10-01", "2026-10-31");
        var comparison = TaskScoreMapping.Comparison(result);
        var codex = comparison.Of(SourceKind.Codex);
        Assert.Equal((3, 2, 1, 1, 3, 6), (codex.Tasks, codex.ScoredTasks, codex.Successes, codex.Stalls, codex.FixFailures, codex.FixAttempts));
        Assert.Equal(0.5, codex.SuccessRate);
        Assert.Equal(2, comparison.ByModel.Count);

        var view = TaskPerformanceView.Build(new TaskPerformanceInput(comparison), 900, Loc());
        Assert.Equal("1 / 2 個已評分的任務", view.Rows.Single(r => r.Metric == TaskMetric.Success).A.Tooltip);
    }
}
