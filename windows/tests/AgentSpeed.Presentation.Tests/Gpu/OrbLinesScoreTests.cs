using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.Orb.Gpu.Styles;
using AgentSpeed.Presentation.Orb.Lines;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Presentation.Tests.TestSupport;
using CoreTasks = AgentSpeed.Core.Tasks;

namespace AgentSpeed.Presentation.Tests.Gpu;

/// <summary>
/// 智力 on the orb's intelligence readout (DESIGN 10, Intelligence readout; DESIGN 12): the set's value and words, the revised
/// quiet rule, the forms' colors, the proof mark and the score's motion.
/// </summary>
public partial class OrbLinesTests
{
    /// <summary>176 tok/s of thinking in the window.</summary>
    private static readonly IntelligenceSums Thinking176 = new(1200, 528, 1200);

    private static TaskSetReading Score(int? shown, TaskForm form = TaskForm.Live, TaskState state = TaskState.Passing, int count = 1) =>
        shown is null ? TaskSetReading.None : new TaskSetReading(form, shown, state, count, null);

    private static OrbLinesSample Scored(TaskSetReading score, bool generating = true, IntelligenceSums? sums = null) =>
        OrbLinesText.Intelligence(score, sums ?? Thinking176, generating, ZhHant);

    private static AgentStatus ScoredAgent(string id, string project, CoreTasks.TaskValueForm form, double score, CoreTasks.TaskState state,
        double thinking = 0)
    {
        var set = CoreTasks.TaskSetValue.Of(id, 0, new CoreTasks.TaskValue(form, score, CoreTasks.TaskRounding.HalfAwayFromZero(score), state));
        var sums = new IntelligenceSums(thinking > 0 ? 3 * thinking : 0, 3 * thinking, thinking > 0 ? 3 * thinking : 0) { Score = set };
        return new AgentStatus(id, SourceKind.ClaudeCode, project, "model",
            form == CoreTasks.TaskValueForm.Live ? AgentPhase.Generating : AgentPhase.Idle, false, Rate.Zero, null, DateTimeOffset.UnixEpoch)
        {
            Intelligence = new AgentIntelligence(sums, set, state),
        };
    }

    private static (TaskSetReading Score, IntelligenceSums Sums) Set(params AgentStatus[] agents)
    {
        var sums = agents.Aggregate(IntelligenceSums.Zero, (sum, agent) => sum + agent.Intelligence.Sums);
        return (TaskSetReading.Of(sums, agents), sums);
    }

    private static OrbGpuOrb Showing(OrbStyle style, OrbLinesSample sample, double size = 196, bool reduceMotion = false, bool highContrast = false)
    {
        var effects = new EffectsPolicy(EffectsIntensity.Standard, reduceMotion, highContrast);
        var orb = new OrbGpuOrb(3, new FakeGlyphs());
        orb.Configure(new OrbGpuConfig(style, size, effects, true, true, Redline, Strings, null, SourceSet.Empty),
            new OrbGpuContext(size, 2, OrbTokens.Dark, effects, highContrast ? OrbContrastColors.Default : null));
        orb.SetData(0, 0, SpeedTier.Idle, SpeedTitle.Idle, [], "today", null);
        orb.SetLines(0, sample, true);
        RenderFor(orb, 0, 1);
        return orb;
    }

    /// <summary>The readout's own shapes and quads, drawn alone into a fresh list at the orb's current time.</summary>
    private static OrbDrawList LinesOnly(OrbGpuOrb orb, OrbStyle style, double size = 196, bool highContrast = false)
    {
        var effects = new EffectsPolicy(EffectsIntensity.Standard, false, highContrast);
        var ctx = new OrbGpuContext(size, 2, OrbTokens.Dark, effects, highContrast ? OrbContrastColors.Default : null);
        var list = new OrbDrawList();
        orb.Lines.Add(list, ctx, orb.Scene, orb.LinesReading!.Value, OrbLinesPlacement.For(style), 0, OrbLinesColors.Standard(ctx),
            OrbTextClip.None, yieldsToPill: false);
        return list;
    }

    private static OrbGpuInstance[] Dots(OrbDrawList list) => [.. list.Instances.ToArray().Where(i => i.Shape == OrbShape.Circle)];

    /// <summary>The intelligence number's quads (size p, the larger ones on the top line).</summary>
    private static OrbTextInstance[] Digits(OrbDrawList list, double p) =>
        [.. list.Texts.ToArray().Where(q => Math.Abs(q.Rect.W - 4 - Math.Round(p * 4) / 4) < 0.01).OrderBy(q => q.Rect.X)];

    [Fact]
    public void TheReadoutShowsTheHighestScoreOfTheOrbsAgentsAndNamesItsSupplier()
    {
        var (score, sums) = Set(
            ScoredAgent("a1", "nova", CoreTasks.TaskValueForm.Live, 86.2, CoreTasks.TaskState.Passing, thinking: 128),
            ScoredAgent("a2", "rocket", CoreTasks.TaskValueForm.Live, 41, CoreTasks.TaskState.Failing),
            ScoredAgent("a3", "atlas", CoreTasks.TaskValueForm.Resting, 95, CoreTasks.TaskState.Passing));
        Assert.Equal((TaskForm.Live, 86, 2), (score.Form, score.Shown, score.Count));

        var content = OrbLinesText.Intelligence(score, sums, generating: true, ZhHant).Content!;
        Assert.Equal(new OrbLine("智力", "86", string.Empty), content.First);
        Assert.Equal(new OrbLineScore(86, false, false), content.Score);
        Assert.Equal("智力 86，2 個 agent 中最高，思考每秒 128 個 token", content.Spoken);
        Assert.Equal("智力 86，2 個 agent 中最高", content.SpokenAlone);

        // The Windows tooltip: task.tooltip.top with the supplying agent's project, then the disclaimer.
        Assert.Equal("智力 86 · 最高：nova\n任務進展的估算，不是模型能力測驗", content.Detail);
    }

    [Fact]
    public void UnprovenAndRestingScoresSayItAndCarryTheirForm()
    {
        var unverified = Scored(Score(9, state: TaskState.Unverified, count: 2)).Content!;
        Assert.Equal(new OrbLineScore(9, false, true), unverified.Score);
        Assert.Equal("智力 9，尚未驗證，2 個 agent 中最高，思考每秒 176 個 token", unverified.Spoken);

        var pending = Scored(Score(34, state: TaskState.Pending)).Content!;
        Assert.True(pending.Score.Unproven);
        Assert.Equal("智力 34，待重新驗證，思考每秒 176 個 token", pending.Spoken);

        var resting = Scored(Score(72, TaskForm.Resting, count: 2), generating: false, sums: IntelligenceSums.Zero).Content!;
        Assert.Equal(new OrbLineScore(72, true, false), resting.Score);
        Assert.True(resting.ThinkingZero);
        Assert.Equal("智力 72，閒置中，2 個 agent 中最高", resting.SpokenAlone);
    }

    [Fact]
    public void NoScoreShowsADashWhileItsAgentsGenerateAndNothingOtherwise()
    {
        var reading = Scored(TaskSetReading.None);
        Assert.True(reading.Loud);
        var content = reading.Content!;
        Assert.Equal("–", content.First.Number);
        Assert.Equal(OrbLineScore.None, content.Score);
        Assert.Equal("智力：尚無評分，思考每秒 176 個 token", content.Spoken);
        Assert.Equal("智力：尚無評分", content.SpokenAlone);
        Assert.Equal("任務進展的估算，不是模型能力測驗", content.Detail);

        var quiet = Scored(TaskSetReading.None, generating: false);
        Assert.False(quiet.Loud);
        Assert.Null(quiet.Content);
    }

    [Fact]
    public void ARestingScoreKeepsTheReadoutUpWithoutAnyGeneration()
    {
        var rule = new OrbLinesQuietRule();
        Assert.True(rule.Apply(Scored(Score(72, TaskForm.Resting), generating: false, IntelligenceSums.Zero), 0));
        foreach (var time in new[] { 30.0, 300, 590 })
        {
            rule.Apply(Scored(Score(72, TaskForm.Resting), generating: false, IntelligenceSums.Zero), time);
            Assert.Equal(time + OrbLinesQuietRule.QuietAfter, rule.Reading!.Value.QuietAt);
        }

        // A set with nothing to show and no agent generating never brings a readout up.
        Assert.False(new OrbLinesQuietRule().Apply(Scored(TaskSetReading.None, generating: false), 0));
    }

    [Fact]
    public void AfterItsAgentsLeaveTheOrbKeepsItsLastScoreWhileItFades()
    {
        var rule = new OrbLinesQuietRule();
        rule.Apply(Scored(Score(72, TaskForm.Resting, count: 2), generating: false, IntelligenceSums.Zero), 0);
        var last = rule.Reading!.Value;

        // The agents leave the list: the set has no score and nobody generates. The pale 72 stays, unchanged, for the 10 s
        // and the 0.6 s fade; then the readout is gone and further quiet samples change nothing.
        Assert.False(rule.Apply(Scored(TaskSetReading.None, generating: false), 1));
        Assert.Equal(last, rule.Reading!.Value);
        Assert.Equal("72", rule.Reading!.Value.Content.First.Number);
        Assert.Equal(new OrbLineScore(72, true, false), rule.Reading!.Value.Content.Score);
        Assert.Equal(0.5, rule.Reading!.Value.Alpha(10.3, false), 6);
        Assert.True(rule.Reading!.Value.IsGone(10.6));
        Assert.False(rule.Apply(Scored(TaskSetReading.None, generating: false), 11));
    }

    [Fact]
    public void ARiseOfThreeOrMoreCountsUpWithEaseOutCubic()
    {
        var motion = OrbScoreMotion.Start(new OrbLineScore(40, false, false)).Next(new OrbLineScore(80, false, false), 10);
        var duration = TaskScoreMotion.CountUpDuration(40);
        Assert.Equal(0.78, duration, 9);

        Assert.Equal(40, motion.Frame(10, false).Shown);
        var mid = motion.Frame(10 + duration / 2, false);
        Assert.Equal((int)Math.Round(40 + 40 * (1 - Math.Pow(0.5, 3)), MidpointRounding.AwayFromZero), mid.Shown);
        Assert.Equal((1.0, 0.0), (mid.Alpha, mid.OutgoingAlpha));
        Assert.Equal(80, motion.Frame(10 + duration, false).Shown);

        // Frames at 30 fps only for its duration.
        Assert.True(motion.IsMoving(10 + duration - 0.01, false));
        Assert.False(motion.IsMoving(10 + duration + 1e-9, false));
    }

    [Fact]
    public void SmallRisesReplaceTheDigitsAndFallsAndDashesCrossfade()
    {
        var start = OrbScoreMotion.Start(new OrbLineScore(40, false, false));
        var small = start.Next(new OrbLineScore(42, false, false), 5);
        Assert.Equal(42, small.Frame(5, false).Shown);
        Assert.False(small.IsMoving(5, false));

        // A fall: the old number fades out over the new one in 0.2 s, never rolling down.
        var fall = start.Next(new OrbLineScore(31, false, false), 5);
        var half = fall.Frame(5.1, false);
        Assert.Equal((31, 40), (half.Shown, half.Outgoing));
        Assert.InRange(half.Alpha, 0.01, 0.99);
        Assert.Equal(1, half.Alpha + half.OutgoingAlpha, 9);
        Assert.True(fall.IsMoving(5.19, false));
        Assert.Equal((1.0, 0.0), (fall.Frame(5.2, false).Alpha, fall.Frame(5.2, false).OutgoingAlpha));
        Assert.False(fall.IsMoving(5.2, false));

        // `–` ↔ a number crossfades too.
        var none = start.Next(OrbLineScore.None, 5);
        Assert.Equal(((int?)null, 40), (none.Frame(5.05, false).Shown, none.Frame(5.05, false).Outgoing));
        Assert.Equal(TaskScoreText.Dash, OrbScoreMotion.Text(null));
        Assert.Same(OrbScoreMotion.Text(86), OrbScoreMotion.Text(86));
    }

    [Fact]
    public void ACountUpInterruptedByANewValueStartsFromTheIntegerItReached()
    {
        var climbing = OrbScoreMotion.Start(new OrbLineScore(10, false, false)).Next(new OrbLineScore(90, false, false), 0);
        var reached = climbing.Frame(0.2, false).Shown!.Value;
        Assert.InRange(reached, 11, 89);

        var retarget = climbing.Next(new OrbLineScore(95, false, false), 0.2);
        Assert.Equal(reached, retarget.Frame(0.2, false).Shown);
        Assert.Equal(95, retarget.Frame(0.2 + TaskScoreMotion.CountUpDuration(95 - reached), false).Shown);

        // A fall mid-climb fades out the integer on display.
        var dropped = climbing.Next(new OrbLineScore(50, false, false), 0.2);
        var frame = dropped.Frame(0.25, false);
        Assert.Equal((50, reached), (frame.Shown, frame.Outgoing));
    }

    [Fact]
    public void ReduceMotionCrossfadesEveryChangeAndKeepsTheColorFade()
    {
        var start = OrbScoreMotion.Start(new OrbLineScore(40, false, true));
        var rise = start.Next(new OrbLineScore(80, true, false), 3);
        var frame = rise.Frame(3 + TaskScoreMotion.ReducedSeconds / 2, true);
        Assert.Equal((80, 40), (frame.Shown, frame.Outgoing));
        Assert.True(rise.IsMoving(3.1, true));
        Assert.Equal(80, rise.Frame(3 + TaskScoreMotion.ReducedSeconds, true).Shown);

        // Live → resting still crossfades the color in 0.2 s; the proof mark goes at once.
        Assert.Equal(0.5, rise.Frame(3.1, true).Pale, 9);
        Assert.Equal(0, rise.Frame(3, true).Proof);
        Assert.True(rise.IsMoving(3.19, true));
        Assert.False(rise.IsMoving(3.2, true));
    }

    [Fact]
    public void TheColorAndTheProofMarkReverseFromWhereTheyStand()
    {
        var start = OrbScoreMotion.Start(new OrbLineScore(60, false, false));
        var resting = start.Next(new OrbLineScore(60, true, true), 0);
        Assert.Equal(0.5, resting.Frame(0.1, false).Pale, 9);
        Assert.Equal(0.4, resting.Frame(0.1, false).Proof, 9);

        var live = resting.Next(new OrbLineScore(60, false, false), 0.1);
        Assert.Equal(0.5, live.Frame(0.1, false).Pale, 9);
        Assert.Equal(0.4, live.Frame(0.1, false).Proof, 9);
        Assert.Equal((0.0, 0.0), (live.Frame(0.2, false).Pale, live.Frame(0.2, false).Proof));
        Assert.False(live.IsMoving(0.2, false));
    }

    [Fact]
    public void LiveAndRestingTakeTheNumberAndTheUnitTokens()
    {
        var p = OrbLinesPlacement.IntelligenceScale * OrbLinesPlacement.Led.TextSize(196);
        var orb = Showing(OrbStyle.Led, Scored(Score(86)));
        Assert.All(Digits(LinesOnly(orb, OrbStyle.Led), p), q => Assert.Equal(0.78, q.Color.W, 3));

        // Resting: the unit token (white 50 %), reached over a 0.2 s crossfade of the color.
        orb.SetLines(1, Scored(Score(86, TaskForm.Resting), generating: false, IntelligenceSums.Zero), true);
        orb.Render(1.1);
        Assert.All(Digits(LinesOnly(orb, OrbStyle.Led), p), q => Assert.Equal(0.64, q.Color.W, 3));
        Assert.Equal(FrameClass.Fps30, orb.Demand);
        RenderFor(orb, 1.1, 0.5);
        Assert.All(Digits(LinesOnly(orb, OrbStyle.Led), p), q => Assert.Equal(0.50, q.Color.W, 3));
        Assert.Equal(FrameClass.None, orb.Demand);

        // `–` takes the unit token as well.
        var none = Showing(OrbStyle.Led, Scored(TaskSetReading.None));
        Assert.All(Digits(LinesOnly(none, OrbStyle.Led), p), q => Assert.Equal(0.50, q.Color.W, 3));
    }

    [Theory]
    [InlineData(OrbStyle.Led)]
    [InlineData(OrbStyle.Liquid)]
    [InlineData(OrbStyle.Reactor)]
    [InlineData(OrbStyle.Needle)]
    [InlineData(OrbStyle.Minimal)]
    public void AnUnprovenScoreSitsOnDotsThatTakeNoWidth(OrbStyle style)
    {
        var proven = Showing(style, Scored(Score(9)));
        var orb = Showing(style, Scored(Score(9, state: TaskState.Unverified)));
        Assert.Empty(Dots(LinesOnly(proven, style)));
        Assert.Equal(Drawn(proven, style, 196).Select(l => l.Left), Drawn(orb, style, 196).Select(l => l.Left));

        var p = OrbLinesPlacement.IntelligenceScale * OrbLinesPlacement.For(style).TextSize(196);
        var list = LinesOnly(orb, style);
        var digits = Digits(list, p);
        var left = digits[0].Rect.X + 2;
        var width = 0.56 * p;
        var baseline = Drawn(orb, style, 196)[0].Baseline;
        var mark = TaskProofMark.Underline(p, width, 2);
        var dots = Dots(list);
        Assert.Equal(mark.Centers.Count, dots.Length);
        Assert.All(dots, dot =>
        {
            Assert.Equal(mark.Diameter / 2, dot.B.X, 4);
            Assert.InRange(dot.A.Y, baseline + 0.22 * p - 0.25 - 1e-4, baseline + 0.22 * p + 0.25 + 1e-4);
            Assert.Equal(Math.Round((dot.A.Y - dot.B.X) * 2), (dot.A.Y - dot.B.X) * 2, 3);
            Assert.Equal(Math.Round((dot.A.X - dot.B.X) * 2), (dot.A.X - dot.B.X) * 2, 3);
            Assert.Equal(0.78 * TaskProofMark.Opacity, dot.Color.W, 3);
        });

        // From the number's left edge to its right edge, the dots spread evenly.
        Assert.Equal(left, dots[0].A.X - dots[0].B.X, 3);
        Assert.InRange(dots[^1].A.X + dots[^1].B.X, left + width - 0.5, left + width + 0.5);
    }

    [Fact]
    public void HighContrastDrawsTheDotsInTheTextColorAtFullStrengthAndThicker()
    {
        var orb = Showing(OrbStyle.Led, Scored(Score(34, state: TaskState.Pending)), highContrast: true);
        var p = OrbLinesPlacement.IntelligenceScale * OrbLinesPlacement.Led.TextSize(196);
        var dots = Dots(LinesOnly(orb, OrbStyle.Led, highContrast: true));
        Assert.NotEmpty(dots);
        Assert.All(dots, dot =>
        {
            Assert.Equal(TaskProofMark.Diameter(p, 2, highContrast: true) / 2, dot.B.X, 4);
            Assert.Equal((1f, 1f, 1f, 1f), (dot.Color.X, dot.Color.Y, dot.Color.Z, dot.Color.W));
        });
    }

    [Fact]
    public void TheProofMarkFadesWithTheStateAndAtOnceUnderReduceMotion()
    {
        var orb = Showing(OrbStyle.Minimal, Scored(Score(60)));
        orb.SetLines(1, Scored(Score(60, state: TaskState.Pending)), true);
        orb.Render(1.125);
        Assert.Equal(0.78 * 0.7 * 0.5, Dots(LinesOnly(orb, OrbStyle.Minimal))[0].Color.W, 3);
        Assert.Equal(FrameClass.Fps30, orb.Demand);
        RenderFor(orb, 1.125, 0.5);
        Assert.Equal(FrameClass.None, orb.Demand);

        var reduced = Showing(OrbStyle.Minimal, Scored(Score(60)), reduceMotion: true);
        reduced.SetLines(1, Scored(Score(60, state: TaskState.Pending)), true);
        reduced.Render(1.01);
        Assert.Equal(0.78 * 0.7, Dots(LinesOnly(reduced, OrbStyle.Minimal))[0].Color.W, 3);
        Assert.Equal(FrameClass.None, reduced.Demand);
    }

    [Fact]
    public void ACountUpAsksForThirtyFramesForItsDurationAndAStillScoreForNone()
    {
        var orb = Showing(OrbStyle.Minimal, Scored(Score(20, state: TaskState.Unverified)));
        Assert.Equal(FrameClass.None, orb.Demand);
        var still = orb.List.Fingerprint();
        orb.Render(1.5);
        Assert.Equal(still, orb.List.Fingerprint());

        Assert.True(orb.SetLines(2, Scored(Score(60, state: TaskState.Unverified)), true));
        orb.Render(2.02);
        Assert.Equal(FrameClass.Fps30, orb.Demand);
        var duration = TaskScoreMotion.CountUpDuration(40);
        orb.Render(2 + duration - 0.02);
        Assert.Equal(FrameClass.Fps30, orb.Demand);
        orb.Render(2 + duration + 0.01);
        Assert.Equal(FrameClass.None, orb.Demand);
        Assert.Equal(60, orb.LinesReading!.Value.Score.Frame(2 + duration, false).Shown);
    }

    [Fact]
    public void AWarmCountUpFrameWithItsProofMarkAllocatesNothing()
    {
        var orb = Showing(OrbStyle.Led, Scored(Score(50, state: TaskState.Unverified)), size: 240);
        orb.SetLines(1, Scored(Score(57, state: TaskState.Unverified)), true);
        var time = 1.0;
        orb.Render(time += 1 / 60.0);
        orb.Render(time += 1 / 60.0);
        AllocationProbe.AssertNone(() => orb.Render(time += 1 / 120.0));
    }

    [Fact]
    public void TheLiquidSplitsTheDotsAtItsSurfaceAndDrawsEachOnce()
    {
        var orb = Showing(OrbStyle.Liquid, Scored(Score(88, state: TaskState.Unverified)));
        orb.SetData(1, Redline * 1.1, HeatMath.RawTier(Redline * 1.1, Redline), SpeedTitles.Raw(Redline * 1.1, Redline),
            [SourceKind.Codex], "today", null);
        var time = RenderFor(orb, 1, 3);
        Assert.True(orb.List.HasLiquid);

        // The CPU surface is the shader's, which the model drives.
        var model = (LiquidModel)((LiquidDraw)orb.Style!).Model;
        var u = orb.List.Liquid;
        for (var i = 0; i <= 8; i++)
        {
            var x = -0.4 + 0.1 * i;
            var expected = u.Frame.Y + model.SurfaceY(x) * 196;
            Assert.InRange(u.SurfaceY(u.Frame.X + x * 196), expected - 0.02, expected + 0.02);
        }

        var ctx = orb.Context!;
        var reading = orb.LinesReading!.Value;
        var above = new OrbDrawList();
        var below = new OrbDrawList();
        foreach (var (list, isBelow) in new[] { (above, false), (below, true) })
        {
            list.Liquid = orb.List.Liquid;
            list.AddLiquid(0);
            orb.Lines.Add(list, ctx, orb.Scene, reading, OrbLinesPlacement.Liquid, 0, OrbLinesColors.Standard(ctx), OrbTextClip.Liquid(isBelow),
                yieldsToPill: false);
        }

        var p = OrbLinesPlacement.IntelligenceScale * OrbLinesPlacement.Liquid.TextSize(196);
        var all = Dots(above).Concat(Dots(below)).ToArray();
        Assert.Equal(TaskProofMark.Underline(p, 2 * 0.56 * p, 2).Centers.Count, all.Length);
        Assert.All(Dots(above), dot => Assert.True(dot.A.Y <= u.SurfaceY(dot.A.X)));
        Assert.All(Dots(below), dot => Assert.True(dot.A.Y > u.SurfaceY(dot.A.X)));
        Assert.True(time > 3);
    }
}
