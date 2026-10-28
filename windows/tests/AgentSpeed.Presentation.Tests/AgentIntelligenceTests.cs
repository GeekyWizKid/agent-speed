using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests;

/// <summary>
/// Per-agent intelligence on the dashboard (DESIGN 4, Intelligence column) and the taskbar intelligence block (DESIGN 10,
/// Menu bar and taskbar).
/// </summary>
public class AgentIntelligenceTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly Localizer ZhHant = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);

    /// <summary>gpt-5.5 at xhigh: standard, level 4.</summary>
    private static readonly ModelGrade Standard104 = new(ModelClass.Standard, 4);

    [Fact]
    public void ALiveRowShowsItsOwnIndexAndSaysItAfterTheLiveValue()
    {
        var cell = AgentIntelligenceCell.Of(Agent(Live(105, share: 0.25), Standard104), ZhHant);
        Assert.Equal(AgentIntelligenceForm.Live, cell.Form);
        Assert.Equal("105", cell.Text);
        Assert.Equal("智力 105", cell.Spoken);
        Assert.Equal(["智力 105 · 思考 25%", "等級 104 · 標準"], cell.TooltipLines);
    }

    [Fact]
    public void ALiveValueLowerThanTheGradeIsStillTheLiveValue()
    {
        var cell = AgentIntelligenceCell.Of(Agent(Live(97, share: 0), new ModelGrade(ModelClass.Flagship, 4)), En);
        Assert.Equal("97", cell.Text);
        Assert.Equal(En.Format("a11y.agent.intel", "97"), cell.Spoken);
    }

    [Fact]
    public void AnIdleRowRestsOnItsGrade()
    {
        var cell = AgentIntelligenceCell.Of(Agent(IntelligenceSums.Zero, new ModelGrade(ModelClass.Light, 1)), ZhHant);
        Assert.Equal(AgentIntelligenceForm.Resting, cell.Form);
        Assert.Equal("66", cell.Text);
        Assert.Equal("等級 66", cell.Spoken);
        Assert.Equal(["等級 66 · 輕量"], cell.TooltipLines);
    }

    [Fact]
    public void ARowWithoutAModelShowsADashAndSaysNothing()
    {
        var cell = AgentIntelligenceCell.Of(Agent(IntelligenceSums.Zero, null), En);
        Assert.Equal(AgentIntelligenceForm.None, cell.Form);
        Assert.Equal("–", cell.Text);
        Assert.Equal(string.Empty, cell.Spoken);
        Assert.Empty(cell.TooltipLines);
    }

    [Fact]
    public void AModelLessAgentThatGeneratesIsLiveWithoutAGradeLine()
    {
        var cell = AgentIntelligenceCell.Of(Agent(Live(92, share: 0.5), null), En);
        Assert.Equal(AgentIntelligenceForm.Live, cell.Form);
        Assert.Equal([En.Format("intel.tooltip", "92", "50%")], cell.TooltipLines);
    }

    [Fact]
    public void ASourceWithoutSeparateReasoningShowsNoThinkingShare()
    {
        var cell = AgentIntelligenceCell.Of(Agent(Live(92, share: 0), Standard104, SourceKind.DeepSeek), ZhHant);
        Assert.Equal("智力 92 · 思考 –", cell.TooltipLines[0]);
    }

    [Fact]
    public void ASubAgentShowsItsOwnValue()
    {
        var parent = Agent(IntelligenceSums.Zero, new ModelGrade(ModelClass.Flagship, 3)) with { Id = "p" };
        var child = Agent(Live(94, share: 0.4), new ModelGrade(ModelClass.Light, 1)) with { Id = "c", IsSubagent = true, ParentId = "p" };
        Assert.Equal(("118", AgentIntelligenceForm.Resting), Pair(AgentIntelligenceCell.Of(parent, En)));
        Assert.Equal(("94", AgentIntelligenceForm.Live), Pair(AgentIntelligenceCell.Of(child, En)));
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
    public void TheTaskbarBlockReadsTheIndexOverTheThinkingRateOfAllSources()
    {
        var chosen = AppSettings.Default with { Traffic = TrafficSettings.Default with { Bar = BarTraffic.Intelligence } };
        // 300 tokens in the window at grade 123 with 10 % reasoning: 123 + 20 × 0.1 = 125, 30 / 3 s = 10 tok/s.
        var block = BarIntelligenceBlock.From(chosen, Snapshot(new IntelligenceSums(300, 300 * 123, 30, 300)), ZhHant)!;
        Assert.Equal(("智力", "125", "思考", "10", "tok/s"), (block.Label, block.Index, block.ThinkingLabel, block.Thinking, block.Unit));
        Assert.False(block.Quiet);
        Assert.False(block.ThinkingDim);
        Assert.Equal("智力 125，思考每秒 10 個 token", block.Spoken);

        var english = BarIntelligenceBlock.Of(new IntelligenceSums(300, 300 * 92, 0, 300), En);
        Assert.Equal(("IQ", "92", "TH", "0"), (english.Label, english.Index, english.ThinkingLabel, english.Thinking));
        Assert.True(english.ThinkingDim);
        Assert.False(english.Quiet);
    }

    [Fact]
    public void AQuietBlockStaysWithDashesAndItsUnit()
    {
        var block = BarIntelligenceBlock.Of(IntelligenceSums.Zero, ZhHant);
        Assert.Equal(("–", "–", "tok/s"), (block.Index, block.Thinking, block.Unit));
        Assert.True(block.Quiet);
        Assert.True(block.ThinkingDim);
        Assert.Equal("智力：目前沒有輸出", block.Spoken);
    }

    [Fact]
    public void TheBlocksCellsNeverChangeWithTheValues()
    {
        // Tabular digits: every digit 6 wide; '.', 'k' and '–' narrower or as wide as one digit.
        static double Number(string text) => text.Sum(c => char.IsDigit(c) ? 6.0 : c switch { '.' => 2.5, 'k' => 5.5, 'M' => 9, _ => 6 });
        static double Text(string text) => text.Length * 7.0;
        var widths = new[] { "–", "60", "136", "0", "4.2", "128", "1.28k", "12.8k" }
            .Select(value => new BarIntelligenceBlock("IQ", "TH", value, value, "tok/s", false, false, string.Empty).Cells(Text, Number))
            .ToArray();
        Assert.All(widths, cells => Assert.Equal(widths[0], cells));
        Assert.Equal(Number("8.88k"), widths[0].Number);
        Assert.Equal(14 + 3 + Number("8.88k") + 3 + 35, widths[0].Width);

        // From 1 M tok/s the number widens its cell.
        var wide = new BarIntelligenceBlock("IQ", "TH", "130", "1.28M", "tok/s", false, false, string.Empty).Cells(Text, Number);
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

    /// <summary>Window sums whose index is <paramref name="index"/> with the given reasoning share.</summary>
    private static IntelligenceSums Live(double index, double share) =>
        new(300, 300 * (index - IntelligenceSums.ThinkingBonus * share), 300 * share, 300);

    private static AgentStatus Agent(IntelligenceSums sums, ModelGrade? grade, SourceKind source = SourceKind.Codex) =>
        new("a", source, "atlas", grade is null ? null : "model", sums.IsActive ? AgentPhase.Generating : AgentPhase.Idle, false,
            Rate.Zero, null, DateTimeOffset.UnixEpoch)
        {
            Grade = grade,
            Intelligence = new AgentIntelligence(sums, grade),
        };

    private static (string, AgentIntelligenceForm) Pair(AgentIntelligenceCell cell) => (cell.Text, cell.Form);

    private static SpeedSnapshot Snapshot(IntelligenceSums sums) => new(
        DateTimeOffset.UnixEpoch, MonitorState.Running, Rate.Zero, 0, Rate.Zero, null, null, TokenTotals.Zero,
        [new SourceStatus(SourceKind.ClaudeCode, true, string.Empty, true, 1, Rate.Zero, TokenTotals.Zero, 0) { Intelligence = sums }],
        [], 0, 0, [], null, []);
}
