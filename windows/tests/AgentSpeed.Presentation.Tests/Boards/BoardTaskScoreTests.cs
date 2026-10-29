using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Live;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Presentation.Titles;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;
using CoreState = AgentSpeed.Core.Tasks.TaskState;
using CoreTasks = AgentSpeed.Core.Tasks;
using KeyedLayers = AgentSpeed.Presentation.Tests.Boards.BoardGaugeSceneTests.KeyedLayers;
using TaskState = AgentSpeed.Presentation.TaskScore.TaskState;

namespace AgentSpeed.Presentation.Tests.Boards;

/// <summary>The task score on the boards (DESIGN 12; 2c, Live metrics): the hero's motion and proof mark, the rows, the cards.</summary>
public class BoardTaskScoreTests
{
    private static readonly Localizer Zh = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);

    private static TaskSetReading Live(int shown, TaskState state = TaskState.Passing, int count = 1) => new(TaskForm.Live, shown, state, count, null);

    private static TaskSetReading Resting(int shown, TaskState state = TaskState.Passing, int count = 1) =>
        new(TaskForm.Resting, shown, state, count, null);

    // ---- The hero's motion (DESIGN 12, Motion) ----

    [Fact]
    public void AClimbOfThreeOrMoreCountsUpWithTheArcAndTheMeterFollowing()
    {
        var score = new BoardHeroScore();
        score.Apply(Live(40), 0, animate: false, reduceMotion: false);
        score.Apply(Live(80), 10, animate: true, reduceMotion: false);
        Assert.Equal(TaskScoreChangeKind.CountUp, score.Change.Kind);
        // A test pass of +40 lands in 0.78 s.
        Assert.Equal(0.78, score.Change.Duration, 9);
        var last = 40;
        for (var t = 10.0; t < 10.78; t += 1 / 60.0)
        {
            var shown = score.Shown(t)!.Value;
            Assert.InRange(shown, last, 80);
            last = shown;
            var sweep = score.Sweep(t);
            Assert.Equal(sweep.Settled, sweep.Changing, 9);
            Assert.Equal(shown / 100.0, sweep.First.At, 2);
            Assert.Equal(BoardHeroScore.Lit(shown), score.Meter(t).From);
            Assert.True(score.Animating(t));
        }

        // Quick at first, settling into the final number.
        Assert.True(score.Shown(10.2) > 60);
        Assert.Equal(80, score.Shown(10.78));
        Assert.False(score.Animating(10.79));
        Assert.Equal(0.8, score.Sweep(11).First.At, 9);
        Assert.Equal(10, BoardHeroScore.Lit(80));
        // A count-up composes its digits from cached strings.
        Assert.Same(score.Text(10.78), score.Text(11));
    }

    [Fact]
    public void ARiseOfOneOrTwoReplacesTheDigitsAtOnce()
    {
        var score = new BoardHeroScore();
        score.Apply(Live(40), 0, false, false);
        score.Apply(Live(42), 1, true, false);
        Assert.Equal(TaskScoreChangeKind.Replace, score.Change.Kind);
        Assert.False(score.Animating(1));
        Assert.Equal("42", score.Text(1));
        Assert.Null(score.Outgoing(1));
    }

    [Fact]
    public void AFallCrossfadesInPlaceAndNeverRollsDown()
    {
        var score = new BoardHeroScore();
        score.Apply(Live(80), 0, false, false);
        score.Apply(Live(60), 1, true, false);
        Assert.Equal((TaskScoreChangeKind.Crossfade, 0.2), (score.Change.Kind, score.Change.Duration));
        var mid = score.Fade(1.1);
        Assert.InRange(mid.Outgoing, 0.01, 0.99);
        Assert.Equal(1, mid.Outgoing + mid.Incoming, 9);
        // The number is the new one at once; the old one only fades out.
        Assert.Equal(("60", "80"), (score.Text(1.1), score.Outgoing(1.1)));
        var sweep = score.Sweep(1.1);
        Assert.Equal((0.6, 0.8), (sweep.Settled, sweep.Changing));
        Assert.Equal(mid.Outgoing, sweep.ChangingAlpha, 9);
        Assert.Equal((0.8, mid.Outgoing), (sweep.First.At, sweep.First.Alpha));
        Assert.Equal((0.6, mid.Incoming), (sweep.Second.At, sweep.Second.Alpha));
        var meter = score.Meter(1.1);
        Assert.Equal((BoardHeroScore.Lit(80), BoardHeroScore.Lit(60)), (meter.From, meter.To));
        Assert.False(score.Animating(1.21));
        Assert.Null(score.Outgoing(1.21));
    }

    [Fact]
    public void ADashAndANumberCrossfade()
    {
        var score = new BoardHeroScore();
        score.Apply(TaskSetReading.None, 0, false, false);
        Assert.Equal("–", score.Text(0));
        Assert.Equal(default, score.Sweep(0));
        score.Apply(Live(50), 1, true, false);
        Assert.Equal((TaskScoreChangeKind.Crossfade, 0.15), (score.Change.Kind, score.Change.Duration));
        Assert.Equal("–", score.Outgoing(1.05));
        var sweep = score.Sweep(1.05);
        Assert.Equal((0, 0.5), (sweep.Settled, sweep.Changing));
        Assert.Equal(0, sweep.First.Alpha);
        Assert.True(sweep.Second.Alpha > 0);
    }

    [Fact]
    public void ReduceMotionCrossfadesEveryChangeAndShowsTheProofMarkAtOnce()
    {
        var score = new BoardHeroScore();
        score.Apply(Live(40), 0, false, true);
        score.Apply(Live(80, TaskState.Pending), 1, true, true);
        Assert.Equal((TaskScoreChangeKind.Crossfade, 0.15), (score.Change.Kind, score.Change.Duration));
        Assert.Equal(1, score.Proof(1));
        // Growing under Reduce Motion: the new stretch fades in over the old arc.
        var sweep = score.Sweep(1.05);
        Assert.Equal((0.4, 0.8), (sweep.Settled, sweep.Changing));
        Assert.Equal(score.Fade(1.05).Incoming, sweep.ChangingAlpha, 9);
        Assert.False(score.Animating(1.16));
    }

    [Fact]
    public void LiveAndRestingCrossfadeTheColorEvenWithReduceMotion()
    {
        foreach (var reduce in new[] { false, true })
        {
            var score = new BoardHeroScore();
            score.Apply(Live(70), 0, false, reduce);
            Assert.Equal(1, score.Liveness(0));
            score.Apply(Resting(70), 1, true, reduce);
            Assert.InRange(score.Liveness(1.1), 0.01, 0.99);
            Assert.True(score.Animating(1.1));
            Assert.Equal(0, score.Liveness(1.2));
            Assert.False(score.Animating(1.21));
        }
    }

    [Fact]
    public void TheProofMarkFadesInAndOutWithTheState()
    {
        var score = new BoardHeroScore();
        score.Apply(Live(70), 0, false, false);
        Assert.Equal(0, score.Proof(0));
        score.Apply(Live(70, TaskState.Pending), 1, true, false);
        Assert.InRange(score.Proof(1.1), 0.01, 0.99);
        Assert.Equal(1, score.Proof(1.25));
        score.Apply(Live(70, TaskState.Passing), 2, true, false);
        Assert.InRange(score.Proof(2.1), 0.01, 0.99);
        Assert.Equal(0, score.Proof(2.25));
        // Failing and passing carry no mark on the compact surfaces; neither does none.
        score.Apply(Live(70, TaskState.Failing), 3, true, false);
        Assert.Equal(0, score.Proof(3));
    }

    // ---- Drawing (DESIGN 12, Proof mark; 2c, Sub-gauge) ----

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(2.0, false)]
    [InlineData(1.5, true)]
    public void TheUnderlineSpreadsItsDotsAsTheProofMarkRuleSays(double scale, bool highContrast)
    {
        var list = new BoardDrawList { Scale = scale };
        var color = new Rgba(1, 1, 1, 0.9);
        BoardScoreDraw.Underline(list, 10, 30, 50, 19, color, 1, highContrast);
        var d = TaskProofMark.Diameter(19, scale, highContrast);
        var centers = TaskProofMark.UnderlineCenters(30, d);
        var dots = list.Instances.ToArray();
        Assert.Equal(centers.Count, dots.Length);
        Assert.Equal(centers.Count, BoardScoreDraw.UnderlineCount(30, d));
        for (var i = 0; i < dots.Length; i++)
        {
            Assert.Equal(OrbShape.Circle, (OrbShape)(int)dots[i].A.W);
            Assert.Equal(d / 2, dots[i].B.X, 4);
            // On the device pixel grid, within half a pixel of the rule's place.
            Assert.InRange(dots[i].A.X - (10 + centers[i]), -0.5 / scale - 1e-4, 0.5 / scale + 1e-4);
            Assert.InRange(dots[i].A.Y - (50 + 0.22 * 19), -0.5 / scale - 1e-4, 0.5 / scale + 1e-4);
            Assert.Equal(0.9 * (highContrast ? 1 : 0.7), dots[i].Color.W, 4);
        }
    }

    [Fact]
    public void AnUnprovenGlassHeroDrawsDotsAHollowTipAndTheUnderline()
    {
        var (scene, list, _) = Hero(BoardStyle.Glass, Live(60, TaskState.Unverified));
        var spec = BoardGaugeSpec.For(BoardStyle.Glass, BoardSize.M);
        var (cx, cy) = DialCenter(list, spec);
        var instances = list.Instances.ToArray();
        var dots = instances.Where(i => Is(i, OrbShape.Circle) && Math.Abs(i.B.X - spec.ArcStroke / 2) < 1e-3 && i.Extra.X > 0 &&
            Math.Abs(Distance(i, cx, cy) - spec.ArcRadius) < 1e-2).ToArray();
        var length = BoardGaugeSpec.ArcSweep * Math.PI / 180 * spec.ArcRadius;
        Assert.Equal((int)Math.Floor(0.6 * length / (2 * spec.ArcStroke) + 1e-6) + 1, dots.Length);
        // No solid value arc: only the track's band arc at the dial.
        Assert.Single(instances, i => Is(i, OrbShape.BandArc) && Near(i, cx, cy) && i.Extra.X > 0);
        var ring = Assert.Single(instances, i => Is(i, OrbShape.Ring) && i.Extra.X > 0 && Math.Abs(Distance(i, cx, cy) - spec.ArcRadius) < 1e-2);
        Assert.True(ring.B.Y >= BoardScoreDraw.TipRingShare * spec.TipRadius - 1e-4);
        Assert.Equal(spec.TipRadius, ring.B.X + ring.B.Y / 2, 3);
        // The digits' dotted underline, 0.22 × the size under their baseline.
        var baseline = BoardCanvas.Margin + scene.Metrics.HeroTop + Placement(scene).MainBaseline;
        Assert.True(instances.Count(i => Is(i, OrbShape.Circle) && Math.Abs(i.A.Y - (baseline + 0.22 * spec.MainSize)) <= 0.51) >= 2);
    }

    [Fact]
    public void AnEmptyDialDrawsNoDotAfterAnUnprovenScoreLeavesAndZeroDrawsOnlyItsRing()
    {
        var spec = BoardGaugeSpec.For(BoardStyle.Glass, BoardSize.M);
        var feed = new BoardDemoFeed(BoardSimScenario.Team) { Localizer = Zh, Readouts = BoardReadouts.All };
        var scene = new BoardScene(BoardStyle.Glass, BoardSize.M, 3);
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off));
        var tick = feed.Step(BoardStyle.Glass, BoardSize.M) with { Events = [], NewPeak = false };
        scene.Apply(tick with { Gauge = tick.Gauge with { Score = Live(60, TaskState.Unverified) } }, 0);
        scene.Settle();
        scene.Apply(tick with { Gauge = tick.Gauge with { Score = TaskSetReading.None } }, 1);
        var list = new BoardDrawList();
        scene.Render(1.05, new KeyedLayers(), list);
        var (cx, cy) = DialCenter(list, spec);
        // While it fades out, the outgoing arc keeps its dots.
        Assert.Contains(list.Instances.ToArray(), i => IsArcDot(i, spec, cx, cy));
        list.Reset();
        scene.Render(3, new KeyedLayers(), list);
        Assert.DoesNotContain(list.Instances.ToArray(), i => IsArcDot(i, spec, cx, cy) || Is(i, OrbShape.Ring));

        var (_, zero, _) = Hero(BoardStyle.Glass, Live(0, TaskState.Unverified));
        Assert.DoesNotContain(zero.Instances.ToArray(), i => IsArcDot(i, spec, cx, cy));
        Assert.Single(zero.Instances.ToArray(), i => Is(i, OrbShape.Ring) && i.Extra.X > 0 && Math.Abs(Distance(i, cx, cy) - spec.ArcRadius) < 1e-2);
    }

    [Fact]
    public void AProvenGlassHeroDrawsTheSolidArcAndTipWithoutAMark()
    {
        var (scene, list, _) = Hero(BoardStyle.Glass, Live(60));
        var spec = BoardGaugeSpec.For(BoardStyle.Glass, BoardSize.M);
        var (cx, cy) = DialCenter(list, spec);
        var instances = list.Instances.ToArray();
        // The track, the glow and the gradient pieces.
        Assert.True(instances.Count(i => Is(i, OrbShape.BandArc) && Near(i, cx, cy)) >= 3);
        Assert.DoesNotContain(instances, i => Is(i, OrbShape.Ring) && Near(i, cx, cy, spec.ArcRadius + 1));
        var baseline = BoardCanvas.Margin + scene.Metrics.HeroTop + Placement(scene).MainBaseline;
        Assert.DoesNotContain(instances, i => Is(i, OrbShape.Circle) && Math.Abs(i.A.Y - (baseline + 0.22 * spec.MainSize)) <= 0.51);
    }

    [Fact]
    public void ARestingGlassArcDrawsAtThirtyFivePercentWithoutGlow()
    {
        var (_, live, _) = Hero(BoardStyle.Glass, Live(60));
        var (_, resting, _) = Hero(BoardStyle.Glass, Resting(60));
        var spec = BoardGaugeSpec.For(BoardStyle.Glass, BoardSize.M);
        var (cx, cy) = DialCenter(resting, spec);
        var pieces = resting.Instances.ToArray().Where(i => Is(i, OrbShape.BandArc) && Near(i, cx, cy)).Skip(1).ToArray();
        Assert.NotEmpty(pieces);
        Assert.All(pieces, p => Assert.True(p.Extra.X > 0 && p.Extra.Y == 0, "no glow"));
        var livePieces = live.Instances.ToArray().Where(i => Is(i, OrbShape.BandArc) && Near(i, cx, cy) && i.Extra.X > 0).Skip(1).ToArray();
        Assert.Equal(0.35, pieces[0].Color.W / livePieces[0].Color.W, 3);
    }

    [Fact]
    public void HighContrastKeepsARestingArcApartFromItsTrack()
    {
        var (_, list, _) = Hero(BoardStyle.Glass, Resting(60), highContrast: true);
        var spec = BoardGaugeSpec.For(BoardStyle.Glass, BoardSize.M);
        var (cx, cy) = DialCenter(list, spec);
        var arcs = list.Instances.ToArray().Where(i => Is(i, OrbShape.BandArc) && Near(i, cx, cy)).ToArray();
        var track = arcs[0];
        Assert.All(arcs.Skip(1), a => Assert.True(a.Color.W >= track.Color.W + 0.3, $"{a.Color.W} vs {track.Color.W}"));
        Assert.DoesNotContain(list.Instances.ToArray(), i => i.Extra.Y > 0 && Near(i, cx, cy, spec.ArcRadius + 4));
    }

    [Fact]
    public void AnUnprovenNeonHeroDrawsItsLitSegmentsHollow()
    {
        var (_, list, _) = Hero(BoardStyle.Neon, Live(60, TaskState.Pending));
        var instances = list.Instances.ToArray();
        var hollow = instances.Where(i => Is(i, OrbShape.RoundedRing) && Math.Abs(i.B.Y - (1.5 - TaskProofMark.HollowInset)) < 1e-3).ToArray();
        Assert.Equal(BoardHeroScore.Lit(60), hollow.Length);
        Assert.All(hollow, h => Assert.Equal(TaskProofMark.HollowStroke, h.Extra.Z, 3));
        // No filled lit segment, no glow on the meter.
        var segments = instances.Where(i => Is(i, OrbShape.RoundedRect) && Math.Abs(i.B.Y - 1.5) < 1e-3).ToArray();
        Assert.Equal(12 - BoardHeroScore.Lit(60), segments.Length);
        Assert.All(segments, s => Assert.Equal(0, s.Extra.Y));
    }

    [Fact]
    public void ARestingNeonMeterLightsAtThirtyFivePercentWithoutGlow()
    {
        var (_, live, _) = Hero(BoardStyle.Neon, Live(60));
        var (_, resting, _) = Hero(BoardStyle.Neon, Resting(60));
        RoundedRect(live, out var liveLit);
        RoundedRect(resting, out var restLit);
        Assert.Equal(BoardHeroScore.Lit(60), restLit.Length);
        Assert.All(restLit, s => Assert.Equal(0, s.Extra.Y));
        Assert.Equal(0.35, restLit[0].Color.W / liveLit[0].Color.W, 3);

        static void RoundedRect(BoardDrawList list, out OrbGpuInstance[] lit)
        {
            var mind = BoardGaugePalette.For(BoardStyle.Neon, BoardPalette.For(BoardStyle.Neon, true)).Mind;
            lit = [.. list.Instances.ToArray().Where(i => Is(i, OrbShape.RoundedRect) && Math.Abs(i.B.Y - 1.5) < 1e-3 &&
                Math.Abs(i.Color.X - mind.R) < 1e-3 && Math.Abs(i.Color.Z - mind.B) < 1e-3)];
        }
    }

    [Fact]
    public void AnUnprovenTimingHeroUnderlinesItsDigits()
    {
        var (scene, list, _) = Hero(BoardStyle.Timing, Live(9, TaskState.Unverified));
        var spec = BoardGaugeSpec.For(BoardStyle.Timing, BoardSize.M);
        var baseline = BoardCanvas.Margin + scene.Metrics.HeroTop + Placement(scene).MainBaseline;
        var dots = list.Instances.ToArray().Where(i => Is(i, OrbShape.Circle) && Math.Abs(i.A.Y - (baseline + 0.22 * spec.MainSize)) <= 0.51).ToArray();
        Assert.Equal(2, dots.Length);
        Assert.NotNull(scene.Gauge.ScoreArea);
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Neon)]
    [InlineData(BoardStyle.Timing)]
    public void TheHerosScoreAreaIsWhereHoveringOpensItsCard(BoardStyle style)
    {
        var (scene, _, _) = Hero(style, Live(60));
        var area = scene.Gauge.ScoreArea!.Value;
        var spec = BoardGaugeSpec.For(style, BoardSize.M);
        var placement = Placement(scene);
        Assert.Equal(BoardCanvas.Margin + scene.Metrics.HeroTop + placement.Top, area.Top, 3);
        Assert.Equal(style == BoardStyle.Timing ? placement.MainRow : spec.MainHeight, area.Bottom - area.Top, 3);
        Assert.True(scene.HitScore((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2));
        Assert.False(scene.HitScore(area.Left - 1, area.Top - 1));
        // With 智力 off there is nothing to hover.
        var off = Hero(style, Live(60), readouts: BoardReadouts.All with { Intelligence = false }).Scene;
        Assert.Null(off.Gauge.ScoreArea);
        Assert.False(off.HitScore((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2));
    }

    // ---- Frames (DESIGN 2c, Frame policy) ----

    [Fact]
    public void ACountUpRendersAtSixtyForItsDurationOnlyAndAnIdleBoardRendersNothing()
    {
        var scene = new BoardScene(BoardStyle.Glass, BoardSize.M, 3);
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off));
        var layers = new KeyedLayers();
        var list = new BoardDrawList();
        scene.Apply(Quiet(Resting(40)), 0);
        scene.Settle();
        Render(scene, layers, list, 0, 0.2);
        Assert.Equal(FrameClass.None, scene.Demand(0.2));
        scene.Apply(Quiet(Live(80)), 1);
        Assert.Equal(FrameClass.Fps60, scene.Demand(1.0));
        Render(scene, layers, list, 1, 0.7);
        Assert.Equal(FrameClass.Fps60, scene.Demand(1.75));
        Assert.Equal(FrameClass.None, scene.Demand(1.81));
        // A tick that changes nothing renders nothing new.
        scene.Apply(Quiet(Live(80)), 2);
        Assert.Equal(FrameClass.None, scene.Demand(2));
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Neon)]
    [InlineData(BoardStyle.Timing)]
    public void CountUpAndCrossfadeFramesAllocateNothing(BoardStyle style)
    {
        var scene = new BoardScene(style, BoardSize.M, 3);
        scene.Configure(style, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off));
        var layers = new PlainLayers();
        var list = new BoardDrawList();
        BoardTick At(int hero, double row)
        {
            var agent = Agent("a", 120) with { Intelligence = BoardLiveMetricsTests.Scored(row, CoreTasks.TaskValueForm.Live, CoreState.Pending, "a") };
            var tick = RowsTick([agent], style: style);
            return tick with { Gauge = tick.Gauge with { Score = Live(hero, TaskState.Unverified) } };
        }

        void Frames(double from)
        {
            for (var t = from; t < from + 0.8; t += 1 / 60.0)
            {
                list.Reset();
                scene.Render(t, layers, list);
            }
        }

        scene.Apply(At(10, 80), 0);
        scene.Settle();
        scene.Apply(At(90, 60), 1);
        Frames(1);
        scene.Apply(At(20, 80), 2);
        scene.Apply(At(95, 50), 2.001);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Frames(2.001);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ARefreshedHoverTestsWhereTheRowsAreHeading()
    {
        var a = Agent("a", 300);
        var b = Agent("b", 100);
        var scene = new BoardScene(BoardStyle.Glass, BoardSize.M, 3);
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off));
        scene.Apply(RowsTick([a, b]), 0);
        scene.Settle();
        var first = scene.Rows.Single(r => r.Key == "a");
        var x = BoardCanvas.Margin + scene.Metrics.ListLeft + 20;
        var y = BoardCanvas.Margin + scene.Metrics.ListTop + first.Y + 4;
        // b passes a: right after the tick the rows have not moved yet, but b is heading for the first place.
        scene.Apply(RowsTick([b with { Live = new Rate(900, 3600) }, a]), 1);
        Assert.Equal("a", scene.HitRow(x, y)!.Key);
        Assert.Equal("b", scene.HitRow(x, y, atRest: true)!.Key);
    }

    // ---- Rows (DESIGN 2c, Ranking rows) ----

    [Fact]
    public void AnUnprovenRowUnderlinesItsDigitsInsideTheRow()
    {
        var root = Agent("a", 120) with { Intelligence = BoardLiveMetricsTests.Scored(86, CoreTasks.TaskValueForm.Live, CoreState.Pending, "a") };
        var (scene, list, tick) = Rows([root]);
        var row = scene.Rows.Single(r => r.Key == "a");
        Assert.Equal(1, row.IntelProof(100));
        var top = BoardCanvas.Margin + scene.Metrics.ListTop + row.Y;
        // A two-line row's 智力 is 13 pt on the figures' centred baseline.
        var size = BoardRowColumns.IntelligenceSize(BoardStyle.Glass, sub: false, twoLine: true);
        Assert.Equal(13, size);
        var d = TaskProofMark.Diameter(size, 1);
        var dots = list.Instances.ToArray().Where(i => Is(i, OrbShape.Circle) && Math.Abs(i.B.X - d / 2) < 1e-4 &&
            Math.Abs(i.A.Y - (top + scene.FigureBaseline(row.Entry) + 0.22 * size)) <= 0.51).ToArray();
        // "86" is 14 wide in the test's glyphs: n = ⌊(14 − 1) / 2.4⌋ + 1.
        Assert.Equal(6, dots.Length);
        var right = BoardCanvas.Margin + scene.Metrics.ListLeft + scene.Metrics.ListWidth - scene.Columns.IntelligenceRight;
        Assert.All(dots, dot => Assert.InRange(dot.A.X, right - 14 - 0.5, right + 0.5));
        Assert.True(tick.Entries[0].Readout!.IsUnproven);
    }

    [Fact]
    public void RowDigitsChangeInPlaceAndAFallCrossfades()
    {
        var root = Agent("a", 120);
        var scene = new BoardScene(BoardStyle.Glass, BoardSize.M, 3);
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off));
        BoardTick At(double score) => RowsTick([root with { Intelligence = BoardLiveMetricsTests.Scored(score, CoreTasks.TaskValueForm.Live, CoreState.Passing, "a") }]);
        scene.Apply(At(40), 0);
        scene.Settle();
        var row = scene.Rows.Single(r => r.Key == "a");
        // A climb replaces the digits (no count-up on rows), whatever its size.
        scene.Apply(At(80), 1);
        Assert.Equal(("80", (string?)null), (row.IntelText, row.IntelOutgoing(1.05)));
        // A fall crossfades in 0.2 s.
        scene.Apply(At(60), 2);
        Assert.Equal(("60", "80"), (row.IntelText, row.IntelOutgoing(2.1)));
        Assert.InRange(row.IntelFade(2.1).Outgoing, 0.01, 0.99);
        Assert.Null(row.IntelOutgoing(2.21));
        // Reduce Motion: every change is a 0.15 s crossfade.
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off, ReduceMotion: true));
        scene.Apply(At(70), 3);
        Assert.Equal("60", row.IntelOutgoing(3.1));
        Assert.Null(row.IntelOutgoing(3.16));
        Assert.False(row.IntelOutgoingUnproven);

        // An unproven score going to none: its underline fades out with its digits.
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(Settings.EffectsIntensity.Off));
        scene.Apply(RowsTick([root with { Intelligence = BoardLiveMetricsTests.Scored(80, CoreTasks.TaskValueForm.Live, CoreState.Pending, "a") }]), 4);
        scene.Apply(RowsTick([root]), 5);
        Assert.Equal(("–", "80", true, false), (row.IntelText, row.IntelOutgoing(5.05), row.IntelOutgoingUnproven, row.IntelHasScore));
    }

    // ---- Score cards (DESIGN 2c, Tooltip and score card) ----

    [Fact]
    public void AGroupRowsCardIsItsSupplyingMembersAndNoneIsTheRoots()
    {
        var root = Agent("r", 120) with { Intelligence = BoardLiveMetricsTests.Scored(40, CoreTasks.TaskValueForm.Live, CoreState.Passing, "r") };
        var sub = Agent("s", 60, "r") with { Intelligence = BoardLiveMetricsTests.Scored(86, CoreTasks.TaskValueForm.Live, CoreState.Pending, "s") };
        var tick = RowsTick([root, sub], BoardSize.L);
        var group = tick.Entries.First(e => e.Kind == BoardEntryKind.Group);
        var card = BoardScoreCards.Row(group, tick.Metric, Zh)!;
        Assert.Equal("s", card.AgentId);
        Assert.Equal(sub.Project, card.Subject.Project);
        Assert.Equal(1, card.Subject.SubAgents);
        Assert.Equal(86, card.Subject.Facts.Shown);
        Assert.Equal(TaskState.Pending, card.Subject.Facts.State);
        Assert.Equal(0, card.Subject.TopCount);
        Assert.Equal(Zh.Format("win.boards.tip.speedSubs", SpeedFormat.Speed(group.Group.Live), SpeedFormat.Speed(group.Group.SubLive)),
            card.Subject.FactLines![^1]);
        Assert.Equal("s", BoardScoreCards.For(tick, "r", Zh)!.AgentId);

        var subRow = tick.Entries.First(e => e.Kind == BoardEntryKind.Sub);
        var own = BoardScoreCards.Row(subRow, tick.Metric, Zh)!;
        Assert.Equal(("s", 0), (own.AgentId, own.Subject.SubAgents));
        Assert.Equal([Zh.Format("win.boards.tip.speed", SpeedFormat.Speed(60))], own.Subject.FactLines);

        var bare = RowsTick([Agent("n", 5)]);
        var none = BoardScoreCards.Row(bare.Entries[0], bare.Metric, Zh)!;
        Assert.Equal(("n", (int?)null), (none.AgentId, none.Subject.Facts.Shown));
    }

    [Fact]
    public void TheHerosCardNamesTheHighestOfNAndNothingOpensWhileNone()
    {
        var a = Agent("a", 120) with { Intelligence = BoardLiveMetricsTests.Scored(72, CoreTasks.TaskValueForm.Resting, CoreState.Passing, "a") };
        var b = Agent("b", 60) with { Intelligence = BoardLiveMetricsTests.Scored(55, CoreTasks.TaskValueForm.Resting, CoreState.Passing, "b") };
        var tick = RowsTick([a, b]);
        var card = BoardScoreCards.For(tick, BoardScoreCards.HeroKey, Zh)!;
        Assert.Equal(("a", 2), (card.AgentId, card.Subject.TopCount));
        // Resting with N ≥ 2: 閒置 · 2 個 agent 中最高.
        Assert.Equal(Zh["phase.idle"] + " · " + Zh.Format("task.card.top", 2), TaskScoreCard.Build(card.Subject, Zh).Eyebrow);

        Assert.Null(BoardScoreCards.For(RowsTick([Agent("n", 5)]), BoardScoreCards.HeroKey, Zh));
        Assert.Null(BoardScoreCards.For(tick with { Readouts = BoardReadouts.All with { Intelligence = false } }, BoardScoreCards.HeroKey, Zh));
        Assert.Null(BoardScoreCards.For(tick with { Readouts = BoardReadouts.All with { Intelligence = false } }, "a", Zh));
        Assert.Null(BoardScoreCards.For(tick, "missing", Zh));
    }

    [Fact]
    public void ARowsHelpTextIsItsCardWhileIntelligenceShows()
    {
        var root = Agent("r", 120) with { Intelligence = BoardLiveMetricsTests.Scored(9, CoreTasks.TaskValueForm.Live, CoreState.Unverified, "r") };
        var tick = RowsTick([root]);
        var group = tick.Entries[0];
        var help = BoardScoreCards.Description(tick, group, Zh);
        Assert.StartsWith("智力 9，尚未驗證", help, StringComparison.Ordinal);
        Assert.Contains(Zh["task.card.footnote"], help, StringComparison.Ordinal);
        Assert.Empty(BoardScoreCards.Description(tick with { Readouts = BoardReadouts.All with { Intelligence = false } }, group, Zh));
    }

    // ---- Helpers ----

    private static bool Is(in OrbGpuInstance i, OrbShape shape) => (OrbShape)(int)i.A.W == shape;

    private static double Distance(in OrbGpuInstance i, double x, double y) => Math.Sqrt(Math.Pow(i.A.X - x, 2) + Math.Pow(i.A.Y - y, 2));

    private static bool Near(in OrbGpuInstance i, double x, double y, double within = 1e-3) => Distance(i, x, y) <= within;

    // A dot of glass's unproven arc: a circle one stroke wide on the arc's radius, not a glow.
    private static bool IsArcDot(in OrbGpuInstance i, BoardGaugeSpec spec, double cx, double cy) =>
        Is(i, OrbShape.Circle) && Math.Abs(i.B.X - spec.ArcStroke / 2) < 1e-3 && i.Extra.X > 0 && Math.Abs(Distance(i, cx, cy) - spec.ArcRadius) < 1e-2;

    // The dial's centre: the track, the only band arc of its radius spanning the whole sweep.
    private static (double X, double Y) DialCenter(BoardDrawList list, BoardGaugeSpec spec)
    {
        var track = list.Instances.ToArray().First(i => Is(i, OrbShape.BandArc) && Math.Abs(i.B.X - spec.ArcRadius) < 1e-3 &&
            Math.Abs(i.Extra.W - BoardGaugeSpec.ArcSweep * Math.PI / 180) < 1e-3);
        return (track.A.X, track.A.Y);
    }

    private static BoardGaugePlacement Placement(BoardScene scene) =>
        BoardGaugeLayout.Place(scene.Style, scene.Size, scene.Readouts, scene.Gauge.Cells(scene.Style, scene.Size, scene.Readouts, new KeyedLayers(), 100),
            scene.Hero.Width, scene.Hero.Height, scene.Tick!.ThinkingHidden ? 1 : 0, scene.Tick.ThinkingHidden ? 0 : 1);

    // A board after one tick of the team story with its hero's 智力 replaced, rendered at rest.
    private static (BoardScene Scene, BoardDrawList List, BoardTick Tick) Hero(BoardStyle style, TaskSetReading score, bool highContrast = false,
        BoardReadouts? readouts = null)
    {
        var feed = new BoardDemoFeed(BoardSimScenario.Team) { Localizer = Zh, Readouts = readouts ?? BoardReadouts.All };
        var scene = new BoardScene(style, BoardSize.M, 3);
        scene.Configure(style, BoardSize.M, new BoardEffectsPolicy(ReduceMotion: true, HighContrast: highContrast));
        var tick = feed.Step(style, BoardSize.M);
        tick = tick with { Gauge = tick.Gauge with { Score = score } };
        scene.Apply(tick, 0);
        scene.Settle();
        var list = new BoardDrawList();
        scene.Render(100, new KeyedLayers(), list);
        return (scene, list, tick);
    }

    // A glass board at M whose rows are these agents' groups, worded with every readout on, rendered at rest.
    private static (BoardScene Scene, BoardDrawList List, BoardTick Tick) Rows(IReadOnlyList<AgentStatus> agents)
    {
        var tick = RowsTick(agents);
        var scene = new BoardScene(BoardStyle.Glass, BoardSize.M, 3);
        scene.Configure(BoardStyle.Glass, BoardSize.M, new BoardEffectsPolicy(ReduceMotion: true));
        scene.Apply(tick, 0);
        scene.Settle();
        var list = new BoardDrawList();
        scene.Render(100, new KeyedLayers(), list);
        return (scene, list, tick);
    }

    private static BoardTick RowsTick(IReadOnlyList<AgentStatus> agents, BoardSize size = BoardSize.M, BoardStyle style = BoardStyle.Glass)
    {
        var texts = new BoardTexts(Zh, TitleTheme.Vehicles);
        var groups = BoardGrouping.Group(agents, SpeedMetric.Output);
        var entries = BoardReadoutRows.Word(BoardLayoutPlan.Arrange(groups, BoardSizes.Limits(size), BoardMetrics.For(style, size, BoardReadouts.All),
            SpeedMetric.Output), BoardReadouts.All, texts);
        var total = groups.Sum(g => g.Live);
        return new BoardTick(total, SpeedFormat.Speed(total), SpeedTier.Cruise, 2000, SpeedMetric.Output, false, entries, [], false)
        {
            Readouts = BoardReadouts.All,
            Gauge = texts.Hero(Core.Intelligence.IntelligenceSums.Zero, BoardFeed.Highest(agents), CostReading.Zero),
        };
    }

    private static BoardTick Quiet(TaskSetReading score) => new(0, "–", SpeedTier.Idle, 2000, SpeedMetric.Output, false, [], [], false)
    {
        Readouts = BoardReadouts.All,
        Gauge = BoardHeroReadouts.Empty with { Score = score },
    };

    private static void Render(BoardScene scene, IBoardLayers layers, BoardDrawList list, double from, double seconds)
    {
        for (var t = from; t <= from + seconds; t += 1 / 60.0)
        {
            list.Reset();
            scene.Render(t, layers, list);
        }
    }

    /// <summary>Every layer exists, glyphs with a baseline; looking one up allocates nothing.</summary>
    private sealed class PlainLayers : IBoardLayers
    {
        public bool TryGet(string key, out BoardLayerSlot slot)
        {
            slot = key.StartsWith("#v", StringComparison.Ordinal)
                ? new BoardLayerSlot(0, 0, 0.1f, 0.1f, 11, 24, Baseline: 16)
                : new BoardLayerSlot(0, 0, 0.5f, 0.1f, 40, 16, Baseline: 12);
            return true;
        }
    }
}
