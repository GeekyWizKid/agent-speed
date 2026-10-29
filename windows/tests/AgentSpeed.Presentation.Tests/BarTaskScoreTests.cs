using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.TaskScore;
using CoreTasks = AgentSpeed.Core.Tasks;

namespace AgentSpeed.Presentation.Tests;

/// <summary>
/// 智力 in the taskbar block (DESIGN 10, Menu bar and taskbar; DESIGN 12): the highest score of all enabled sources' agents,
/// its forms and words, the proof mark at 0.17 × the size and the tooltip's supplying agent.
/// </summary>
public class BarTaskScoreTests
{
    private static readonly Localizer ZhHant = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly AppSettings Chosen = AppSettings.Default with { BarIntelligence = true };

    private static AgentStatus Agent(string id, string project, CoreTasks.TaskValueForm form, double score, CoreTasks.TaskState state,
        double thinking = 0, SourceKind source = SourceKind.ClaudeCode)
    {
        var set = CoreTasks.TaskSetValue.Of(id, 0, new CoreTasks.TaskValue(form, score, CoreTasks.TaskRounding.HalfAwayFromZero(score), state));
        var sums = new IntelligenceSums(thinking > 0 ? 3 * thinking : 0, 3 * thinking, thinking > 0 ? 3 * thinking : 0) { Score = set };
        return new AgentStatus(id, source, project, "model", form == CoreTasks.TaskValueForm.Live ? AgentPhase.Generating : AgentPhase.Idle,
            false, Rate.Zero, null, DateTimeOffset.UnixEpoch)
        {
            Intelligence = new AgentIntelligence(sums, set, state),
        };
    }

    /// <summary>A snapshot of the agents' own sources, each source summing its agents' window and taking their highest score.</summary>
    private static SpeedSnapshot Snapshot(params AgentStatus[] agents) => new(
        DateTimeOffset.UnixEpoch, MonitorState.Running, Rate.Zero, 0, Rate.Zero, null, null, TokenTotals.Zero,
        [.. agents.GroupBy(a => a.Source).Select(group => new SourceStatus(group.Key, true, string.Empty, true, 1, Rate.Zero, TokenTotals.Zero, 0)
        {
            Intelligence = group.Aggregate(IntelligenceSums.Zero, (sum, agent) => sum + agent.Intelligence.Sums),
        })],
        agents, 0, 0, [], null, []);

    [Fact]
    public void TheBlockShowsTheHighestScoreOfEveryEnabledSourceLiveBeforeResting()
    {
        var snapshot = Snapshot(
            Agent("a1", "nova", CoreTasks.TaskValueForm.Live, 86, CoreTasks.TaskState.Passing, thinking: 128),
            Agent("a2", "rocket", CoreTasks.TaskValueForm.Live, 52, CoreTasks.TaskState.Failing, source: SourceKind.Codex),
            Agent("a3", "atlas", CoreTasks.TaskValueForm.Resting, 97, CoreTasks.TaskState.Passing, source: SourceKind.Codex));
        var block = BarIntelligenceBlock.From(Chosen, snapshot, ZhHant)!;
        Assert.Equal(("86", TaskForm.Live, false, false), (block.Index, block.Form, block.IndexDim, block.Unproven));
        Assert.Equal("128", block.Thinking);
        Assert.Equal("智力 86，2 個 agent 中最高，思考每秒 128 個 token", block.Spoken);
        Assert.Equal("智力 86 · 最高：nova", block.Top);
    }

    [Fact]
    public void ARestingScoreStaysPaleAfterWorkAndStandsAloneOnceThinkingHasReadZero()
    {
        var hold = new ThinkingZeroHold();
        var snapshot = Snapshot(
            Agent("a1", "nova", CoreTasks.TaskValueForm.Resting, 72, CoreTasks.TaskState.Passing),
            Agent("a2", "atlas", CoreTasks.TaskValueForm.Resting, 40, CoreTasks.TaskState.Passing));
        var first = BarIntelligenceBlock.From(Chosen, snapshot, ZhHant, hold, 0)!;

        // Nothing generated: the thinking number is `–` and the words leave it out.
        Assert.Equal(("72", "–"), (first.Index, first.Thinking));
        Assert.Equal((TaskForm.Resting, true, true), (first.Form, first.IndexDim, first.ThinkingDim));
        Assert.False(first.ThinkingHidden);
        Assert.Equal("智力 72，閒置中，2 個 agent 中最高", first.Spoken);

        var alone = BarIntelligenceBlock.From(Chosen, snapshot, ZhHant, hold, 3)!;
        Assert.True(alone.ThinkingHidden);
        Assert.Equal(first.Spoken, alone.Spoken);
        Assert.Equal("智力 72 · 最高：nova", alone.Top);
    }

    [Fact]
    public void NoScoreReadsADashAndItsOwnWordsWithoutATooltipLine()
    {
        var quiet = BarIntelligenceBlock.Of(IntelligenceSums.Zero, ZhHant);
        Assert.Equal(("–", "–", TaskForm.None), (quiet.Index, quiet.Thinking, quiet.Form));
        Assert.Equal("智力：尚無評分", quiet.Spoken);
        Assert.Null(quiet.Top);
        Assert.Empty(quiet.ProofDots(0, 18, 20, 10, 1, false));

        // Agents that think before any task has a score: the thinking words stay after intel.none.
        var reading = BarIntelligenceBlock.Of(new IntelligenceSums(300, 30, 300), En);
        Assert.Equal(("–", "10"), (reading.Index, reading.Thinking));
        Assert.Equal("intelligence: no score yet, thinking 10 tokens per second", reading.Spoken);
        Assert.False(reading.ThinkingZero);
    }

    [Fact]
    public void AnUnprovenScoreSaysSoAndNamesTheSourceWithoutAProject()
    {
        var snapshot = Snapshot(Agent("a1", string.Empty, CoreTasks.TaskValueForm.Live, 9.4, CoreTasks.TaskState.Unverified, thinking: 64));
        var block = BarIntelligenceBlock.From(Chosen, snapshot, ZhHant)!;
        Assert.True(block.Unproven);
        Assert.Equal("智力 9，尚未驗證，思考每秒 64 個 token", block.Spoken);
        Assert.Equal(ZhHant.Format("task.tooltip.top", "9", ZhHant[DisplayText.SourceKey(SourceKind.ClaudeCode)]), block.Top);

        var pending = BarIntelligenceBlock.From(Chosen, Snapshot(Agent("a1", "nova", CoreTasks.TaskValueForm.Live, 34,
            CoreTasks.TaskState.Pending)), En)!;
        Assert.True(pending.Unproven);
        Assert.StartsWith("intelligence 34, re-check pending", pending.Spoken, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TheProofMarkSitsInTheRoomLineTwoLeavesOnTheDevicePixelGrid(double scale)
    {
        var block = BarIntelligenceBlock.Of(new TaskSetReading(TaskForm.Live, 9, TaskState.Unverified, 1, null), TestSums.Of(300, 9), ZhHant);
        const double left = 40.3;
        const double width = 12.2;
        const double baseline = 17.6;
        var dots = block.ProofDots(left, width, baseline, 10, scale, highContrast: false);
        var mark = TaskProofMark.Underline(10, width, scale, barBlock: true);
        Assert.Equal(mark.Centers.Count, dots.Count);
        Assert.All(dots, dot =>
        {
            Assert.Equal(mark.Diameter, dot.Diameter, 9);
            Assert.Equal(Math.Round(dot.X * scale), dot.X * scale, 6);
            Assert.Equal(Math.Round(dot.Y * scale), dot.Y * scale, 6);

            // Centred 0.17 × 10 below the baseline, within the half pixel the grid moves it.
            Assert.InRange(dot.Y + dot.Diameter / 2 - (baseline + 1.7), -0.5 / scale - 1e-9, 0.5 / scale + 1e-9);
        });
        Assert.InRange(dots[0].X, left - 0.5 / scale, left + 0.5 / scale);
        Assert.InRange(dots[^1].X + dots[^1].Diameter, left + width - 0.5 / scale, left + width + 0.5 / scale);

        // High contrast: the dots half a point thicker before rounding to the device pixel (at 150 % both round to 2 px).
        var contrast = block.ProofDots(left, width, baseline, 10, scale, highContrast: true);
        Assert.Equal(TaskProofMark.Diameter(10, scale, highContrast: true), contrast[0].Diameter, 9);
        Assert.True(contrast[0].Diameter >= dots[0].Diameter);
    }

    [Fact]
    public void AProvenScoreHasNoProofMark()
    {
        var block = BarIntelligenceBlock.Of(TestSums.Of(300, 74), ZhHant);
        Assert.False(block.Unproven);
        Assert.Empty(block.ProofDots(0, 12, 17, 10, 1.5, false));
    }
}
