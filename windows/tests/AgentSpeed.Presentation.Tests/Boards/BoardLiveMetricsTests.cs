using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Live;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Titles;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;

namespace AgentSpeed.Presentation.Tests.Boards;

/// <summary>DESIGN 2c, Live metrics (智力 · 思考 · 費用): layout B per style and size, switches, rows, wording.</summary>
public class BoardLiveMetricsTests
{
    private static readonly Localizer Zh = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);

    // The Hero table: ranking starts and heights per style and size.
    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.S, 104, 138)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 116, 186)]
    [InlineData(BoardStyle.Glass, BoardSize.L, 128, 254)]
    [InlineData(BoardStyle.Timing, BoardSize.S, 109, 139)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 121, 187)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 131, 257)]
    [InlineData(BoardStyle.Neon, BoardSize.S, 108, 136)]
    [InlineData(BoardStyle.Neon, BoardSize.M, 118, 186)]
    [InlineData(BoardStyle.Neon, BoardSize.L, 128, 256)]
    public void TheRankingStartsAndRunsAsTheHeroTableSays(BoardStyle style, BoardSize size, double top, double height)
    {
        var metrics = BoardMetrics.For(style, size, BoardReadouts.All);
        Assert.Equal(top, metrics.ListTop, 6);
        Assert.Equal(height, metrics.ListHeight, 6);
        Assert.True(metrics.Gauge);
        Assert.Equal(style == BoardStyle.Timing ? 16 : 14, metrics.StartY, 6);
        Assert.Equal(height - metrics.StartY, metrics.Room, 6);
        Assert.Equal(size == BoardSize.S ? BoardMetrics.For(style, size).GroupHeight : size == BoardSize.L ? 40 : 38, metrics.GroupHeight, 6);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.S, 12, 78, 14)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 14, 86, 16)]
    [InlineData(BoardStyle.Glass, BoardSize.L, 16, 94, 18)]
    [InlineData(BoardStyle.Timing, BoardSize.S, 14, 72, 10)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 16, 80, 12)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 18, 88, 12)]
    public void GlassAndTimingMoveTheirHistoryToAFullWidthStrip(BoardStyle style, BoardSize size, double top, double band, double strip)
    {
        var metrics = BoardMetrics.For(style, size, BoardReadouts.All);
        var hero = BoardHeroLayout.For(style, size, BoardReadouts.All);
        Assert.Equal(top, metrics.HeroTop, 6);
        Assert.Equal(band, metrics.HeroHeight, 6);
        Assert.Equal(0, hero.History.Left, 6);
        Assert.Equal(hero.Width, hero.History.Width, 6);
        Assert.Equal(strip, hero.History.Height, 6);
        Assert.Equal(style == BoardStyle.Timing ? band + 8 : band, hero.History.Top, 6);
    }

    [Theory]
    [InlineData(BoardSize.S, 92)]
    [InlineData(BoardSize.M, 100)]
    [InlineData(BoardSize.L, 108)]
    public void NeonsHeroGrowsAndKeepsItsLedBar(BoardSize size, double band)
    {
        var hero = BoardHeroLayout.For(BoardStyle.Neon, size, BoardReadouts.All);
        Assert.Equal(band, hero.Height, 6);
        Assert.Equal(band - 6, hero.History.Bottom, 6);
        Assert.Equal(hero.Width, hero.History.Width, 6);
        // The left column is centred in the band less the bar's 17 pt.
        var column = 1.08 * hero.NumberSize + 8 + 12;
        Assert.Equal((band - 17 - column) / 2, hero.NumberTop, 6);
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Timing)]
    [InlineData(BoardStyle.Neon)]
    public void EveryReadoutOffKeepsTheOriginalHeroAndTheSecondLine(BoardStyle style)
    {
        foreach (var size in BoardSizes.All)
        {
            Assert.Equal(BoardMetrics.For(style, size), BoardMetrics.For(style, size, BoardReadouts.None));
            Assert.Equal(BoardHeroLayout.For(style, size), BoardHeroLayout.For(style, size, BoardReadouts.None));
            // The original hero and list; group rows keep their second line at M and L (8 taller), S stays one line.
            var none = BoardMetrics.For(style, size, BoardReadouts.None);
            var all = BoardMetrics.For(style, size, BoardReadouts.All);
            Assert.False(none.Gauge);
            Assert.False(none.IntelligenceColumn);
            Assert.Equal(size != BoardSize.S, none.TwoLineRows);
            Assert.Equal(size != BoardSize.S, all.TwoLineRows);
            Assert.Equal(all.GroupHeight, none.GroupHeight, 6);
        }
    }

    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.M, 6, 8, 44, 22, 0)]
    [InlineData(BoardStyle.Neon, BoardSize.S, 6, 8, 44, 20, 0)]
    [InlineData(BoardStyle.Timing, BoardSize.S, 4, 7, 40, 22, 0)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 4, 7, 42, 26, 34)]
    public void RowsGainThe智力ColumnBeforeTheValue(BoardStyle style, BoardSize size, double pad, double gap, double value, double cell, double interval)
    {
        var columns = BoardRowColumns.For(style, size, BoardReadouts.All);
        Assert.Equal(new BoardRowColumns(pad, gap, value, cell, interval), columns);
        Assert.Equal(pad + value + gap, columns.IntelligenceRight, 6);
        var plain = BoardRowColumns.For(style, size, BoardReadouts.All with { Intelligence = false });
        Assert.Equal(style == BoardStyle.Timing && size == BoardSize.S ? 44 : 46, plain.Value, 6);
        Assert.Equal(0, plain.Intelligence, 6);
        Assert.Equal(style == BoardStyle.Timing && size != BoardSize.S ? 38 : 0, plain.Interval, 6);
    }

    // The second line holds only the detail: it runs from under the name to the value's right edge, past the capsule and 智力 columns.
    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.M)]
    [InlineData(BoardStyle.Timing, BoardSize.L)]
    [InlineData(BoardStyle.Neon, BoardSize.M)]
    public void TheDetailRunsToTheValuesRightEdge(BoardStyle style, BoardSize size)
    {
        var metrics = BoardMetrics.For(style, size, BoardReadouts.All);
        foreach (var readouts in new[] { BoardReadouts.All, BoardReadouts.None })
        {
            var columns = BoardRowColumns.For(style, size, readouts);
            var width = metrics.ListWidth;
            Assert.Equal(width - columns.ValueRight, columns.DetailRight(width), 6);
            Assert.True(columns.DetailRight(width) > width - columns.CapsuleRight);
        }
    }

    // The per-style tables: heights with 思考 shown and hidden, and the baselines.
    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.S, 75, 58, 51, 68)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 84, 67, 62, 79)]
    [InlineData(BoardStyle.Glass, BoardSize.L, 90, 73, 69, 86)]
    [InlineData(BoardStyle.Timing, BoardSize.S, 51, 36, 32, 47)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 58, 41, 36.5, 53.5)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 65, 46, 41, 60)]
    [InlineData(BoardStyle.Neon, BoardSize.S, 74.5, 57.5, 50.5, 67.5)]
    [InlineData(BoardStyle.Neon, BoardSize.M, 83, 65, 59, 77)]
    [InlineData(BoardStyle.Neon, BoardSize.L, 87.5, 69.5, 64.5, 82.5)]
    public void TheSubGaugeFollowsItsStylesTable(BoardStyle style, BoardSize size, double height, double hidden, double thinking, double cost)
    {
        var spec = BoardGaugeSpec.For(style, size);
        Assert.Equal(height, BoardGaugeLayout.Height(style, spec, BoardReadouts.All, true), 6);
        Assert.Equal(hidden, BoardGaugeLayout.Height(style, spec, BoardReadouts.All, false), 6);
        var shown = Place(style, size, BoardReadouts.All, 0);
        Assert.Equal(thinking, shown.ThinkingBaseline - shown.Top, 6);
        Assert.Equal(cost, shown.CostBaseline - shown.Top, 6);
        // Hidden: 費用 takes 思考's baseline and the sub-gauge re-centres.
        var gone = Place(style, size, BoardReadouts.All, 1);
        Assert.Equal(thinking, gone.CostBaseline - gone.Top, 6);
        Assert.Equal(shown.Top + (height - hidden) / 2, gone.Top, 6);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.M, 112, 86)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 134, 80)]
    [InlineData(BoardStyle.Neon, BoardSize.L, 128, 108)]
    public void TheSubGaugeIsRightAlignedAndCentredInItsBand(BoardStyle style, BoardSize size, double nominal, double band)
    {
        var placement = Place(style, size, BoardReadouts.All, 0, band: band);
        Assert.Equal(nominal, placement.Width, 6);
        Assert.Equal(300, placement.Left + placement.Width, 6);
        var room = style == BoardStyle.Neon ? band - 17 : band;
        Assert.Equal((room - placement.Height) / 2, placement.Top, 6);
    }

    [Fact]
    public void AWiderTableGrowsTheSubGaugeToTheLeft()
    {
        var narrow = Place(BoardStyle.Glass, BoardSize.M, BoardReadouts.All, 0);
        var wide = BoardGaugeLayout.Place(BoardStyle.Glass, BoardSize.M, BoardReadouts.All, new BoardGaugeCells(40, 8, 60, 30), 300, 86, 0, 1);
        Assert.Equal(112, narrow.Width, 6);
        Assert.Equal(40 + 4 + 8 + 1 + 60 + 3 + 30, wide.Width, 6);
        Assert.Equal(300, wide.Left + wide.Width, 6);
        Assert.Equal(wide.Left, wide.TableLeft, 6);
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Neon)]
    [InlineData(BoardStyle.Timing)]
    public void SwitchedOffReadoutsTakeNoRoom(BoardStyle style)
    {
        var spec = BoardGaugeSpec.For(style, BoardSize.M);
        var only = BoardReadouts.None with { Intelligence = true };
        Assert.Equal(spec.MainHeight, BoardGaugeLayout.Height(style, spec, only, true), 6);
        // 智力 off: the table alone (timing: the box starts with 思考 at the small row height).
        var table = BoardReadouts.None with { Thinking = true, Cost = true };
        var expected = style == BoardStyle.Timing ? 2 * spec.RowPitch : BoardGaugeSpec.RowHeight + spec.RowPitch;
        Assert.Equal(expected, BoardGaugeLayout.Height(style, spec, table, true), 6);
        var cost = BoardReadouts.None with { Intelligence = true, Cost = true };
        var withCost = Place(style, BoardSize.M, cost, 0);
        Assert.Equal(style == BoardStyle.Timing ? 36.5 : style == BoardStyle.Glass ? 62 : 59, withCost.CostBaseline - withCost.Top, 6);
    }

    [Fact]
    public void ThinkingHidesAfterThreeSecondsAtZeroAndReturnsAtOnce()
    {
        var hold = new BoardThinkingHold();
        var zero = Hero("0", quiet: false, zero: true);
        var busy = Hero("12", quiet: false, zero: false);
        Assert.False(hold.Update(BoardReadouts.All, zero, 10));
        Assert.False(hold.Update(BoardReadouts.All, zero, 12.5));
        Assert.True(hold.Update(BoardReadouts.All, zero, 13));
        // Quiet changes nothing: a hidden 思考 stays hidden.
        Assert.True(hold.Update(BoardReadouts.All, Hero("–", quiet: true, zero: false), 20));
        Assert.False(hold.Update(BoardReadouts.All, busy, 20.5));
        // With 思考 the only hero readout it never hides.
        var only = BoardReadouts.None with { Thinking = true };
        Assert.False(hold.Update(only, zero, 30));
        Assert.False(hold.Update(only, zero, 40));
        // Switching it off and on shows it.
        Assert.False(hold.Update(BoardReadouts.All, zero, 41));
        Assert.False(hold.Update(BoardReadouts.All with { Thinking = false }, zero, 50));
        Assert.False(hold.Update(BoardReadouts.All, zero, 51));
    }

    [Fact]
    public void TheHeroReadsIntelligenceThinkingAndCost()
    {
        var texts = new BoardTexts(Zh, TitleTheme.Vehicles, new CostDisplay(Zh));
        var sums = new IntelligenceSums(2121, 2121 * 118, 3 * 174, 2121);
        var hero = texts.Hero(sums, new CostReading(24_000, 1, 707, CostTally.Zero, 0, [], [], CostState.Priced));
        Assert.Equal("123", hero.Intelligence);
        Assert.Equal(123, hero.Shown);
        Assert.Equal("174", hero.Thinking);
        Assert.False(hero.Quiet);
        Assert.Equal("0.024", hero.Cost.Number);
        Assert.Equal(Zh.Format("cost.unitPerSecond", "US$"), hero.Cost.Unit);
        Assert.Equal(BoardMoneyState.Value, hero.Cost.State);

        var quiet = texts.Hero(IntelligenceSums.Zero, CostReading.Zero);
        Assert.Equal(("–", "–", true), (quiet.Intelligence, quiet.Thinking, quiet.Quiet));
        Assert.Equal(("0", BoardMoneyState.Zero), (quiet.Cost.Number, quiet.Cost.State));

        var partial = texts.Hero(sums, new CostReading(24_000, 0.5, 707, CostTally.Zero, 0, [], [], CostState.Partial));
        Assert.True(partial.Cost.AtLeast);
        Assert.Equal(Zh.Format("a11y.orb.costAtLeast", "0.024"), partial.Cost.SpokenNumber);
        var unknown = texts.Hero(sums, new CostReading(0, 0, 707, CostTally.Zero, 0, [], [], CostState.NothingPriced));
        Assert.Equal(("–", BoardMoneyState.Unknown, false), (unknown.Cost.Number, unknown.Cost.State, unknown.Cost.HasUnit));
        var tag = texts.Hero(sums, new CostReading(0, 1, 0, CostTally.Zero, 0, [], [SourceKind.ClaudeCode], CostState.SubscriptionOnly));
        Assert.Equal((Zh["cost.subscription.tag"], BoardMoneyState.Tag), (tag.Cost.Number, tag.Cost.State));
        var zero = texts.Hero(new IntelligenceSums(300, 300 * 115, 0, 300), CostReading.Zero);
        Assert.True(zero.ThinkingZero);
        Assert.Equal("0", zero.Thinking);
    }

    [Fact]
    public void TheBoardsLabelAddsTheSwitchedOnReadouts()
    {
        var feed = new BoardDemoFeed(BoardSimScenario.Team) { Localizer = Zh, Readouts = BoardReadouts.All };
        var tick = feed.Step(BoardStyle.Glass, BoardSize.M);
        var board = new BoardDefinition("b");
        var label = BoardAccessibility.Board(board, tick, "噴射客機", Zh);
        Assert.StartsWith(BoardAccessibility.Board(board, tick.ValueText, "噴射客機", Zh), label, StringComparison.Ordinal);
        Assert.Contains(Zh.Format("a11y.orb.intel", tick.Gauge.Intelligence, tick.Gauge.Thinking), label, StringComparison.Ordinal);
        Assert.EndsWith(Zh.Format("a11y.board.cost", tick.Gauge.Cost.SpokenNumber, Zh["currency.USD"]), label, StringComparison.Ordinal);

        var hidden = tick with { ThinkingHidden = true };
        Assert.Contains(Zh.Format("a11y.orb.intel.only", tick.Gauge.Intelligence), BoardAccessibility.Board(board, hidden, "t", Zh),
            StringComparison.Ordinal);
        var noIntel = tick with { Readouts = BoardReadouts.All with { Intelligence = false, Cost = false } };
        Assert.EndsWith(Zh.Format("a11y.board.thinking", tick.Gauge.Thinking), BoardAccessibility.Board(board, noIntel, "t", Zh),
            StringComparison.Ordinal);
        var plain = tick with { Readouts = BoardReadouts.None };
        Assert.Equal(BoardAccessibility.Board(board, tick.ValueText, "t", Zh), BoardAccessibility.Board(board, plain, "t", Zh));
    }

    [Fact]
    public void TheExampleLabelReadsAsTheSpecWritesIt()
    {
        var tick = new BoardTick(707, "707", SpeedTier.Boost, 2000, SpeedMetric.Output, true, [], [], false)
        {
            Readouts = BoardReadouts.All,
            Gauge = new BoardHeroReadouts("123", 123, "174", false, false,
                new BoardMoney("0.024", "US$/s", false, BoardMoneyState.Value, "0.024", Zh["currency.USD"])),
        };
        Assert.Equal("排行看板，全部來源：每秒 707 個 token，噴射客機，智力 123，思考每秒 174 個 token，費用每秒 0.024 美元（API 等值）",
            BoardAccessibility.Board(new BoardDefinition("b"), tick, "噴射客機", Zh));
    }

    [Fact]
    public void GroupRowsCarryTheirIntelligenceAndTooltipLines()
    {
        var texts = new BoardTexts(En, TitleTheme.Vehicles, new CostDisplay(En));
        var root = Agent("r", 120) with
        {
            Grade = new ModelGrade(ModelClass.Flagship, 4),
            Intelligence = new AgentIntelligence(new IntelligenceSums(360, 360 * 124, 90, 360), new ModelGrade(ModelClass.Flagship, 4)),
            LiveCost = new LiveCost(6_000, 120),
        };
        var group = BoardGrouping.Group([root], SpeedMetric.Output)[0];
        var entry = new BoardEntry(BoardEntryKind.Group, "r", group, root, 1, 0, 38) { TwoLine = true };
        var row = texts.Row(entry, BoardReadouts.All, group.Live)!;
        Assert.Equal("129", row.Intelligence);
        Assert.Equal(AgentIntelligenceForm.Live, row.Form);
        Assert.Equal(En.Format("a11y.agent.intel", "129"), row.Spoken);
        Assert.Equal(
            [
                En.Format("intel.tooltip", "129", "25%"), En.Format("intel.grade", "124", En["intel.class.flagship"]),
                En.Format("boards.tooltip.cost", "0.006 " + En.Format("cost.unitPerSecond", "$")), En["intel.disclaimer"],
            ],
            row.TooltipLines);
        // The row's label names no cost: it ends with the 智力 words.
        Assert.Equal(En.Format("a11y.board.row", 1, root.Project, SpeedFormat.Speed(group.Live)) + ", " + row.Spoken,
            BoardAccessibility.Row(entry with { Readout = row }, En));

        // An idle group: no cost line, its root's grade at rest.
        var idle = root with { Live = Rate.Zero, Intelligence = new AgentIntelligence(IntelligenceSums.Zero, root.Grade) };
        var resting = BoardGrouping.Group([idle], SpeedMetric.Output)[0];
        var quiet = texts.Row(new BoardEntry(BoardEntryKind.Group, "r", resting, idle, 1, 0, 38), BoardReadouts.All, 0)!;
        Assert.Equal(("124", AgentIntelligenceForm.Resting), (quiet.Intelligence, quiet.Form));
        Assert.DoesNotContain(quiet.TooltipLines, l => l.StartsWith(En.Format("boards.tooltip.cost", string.Empty).TrimEnd(), StringComparison.Ordinal));

        // 費用 off: the group's cost leaves the tooltip too.
        Assert.DoesNotContain(En.Format("boards.tooltip.cost", "0.006 " + En.Format("cost.unitPerSecond", "$")),
            texts.Row(entry, BoardReadouts.All with { Cost = false }, group.Live)!.TooltipLines);

        // 智力 off: no words, no intelligence tooltip lines.
        var off = texts.Row(entry, BoardReadouts.All with { Intelligence = false }, group.Live)!;
        Assert.Empty(off.Spoken);
        Assert.Single(off.TooltipLines);
    }

    [Fact]
    public void AnExcludedGroupShowsTheTagAndNamesItsSource()
    {
        var texts = new BoardTexts(En, TitleTheme.Vehicles);
        var root = Agent("r", 120) with { LiveCost = new LiveCost(6_000, 120) };
        var group = BoardGrouping.Group([root], SpeedMetric.Output, SourceSet.Empty)[0];
        Assert.True(group.Cost.Excluded);
        var row = texts.Row(new BoardEntry(BoardEntryKind.Group, "r", group, root, 1, 0, 38), BoardReadouts.All, 120)!;
        Assert.Contains(En.Format("boards.tooltip.subscription", "Claude Code"), row.TooltipLines);
    }

    [Fact]
    public void ABoardWhoseSourcesAreAllSubscriptionsShowsTheTag()
    {
        var display = new CostDisplay(En) { Settings = Settings.CostSettings.Default with { Subscriptions = SourceSet.Everything } };
        var texts = new BoardTexts(En, TitleTheme.Vehicles, display);
        var board = new BoardDefinition("b");
        var feed = new BoardLiveFeed(board);
        feed.Configure(board, SpeedMetric.Output, 2000, 0);
        var agent = Agent("a", 300) with { LiveCost = new LiveCost(9_000, 300) };
        var tick = feed.Update(BoardLiveFeedTests.Snapshot(0, agent), 0, texts);
        Assert.Equal(En["cost.subscription.tag"], tick.CostText);
        Assert.Equal(BoardMoneyState.Tag, tick.Gauge.Cost.State);
        Assert.Contains(En.Format("boards.tooltip.subscription", "Claude Code"), tick.Entries.First(e => e.Kind == BoardEntryKind.Group).Readout!.TooltipLines);

        // Including subscriptions counts them again.
        display.Settings = display.Settings with { IncludeSubscriptions = true };
        Assert.Equal("$12.34", feed.Update(BoardLiveFeedTests.Snapshot(1, agent), 1, texts).CostText);
    }

    // Rows kept with the readouts on (the team story as the prototype shows it: five groups).
    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.S, 3, true, 0)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 4, true, 0)]
    [InlineData(BoardStyle.Glass, BoardSize.L, 5, false, 0)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 4, true, 0)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 5, false, 1)]
    [InlineData(BoardStyle.Neon, BoardSize.M, 4, true, 0)]
    [InlineData(BoardStyle.Neon, BoardSize.S, 3, true, 0)]
    public void TheRowsKeptFollowTheTable(BoardStyle style, BoardSize size, int groups, bool more, int subs)
    {
        var feed = new BoardDemoFeed(BoardSimScenario.Team) { Localizer = Zh, Readouts = BoardReadouts.All };
        BoardTick tick = null!;
        for (var i = 0; i < 40; i++)
        {
            tick = feed.Step(style, size);
        }

        Assert.Equal(groups, tick.Entries.Count(e => e.Kind == BoardEntryKind.Group));
        Assert.Equal(more, tick.Entries.Any(e => e.Kind == BoardEntryKind.MoreGroups));
        Assert.Equal(subs, tick.Entries.Count(e => e.Kind == BoardEntryKind.Sub));
        // Group rows have their second line at M and L; S never draws it.
        Assert.All(tick.Entries.Where(e => e.Kind == BoardEntryKind.Group), e => Assert.Equal(size != BoardSize.S, e.TwoLine));
        Assert.All(tick.Entries.Where(e => e.Kind is BoardEntryKind.Group or BoardEntryKind.Sub), e => Assert.NotNull(e.Readout));
        var metrics = BoardMetrics.For(style, size, tick.Readouts);
        var last = tick.Entries[^1];
        Assert.True(last.Y + last.Height <= metrics.StartY + metrics.Room + 1e-6, "no row is cut");
    }

    [Fact]
    public void TheLiveFeedCarriesTheSwitchesAndTheThinkingHold()
    {
        var board = new BoardDefinition("b");
        var feed = new BoardLiveFeed(board);
        feed.Configure(board, SpeedMetric.Output, 2000, 0);
        var texts = new BoardTexts(En, TitleTheme.Vehicles, new CostDisplay(En));
        var thinking = Agent("a", 300) with { Intelligence = new AgentIntelligence(new IntelligenceSums(900, 900 * 110, 0, 900), null) };
        var snapshot = BoardLiveFeedTests.Snapshot(0, thinking) with
        {
            Sources = [.. BoardLiveFeedTests.Snapshot(0, thinking).Sources.Select(s => s with { Intelligence = s.Kind == SourceKind.ClaudeCode
                ? new IntelligenceSums(900, 900 * 110, 0, 900) : IntelligenceSums.Zero })],
        };
        var first = feed.Update(snapshot, 0, texts);
        Assert.Equal(BoardReadouts.All, first.Readouts);
        Assert.True(first.Gauge.ThinkingZero);
        Assert.False(first.ThinkingHidden);
        Assert.False(feed.Update(snapshot, 2.5, texts).ThinkingHidden);
        Assert.True(feed.Update(snapshot, 3, texts).ThinkingHidden);

        feed.Configure(board with { Readouts = BoardReadouts.None with { Thinking = true } }, SpeedMetric.Output, 2000, 0);
        var only = feed.Relayout(texts)!;
        Assert.False(only.ThinkingHidden);
        Assert.Equal(BoardReadouts.None with { Thinking = true }, only.Readouts);
    }

    private static BoardHeroReadouts Hero(string thinking, bool quiet, bool zero) =>
        new(quiet ? "–" : "120", quiet ? null : 120, thinking, quiet, zero, BoardHeroReadouts.Empty.Cost);

    private static BoardGaugePlacement Place(BoardStyle style, BoardSize size, BoardReadouts readouts, double glide, double band = 86) =>
        BoardGaugeLayout.Place(style, size, readouts, new BoardGaugeCells(18, 7, 36, 24), 300, band, glide, 1 - glide);
}
