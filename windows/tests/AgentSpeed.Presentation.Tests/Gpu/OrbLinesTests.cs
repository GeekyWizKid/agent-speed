using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Traffic;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb.Lines;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests.Gpu;

/// <summary>The orb's two-line readout (DESIGN 10, Floating orb readout): quiet rule, placement, centering and frames.</summary>
public class OrbLinesTests
{
    private const double Redline = 2000;
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly OrbReadoutStrings Strings = new("tok/s", "NEW PEAK",
        Enum.GetValues<SpeedTitle>().Select(t => t.ToString().ToUpperInvariant()).ToArray(), true);

    private static OrbGpuOrb Create(OrbStyle style, double size = 148, bool reduceMotion = false)
    {
        var effects = new EffectsPolicy(EffectsIntensity.Standard, reduceMotion, false);
        var orb = new OrbGpuOrb(3, new FakeGlyphs());
        orb.Configure(new OrbGpuConfig(style, size, effects, true, true, Redline, Strings, null, SourceSet.Empty),
            new OrbGpuContext(size, 2, OrbTokens.Dark, effects));
        orb.SetData(0, 0, SpeedTier.Idle, SpeedTitle.Idle, [], "today", null);
        return orb;
    }

    private static OrbLinesSample Traffic(double rx, double tx, bool bits = false) => OrbLinesText.Traffic(new TrafficRate(rx, tx), bits, En);

    private static double RenderFor(OrbGpuOrb orb, double from, double seconds)
    {
        var t = from;
        while (t < from + seconds)
        {
            t += 1 / 60.0;
            orb.Render(t);
        }

        return t;
    }

    [Fact]
    public void TheQuietRuleShowsLoudTrafficAndFadesTenSecondsAfterIt()
    {
        var rule = new OrbLinesQuietRule();
        Assert.False(rule.Apply(Traffic(400, 400, false), 0));
        Assert.Null(rule.Reading);

        Assert.True(rule.Apply(Traffic(1200, 0, false), 1));
        var shown = rule.Reading!.Value;
        Assert.Equal(1, shown.ShownAt);
        Assert.Equal(11, shown.QuietAt);
        Assert.Equal(0, shown.Alpha(1, false));
        Assert.Equal(1, shown.Alpha(1 + OrbLinesReading.FadeIn, false), 6);

        // Quiet samples keep the readout (with their values) until 10 s after the last loud one.
        Assert.True(rule.Apply(Traffic(10, 0, false), 5));
        Assert.Equal(11, rule.Reading!.Value.QuietAt);
        Assert.Equal("10", rule.Reading!.Value.Content.First.Number);
        Assert.Equal(1, rule.Reading!.Value.Alpha(10.9, false), 6);
        Assert.InRange(rule.Reading!.Value.Alpha(11.3, false), 0.49, 0.51);
        Assert.True(rule.Reading!.Value.IsGone(11.6));

        // Once gone, quiet samples change nothing (no frames); a loud one brings it back with a fade in.
        Assert.False(rule.Apply(Traffic(5, 5, false), 12));
        Assert.True(rule.Apply(Traffic(5000, 0, false), 13));
        Assert.Equal(13, rule.Reading!.Value.ShownAt);
    }

    [Fact]
    public void ReturningWhileFadingOutStartsFromTheCurrentOpacity()
    {
        var rule = new OrbLinesQuietRule();
        rule.Apply(Traffic(5000, 0, false), 0);
        rule.Apply(Traffic(5000, 0, false), 10.3);
        var reading = rule.Reading!.Value;
        Assert.Equal(0.5, reading.Alpha(10.3, false), 6);
        Assert.Equal(1, reading.Alpha(10.3 + 0.5 * OrbLinesReading.FadeIn, false), 6);
        Assert.Equal(20.3, reading.QuietAt, 6);
    }

    [Fact]
    public void ReduceMotionMakesTheFadesInstant()
    {
        var rule = new OrbLinesQuietRule();
        rule.Apply(Traffic(5000, 0, true), 0);
        var reading = rule.Reading!.Value;
        Assert.Equal(1, reading.Alpha(0, reduceMotion: true));
        Assert.Equal(0, reading.Alpha(10, reduceMotion: true));
        Assert.False(reading.IsFading(0.1, reduceMotion: true));
        Assert.Equal("Kb/s", reading.Content.First.Unit);
    }

    [Fact]
    public void IntelligenceIsLoudWhileGeneratingAndKeepsItsLastValueWhileQuiet()
    {
        var rule = new OrbLinesQuietRule();
        var active = new IntelligenceSums(Output: 400, Graded: 400 * 124, Thinking: 176, ThinkingBasis: 400);
        Assert.True(rule.Apply(OrbLinesText.Intelligence(active, generating: true, En), 1));
        var shown = rule.Reading!.Value.Content;
        Assert.Equal(OrbLinesKind.Intelligence, shown.Kind);
        Assert.Equal(new OrbLine(En["intel.glyph"], "133", string.Empty), shown.First);
        Assert.Equal(new OrbLine(En["intel.thinking.glyph"], "59", En["unit.tokS"]), shown.Second);
        Assert.Equal(En.Format("a11y.orb.intel", "133", "59"), shown.Spoken);
        Assert.StartsWith(En.Format("intel.tooltip", "133", "44%"), shown.Detail, StringComparison.Ordinal);

        // Generating before the first output in the window keeps the readout up with its last values.
        Assert.True(rule.Apply(OrbLinesText.Intelligence(IntelligenceSums.Zero, generating: true, En), 5));
        Assert.Equal(shown, rule.Reading!.Value.Content);
        Assert.Equal(15, rule.Reading!.Value.QuietAt);

        // Nothing at all: it fades 10 s after the last activity and a quiet sample of a gone readout changes nothing.
        Assert.False(rule.Apply(OrbLinesText.Intelligence(IntelligenceSums.Zero, generating: false, En), 6));
        Assert.True(rule.Reading!.Value.IsGone(15.6));
        Assert.False(rule.Apply(OrbLinesText.Intelligence(IntelligenceSums.Zero, generating: false, En), 16));
        Assert.False(new OrbLinesQuietRule().Apply(OrbLinesText.Intelligence(IntelligenceSums.Zero, generating: true, En), 0));
    }

    [Fact]
    public void SwitchingModesStartsTheOtherReadoutFromScratch()
    {
        var rule = new OrbLinesQuietRule();
        rule.Apply(Traffic(50_000, 0), 0);
        var intelligence = new IntelligenceSums(Output: 300, Graded: 300 * 92);
        Assert.True(rule.Apply(OrbLinesText.Intelligence(intelligence, generating: true, En), 1));
        Assert.Equal((OrbLinesKind.Intelligence, 1.0), (rule.Reading!.Value.Content.Kind, rule.Reading!.Value.ShownAt));
        Assert.True(rule.Apply(Traffic(0, 0), 2));
        Assert.Null(rule.Reading);
    }

    [Theory]
    [InlineData(OrbStyle.Led)]
    [InlineData(OrbStyle.Liquid)]
    [InlineData(OrbStyle.Reactor)]
    [InlineData(OrbStyle.Needle)]
    [InlineData(OrbStyle.Minimal)]
    public void TheIntelligenceReadoutUsesTheTrafficTableSoSwitchingMovesNothing(OrbStyle style)
    {
        var traffic = Create(style, size: 196);
        traffic.SetLines(0, Traffic(1_280_000, 64_000), true);
        var t = RenderFor(traffic, 0, 1);
        var intelligence = Create(style, size: 196);
        intelligence.SetLines(0, OrbLinesText.Intelligence(new IntelligenceSums(400, 400 * 124, 176, 400), true, En), true);
        RenderFor(intelligence, 0, 1);

        Assert.True(intelligence.LinesVisible(t));
        Assert.Equal(traffic.ReadoutShift, intelligence.ReadoutShift, 6);
        Assert.Equal(traffic.UnitOnDial, intelligence.UnitOnDial);
        var placement = OrbLinesPlacement.For(style);
        var size = placement.TextSize(196);
        var trafficContent = traffic.LinesReading!.Value.Content;
        var intelligenceContent = intelligence.LinesReading!.Value.Content;

        // Same number and unit cells; only the glyph cell widens to a text glyph's own advance.
        Assert.Equal(OrbLinesPlacement.ArrowWidth * size, traffic.Lines.GlyphCell(size, trafficContent), 6);
        Assert.True(intelligence.Lines.GlyphCell(size, intelligenceContent) >= OrbLinesPlacement.ArrowWidth * size);
        Assert.Equal(
            traffic.Lines.LineWidth(size, trafficContent) - traffic.Lines.GlyphCell(size, trafficContent),
            intelligence.Lines.LineWidth(size, intelligenceContent) - intelligence.Lines.GlyphCell(size, intelligenceContent), 6);

        // The intelligence line is the one nearest the value: ↑'s place above it, ↓'s below it (needle).
        var (top, bottom) = intelligenceContent.Stack(placement.Below);
        Assert.Equal(placement.Below ? intelligenceContent.First : intelligenceContent.Second, top);
        Assert.Equal(placement.Below ? intelligenceContent.Second : intelligenceContent.First, bottom);
    }

    [Theory]
    [InlineData(OrbStyle.Led, 0.0950)]
    [InlineData(OrbStyle.Liquid, 0.0950)]
    [InlineData(OrbStyle.Minimal, 0.09854)]
    [InlineData(OrbStyle.Reactor, 0)]
    [InlineData(OrbStyle.Needle, 0)]
    public void TheStackShiftCentersBlockAndValue(OrbStyle style, double expected)
    {
        var placement = OrbLinesPlacement.For(style);
        Assert.Equal(expected, placement.StackShift(OrbReadoutLayout.For(style), 148) / 148, 3);
        Assert.Equal(0, placement.StackShift(OrbReadoutLayout.For(style), 148, 0));
        Assert.Equal(placement.StackShift(OrbReadoutLayout.For(style), 148), placement.StackShift(OrbReadoutLayout.For(style), 148, 1), 9);
    }

    [Fact]
    public void SmallOrbsStopTheShiftClearOfTheAgentDots()
    {
        // At 96 pt the 7 pt floor makes the block relatively tall: the title would come within 0.05 S of the dots.
        var layout = OrbReadoutLayout.Led;
        var shift = OrbLinesPlacement.Led.StackShift(layout, 96);
        Assert.Equal(0.0929, shift / 96, 3);
        var titleBottom = (layout.TitleBaseline * 96 + shift) + 0.22 * OrbReadoutLayout.TextSize(layout.TitleSize, 96);
        var dotsTop = layout.DotsY!.Value * 96 - OrbLinesPlacement.DotDiameter(96) / 2;
        Assert.Equal(0.05 * 96, dotsTop - titleBottom, 6);
    }

    [Fact]
    public void LinesStackDownloadOverUpload()
    {
        var (down, up) = OrbLinesPlacement.Led.Baselines(100, 148);
        Assert.Equal(100 - 0.245 * 148, up, 6);
        Assert.Equal(up - 1.32 * 7.4, down, 6);
        var (needleDown, needleUp) = OrbLinesPlacement.Needle.Baselines(100, 148);
        Assert.Equal(100 + 0.108 * 148, needleDown, 6);
        Assert.True(needleUp > needleDown);
    }

    [Fact]
    public void TheReadoutDrawsCenteredAboveTheValueAndMovesTheStackDown()
    {
        var orb = Create(OrbStyle.Led);
        orb.Render(0.1);
        var valueBefore = orb.List.Texts.ToArray().Max(t => t.Rect.W);
        var restingY = orb.List.Texts.ToArray().Select(t => t.Rect.Y + t.Rect.W).ToArray();

        Assert.True(orb.SetLines(0.1, Traffic(1_280_000, 64_000, false), true));
        var t = RenderFor(orb, 0.1, 1);
        var list = orb.List;
        Assert.True(orb.LinesVisible(t));
        // Two lines of arrow, number characters and unit: at least 2 × (1 + 4 + 1) quads beyond the readout's.
        Assert.True(list.TextCount >= restingY.Length + 12);
        Assert.Equal(0.0950 * 148, orb.ReadoutShift, 1);
        var block = list.Texts.ToArray().Where(q => q.Rect.Y + q.Rect.W < 0.75 * 148 - 0.10 * 148).ToArray();
        Assert.NotEmpty(block);
        var left = block.Min(q => q.Rect.X);
        var right = block.Max(q => q.Rect.X + q.Rect.Z);
        Assert.InRange((left + right) / 2, 0.75 * 148 - 6, 0.75 * 148 + 6);
        Assert.True(valueBefore > 0);
    }

    [Fact]
    public void FadesAskForThirtyFramesAQuietOrbForNone()
    {
        var orb = Create(OrbStyle.Minimal);
        RenderFor(orb, 0, 0.5);
        Assert.Equal(FrameClass.None, orb.Demand);

        orb.SetLines(0.5, Traffic(5000, 5000, false), true);
        orb.Render(0.55);
        Assert.True(orb.Demand >= FrameClass.Fps30);
        var t = RenderFor(orb, 0.55, 1);
        Assert.Equal(FrameClass.None, orb.Demand);

        // 10 s after the last loud sample it fades out at 30 fps, then draws nothing.
        orb.SetLines(t, Traffic(0, 0, false), true);
        orb.Render(10.6);
        Assert.True(orb.Demand >= FrameClass.Fps30);
        orb.Render(11.5);
        orb.Render(11.6);
        Assert.Equal(FrameClass.None, orb.Demand);
        Assert.False(orb.LinesVisible(11.6));
        Assert.Equal(0, orb.ReadoutShift, 6);
    }

    [Fact]
    public void ThumbnailsAndUnavailableOrbsDrawNoReadout()
    {
        var small = Create(OrbStyle.Led, size: 60);
        small.SetLines(0, Traffic(50_000, 0, false), true);
        RenderFor(small, 0, 0.5);
        Assert.False(small.LinesVisible(0.5));

        var unavailable = Create(OrbStyle.Led);
        unavailable.SetData(0, 0, SpeedTier.Idle, SpeedTitle.Idle, [], string.Empty, "Not found");
        unavailable.SetLines(0, Traffic(50_000, 0, false), true);
        RenderFor(unavailable, 0, 0.5);
        Assert.False(unavailable.LinesVisible(0.5));
    }

    [Fact]
    public void AReadoutThatDoesNotFitIsNotDrawn()
    {
        // Needle needs S ≈ 137: below that neither the readout nor the dial legend appears.
        var orb = Create(OrbStyle.Needle, size: 112);
        orb.SetLines(0, Traffic(50_000, 0, false), true);
        RenderFor(orb, 0, 0.5);
        Assert.False(orb.LinesVisible(0.5));
        Assert.False(orb.UnitOnDial);
    }

    [Fact]
    public void TheNeedlePrintsItsUnitOnTheDialUnderTheNeedle()
    {
        var orb = Create(OrbStyle.Needle, size: 196);
        orb.SetLines(0, null, true);
        RenderFor(orb, 0, 0.3);
        Assert.True(orb.UnitOnDial);
        Assert.True(orb.List.OverlayTextStart >= 1);
        Assert.Contains(orb.List.Batches.ToArray(), b => b.Pass == OrbPass.Text);

        orb.SetLines(0.3, null, false);
        RenderFor(orb, 0.3, 0.3);
        Assert.False(orb.UnitOnDial);
        Assert.Equal(0, orb.List.OverlayTextStart);
    }
}
