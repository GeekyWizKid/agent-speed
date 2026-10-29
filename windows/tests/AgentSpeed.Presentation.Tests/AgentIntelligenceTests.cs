using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Tasks;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using TaskForm = AgentSpeed.Presentation.TaskScore.TaskForm;

namespace AgentSpeed.Presentation.Tests;

/// <summary>
/// Per-agent 智力 on the dashboard (DESIGN 4, Intelligence column; METRICS 6.9 task score) and the taskbar 智力 block
/// (DESIGN 10, Menu bar and taskbar).
/// </summary>
public class AgentIntelligenceTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly Localizer ZhHant = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);

    [Fact]
    public void ALiveRowShowsItsTaskScoreAndSaysItAfterTheLiveValue()
    {
        var cell = AgentIntelligenceCell.Of(Agent(TaskValueForm.Live, 92.5, TaskState.Passing), ZhHant);
        Assert.Equal(AgentIntelligenceForm.Live, cell.Form);
        Assert.Equal("93", cell.Text);
        Assert.Equal("智力 93", cell.Spoken);
        Assert.Equal((93, false), (cell.Shown, cell.Unproven));
    }

    [Fact]
    public void AnIdleRowRestsOnItsLastTasksScore()
    {
        var cell = AgentIntelligenceCell.Of(Agent(TaskValueForm.Resting, 8.75, TaskState.Unverified), ZhHant);
        Assert.Equal(AgentIntelligenceForm.Resting, cell.Form);
        Assert.Equal("9", cell.Text);
        Assert.Equal("智力 9，尚未驗證，閒置中", cell.Spoken);
        Assert.True(cell.Unproven);
    }

    [Fact]
    public void ARowWithoutAScoreShowsADashAndSaysNothing()
    {
        var cell = AgentIntelligenceCell.Of(Agent(TaskValueForm.None, null, TaskState.None), En);
        Assert.Equal(AgentIntelligenceForm.None, cell.Form);
        Assert.Equal("–", cell.Text);
        Assert.Equal(string.Empty, cell.Spoken);
        Assert.Equal((null, false), (cell.Shown, cell.Unproven));
    }

    [Fact]
    public void ASubAgentShowsItsOwnValue()
    {
        var parent = Agent(TaskValueForm.Resting, 61.25, TaskState.Passing, "p");
        var child = Agent(TaskValueForm.Live, 24, TaskState.Failing, "c") with { IsSubagent = true, ParentId = "p" };
        Assert.Equal(("61", AgentIntelligenceForm.Resting), Pair(AgentIntelligenceCell.Of(parent, En)));
        Assert.Equal(("24", AgentIntelligenceForm.Live), Pair(AgentIntelligenceCell.Of(child, En)));
    }

    [Fact]
    public void AGroupTakesTheHighestLiveScoreOfItsMembers()
    {
        var root = Agent(TaskValueForm.Resting, 98.75, TaskState.Passing, "root");
        var low = Agent(TaskValueForm.Live, 24, TaskState.Failing, "low") with { ParentId = "root" };
        var high = Agent(TaskValueForm.Live, 66.5, TaskState.Pending, "high") with { ParentId = "root" };
        var group = AgentIntelligence.Group(root, [low, high]);
        Assert.Equal((AgentIntelligenceForm.Live, 67, "high", TaskState.Pending), (group.Form, group.Shown, group.Score.AgentId, group.State));

        var resting = AgentIntelligence.Group(root, [Agent(TaskValueForm.None, null, TaskState.None, "quiet")]);
        Assert.Equal((AgentIntelligenceForm.Resting, 99, "root"), (resting.Form, resting.Shown, resting.Score.AgentId));
    }

    [Theory]
    [InlineData(23.2, 20.9, 24)]
    [InlineData(18.6, 21.4, 22)]
    [InlineData(20, 12, 20)]
    public void TheCellIsTheWiderOf888AndTheCaptionRoundedUp(double number, double caption, double width)
    {
        Assert.Equal(width, AgentIntelligenceColumn.Width(number, caption));
        Assert.Equal(width + 6, AgentIntelligenceColumn.Footprint(width));
    }

    [Fact]
    public void TheTaskbarBlockFollowsTheSettingAndNeedsNoMeasuring()
    {
        var chosen = AppSettings.Default with { Traffic = TrafficSettings.Default with { Bar = BarTraffic.Intelligence } };
        Assert.NotNull(BarIntelligenceBlock.From(chosen, Snapshot(IntelligenceSums.Zero), En));
        Assert.NotNull(BarIntelligenceBlock.From(chosen with { Traffic = chosen.Traffic with { Enabled = false } }, Snapshot(IntelligenceSums.Zero), En));
        Assert.Null(BarIntelligenceBlock.From(chosen with { BarMode = BarDisplayMode.GlyphOnly }, Snapshot(IntelligenceSums.Zero), En));
        foreach (var other in new[] { BarTraffic.Off, BarTraffic.Machine, BarTraffic.Agents })
        {
            Assert.Null(BarIntelligenceBlock.From(chosen with { Traffic = chosen.Traffic with { Bar = other } }, Snapshot(IntelligenceSums.Zero), En));
        }

        // The network block never draws for the intelligence choice.
        Assert.Null(BarTrafficBlock.From(chosen, new Core.Traffic.TrafficSnapshot(), En));
    }

    [Fact]
    public void TheTaskbarBlockReadsTheHighestScoreOverTheThinkingRateOfAllSources()
    {
        var chosen = AppSettings.Default with { Traffic = TrafficSettings.Default with { Bar = BarTraffic.Intelligence } };
        // 300 tokens in the window with 30 of reasoning: 30 / 3 s = 10 tok/s.
        var block = BarIntelligenceBlock.From(chosen, Snapshot(new IntelligenceSums(300, 30, 300) { Score = Live(74.25) }), ZhHant)!;
        Assert.Equal(("智力", "74", "思考", "10", "tok/s"), (block.Label, block.Index, block.ThinkingLabel, block.Thinking, block.Unit));
        Assert.Equal(TaskForm.Live, block.Form);
        Assert.False(block.IndexDim);
        Assert.False(block.ThinkingDim);
        Assert.Equal("智力 74，思考每秒 10 個 token", block.Spoken);

        var english = BarIntelligenceBlock.Of(new IntelligenceSums(300, 0, 300) { Score = Live(92) }, En);
        Assert.Equal(("IQ", "92", "TH", "0"), (english.Label, english.Index, english.ThinkingLabel, english.Thinking));
        Assert.True(english.ThinkingDim);
        Assert.True(english.HasScore);
    }

    [Fact]
    public void AQuietBlockStaysWithDashesAndItsUnit()
    {
        var block = BarIntelligenceBlock.Of(IntelligenceSums.Zero, ZhHant);
        Assert.Equal(("–", "–", "tok/s"), (block.Index, block.Thinking, block.Unit));
        Assert.False(block.HasScore);
        Assert.True(block.IndexDim);
        Assert.True(block.ThinkingDim);
        Assert.Equal(ZhHant["intel.none"], block.Spoken);
    }

    [Fact]
    public void TheBlocksCellsNeverChangeWithTheValues()
    {
        // Tabular digits: every digit 6 wide; '.', 'k' and '–' narrower or as wide as one digit.
        static double Number(string text) => text.Sum(c => char.IsDigit(c) ? 6.0 : c switch { '.' => 2.5, 'k' => 5.5, 'M' => 9, _ => 6 });
        static double Text(string text) => text.Length * 7.0;
        var widths = new[] { "–", "0", "100", "9", "4.2", "128", "1.28k", "12.8k" }
            .Select(value => new BarIntelligenceBlock("IQ", "TH", value, value, "tok/s", TaskForm.Live, false, string.Empty).Cells(Text, Number))
            .ToArray();
        Assert.All(widths, cells => Assert.Equal(widths[0], cells));
        Assert.Equal(Number("8.88k"), widths[0].Number);
        Assert.Equal(14 + 3 + Number("8.88k") + 3 + 35, widths[0].Width);

        // From 1 M tok/s the number widens its cell.
        var wide = new BarIntelligenceBlock("IQ", "TH", "100", "1.28M", "tok/s", TaskForm.Live, false, string.Empty).Cells(Text, Number);
        Assert.Equal(Number("1.28M"), wide.Number);
    }

    [Fact]
    public void TheIntelligenceChoiceIsStoredAndTheThreeOlderChoicesReadAsBefore()
    {
        foreach (var (key, bar) in new[]
        {
            ("off", BarTraffic.Off), ("machine", BarTraffic.Machine), ("agents", BarTraffic.Agents), ("intelligence", BarTraffic.Intelligence),
        })
        {
            Assert.Equal(key, TrafficSettings.Key(bar));
            Assert.Equal(bar, SettingsSerializer.Deserialize("{\"traffic\":{\"bar\":\"" + key + "\"}}").Traffic.Bar);
            var settings = AppSettings.Default with { Traffic = TrafficSettings.Default with { Bar = bar } };
            Assert.Equal(bar, SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings)).Traffic.Bar);
        }

        Assert.Equal(BarTraffic.Off, SettingsSerializer.Deserialize("""{"traffic":{"bar":"iq"}}""").Traffic.Bar);
        Assert.Equal(BarTraffic.Off, (TrafficSettings.Default with { Bar = (BarTraffic)9 }).Validated().Bar);
        Assert.Equal([0, 1, 2, 3], new[] { BarTraffic.Off, BarTraffic.Machine, BarTraffic.Agents, BarTraffic.Intelligence }.Select(b => (int)b));
        Assert.True(TrafficSettings.IsNetwork(BarTraffic.Machine) && TrafficSettings.IsNetwork(BarTraffic.Agents));
        Assert.False(TrafficSettings.IsNetwork(BarTraffic.Intelligence) || TrafficSettings.IsNetwork(BarTraffic.Off));
    }

    private static TaskSetValue Live(double score) => new(TaskValueForm.Live, score, TaskRounding.HalfAwayFromZero(score), "a");

    private static AgentStatus Agent(TaskValueForm form, double? score, TaskState state, string id = "a")
    {
        var value = score is { } s ? new TaskValue(form, s, TaskRounding.HalfAwayFromZero(s), state) : new TaskValue(TaskValueForm.None, null, null, state);
        var set = TaskSetValue.Of(id, 0, value);
        return new(id, SourceKind.Codex, "atlas", "model", form == TaskValueForm.Live ? AgentPhase.Generating : AgentPhase.Idle, false,
            Rate.Zero, null, DateTimeOffset.UnixEpoch)
        {
            Intelligence = new AgentIntelligence(IntelligenceSums.Zero with { Score = set }, set, score is null ? null : state),
        };
    }

    private static (string, AgentIntelligenceForm) Pair(AgentIntelligenceCell cell) => (cell.Text, cell.Form);

    private static SpeedSnapshot Snapshot(IntelligenceSums sums) => new(
        DateTimeOffset.UnixEpoch, MonitorState.Running, Rate.Zero, 0, Rate.Zero, null, null, TokenTotals.Zero,
        [new SourceStatus(SourceKind.ClaudeCode, true, string.Empty, true, 1, Rate.Zero, TokenTotals.Zero, 0) { Intelligence = sums }],
        [], 0, 0, [], null, []);
}
