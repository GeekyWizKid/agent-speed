using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The proof mark's geometry, the climb and drop rules and the card's placement (DESIGN 12).</summary>
public class TaskScoreMotionTests
{
    [Theory]
    [InlineData(12, 1, 1)]
    [InlineData(12, 2, 1)]
    [InlineData(34, 1, 3)]
    [InlineData(34, 1.5, 10.0 / 3)]
    [InlineData(7, 1, 1)]
    [InlineData(7, 1.25, 0.8)]
    public void TheDotIsATenthOfTheTextSizeOnTheDevicePixelGrid(double size, double scale, double diameter) =>
        Assert.Equal(diameter, TaskProofMark.Diameter(size, scale), 9);

    [Fact]
    public void HighContrastThickensTheDots()
    {
        Assert.Equal(1.5, TaskProofMark.Diameter(12, 2, highContrast: true), 9);
        var mark = TaskProofMark.Underline(12, 20, 1, highContrast: true);
        Assert.Equal(1, mark.Opacity);
        Assert.Equal(0.7, TaskProofMark.Underline(12, 20, 1).Opacity);
    }

    [Fact]
    public void UnderlineDotsSpreadFromEdgeToEdge()
    {
        Assert.Equal([1, 7, 13, 19], TaskProofMark.UnderlineCenters(20, 2));
        var narrow = TaskProofMark.UnderlineCenters(3, 1);
        Assert.Equal(2, narrow.Count);
        Assert.Equal(0.5, narrow[0], 9);
        Assert.Equal(2.5, narrow[1], 9);
    }

    [Fact]
    public void TheUnderlineSitsBelowTheBaseline()
    {
        Assert.Equal(34 * 0.22, TaskProofMark.Underline(34, 40, 1).Below, 9);
        Assert.Equal(12 * 0.17, TaskProofMark.Underline(12, 20, 1, barBlock: true).Below, 9);
    }

    [Fact]
    public void DottedTracksTileTheirDotsToBothEnds()
    {
        var centers = TaskProofMark.SpacedCenters(40, TaskProofMark.TrackPitch, TaskProofMark.TrackDot);
        Assert.Equal(8, centers.Count);
        Assert.Equal(2.5, centers[0], 9);
        Assert.Equal(37.5, centers[^1], 9);
        Assert.Empty(TaskProofMark.SpacedCenters(2, 5, 3));
        Assert.Equal([2.0], TaskProofMark.SpacedCenters(4, 5, 3));
    }

    [Fact]
    public void ADottedArcSpacesDotsTwoStrokesApart()
    {
        var angles = TaskProofMark.ArcDots(50, 0, Math.PI / 2, 2);
        Assert.Equal(20, angles.Count);
        Assert.Equal(0.08, angles[1] - angles[0], 9);
        var back = TaskProofMark.ArcDots(50, Math.PI, 0, 2);
        Assert.True(back[1] < back[0]);
        Assert.Empty(TaskProofMark.ArcDots(50, 1, 1, 2));
    }

    [Fact]
    public void TheCompositionLeavesTwoDipBetweenNeighbours()
    {
        TaskCompositionSegment[] segments = [new(TaskDimension.Verify, 0, 40, true), new(TaskDimension.Converge, 40, 25, false)];
        var rects = TaskProofMark.Composition(segments, 272);
        Assert.Equal(2, rects.Count);
        Assert.Equal((0.0, true), (rects[0].X, rects[0].IsGhost));
        Assert.Equal(106.8, rects[0].Width, 9);
        Assert.Equal(108.8, rects[1].X, 9);
        Assert.Equal(68, rects[1].Width, 9);
    }

    [Fact]
    public void ATestPassClimbsInUnderASecond()
    {
        var change = TaskScoreMotion.Plan(41, 81, TaskScoreSurface.Orb, reduceMotion: false);
        Assert.Equal(TaskScoreChangeKind.CountUp, change.Kind);
        Assert.Equal(0.78, change.Duration, 9);
        Assert.Equal(0.8, TaskScoreMotion.CountUpDuration(90), 9);
        Assert.Equal(41, change.ValueAt(0));
        Assert.Equal(81, change.ValueAt(0.78));
        Assert.Equal(81, change.ValueAt(5));

        // Quick at first, settling into the final number, through every value in between.
        Assert.True(change.ValueAt(0.78 / 4) > 41 + 20);
        var previous = 41;
        for (var t = 0.0; t <= 0.8; t += 1 / 30.0)
        {
            var value = change.ValueAt(t)!.Value;
            Assert.InRange(value, previous, 81);
            previous = value;
        }
    }

    [Theory]
    [InlineData(TaskScoreSurface.Orb, TaskScoreChangeKind.CountUp)]
    [InlineData(TaskScoreSurface.BoardHero, TaskScoreChangeKind.CountUp)]
    [InlineData(TaskScoreSurface.Card, TaskScoreChangeKind.CountUp)]
    [InlineData(TaskScoreSurface.DashboardRow, TaskScoreChangeKind.Roll)]
    [InlineData(TaskScoreSurface.BoardRow, TaskScoreChangeKind.Replace)]
    [InlineData(TaskScoreSurface.Taskbar, TaskScoreChangeKind.Replace)]
    public void EachSurfaceClimbsItsOwnWay(TaskScoreSurface surface, TaskScoreChangeKind kind) =>
        Assert.Equal(kind, TaskScoreMotion.Plan(10, 30, surface, reduceMotion: false).Kind);

    [Fact]
    public void SmallRisesReplaceDropsCrossfadeAndReduceMotionOnlyFades()
    {
        Assert.Equal(TaskScoreChangeKind.Replace, TaskScoreMotion.Plan(10, 12, TaskScoreSurface.Orb, false).Kind);
        var fall = TaskScoreMotion.Plan(81, 41, TaskScoreSurface.Card, false);
        Assert.Equal((TaskScoreChangeKind.Crossfade, 0.2), (fall.Kind, fall.Duration));
        Assert.Equal(41, fall.ValueAt(0));
        var reduced = TaskScoreMotion.Plan(10, 60, TaskScoreSurface.Orb, true);
        Assert.Equal((TaskScoreChangeKind.Crossfade, 0.15), (reduced.Kind, reduced.Duration));
        Assert.Equal(TaskScoreChangeKind.Crossfade, TaskScoreMotion.Plan(null, 9, TaskScoreSurface.DashboardRow, false).Kind);
        Assert.Equal(TaskScoreChangeKind.None, TaskScoreMotion.Plan(9, 9, TaskScoreSurface.Orb, false).Kind);
        var (outgoing, incoming) = fall.Fade(0.1);
        Assert.Equal(1, outgoing + incoming, 9);
        Assert.Equal((0.0, 1.0), fall.Fade(0.3));
    }

    [Fact]
    public void ANewValueMidClimbContinuesFromTheShownNumber()
    {
        var ticker = new TaskScoreTicker(TaskScoreSurface.Card);
        ticker.Set(10, 0, reduceMotion: false);
        Assert.Equal(10, ticker.Shown(0));
        Assert.False(ticker.IsAnimating(0));

        ticker.Set(50, 1, reduceMotion: false);
        Assert.True(ticker.IsAnimating(1.1));
        var mid = ticker.Shown(1.2)!.Value;
        Assert.InRange(mid, 11, 49);

        ticker.Set(60, 1.2, reduceMotion: false);
        Assert.Equal(mid, ticker.Change.From);
        Assert.Equal(mid, ticker.Shown(1.2));
        Assert.Equal(60, ticker.Shown(3));
        Assert.False(ticker.IsAnimating(3));

        ticker.Set(40, 3, reduceMotion: false);
        Assert.Equal(60, ticker.Outgoing(3.05));
        Assert.Null(ticker.Outgoing(3.3));
    }

    [Fact]
    public void TheDashboardCardOpensTowardTheScreensCentre()
    {
        var screen = new RectD(0, 0, 1920, 1040);
        var flyout = new RectD(1500, 600, 400, 430);
        var spot = TaskCardPlacement.Place(TaskCardHost.Dashboard, flyout, 700, 300, screen);
        Assert.Equal(1500 - 8 - 300, spot.X);
        Assert.Equal(700, spot.Y);
        Assert.True(spot.OnLeft);
        Assert.Equal(2, spot.SlideFrom);

        var top = TaskCardPlacement.Place(TaskCardHost.Dashboard, new RectD(20, 40, 400, 430), 60, 420, screen);
        Assert.Equal((428.0, 60.0, false, -2.0), (top.X, top.Y, top.OnLeft, top.SlideFrom));
    }

    [Fact]
    public void ABoardsCardTakesTheSideWithMoreRoomAndStaysOnScreen()
    {
        var screen = new RectD(0, 0, 1920, 1040);
        var board = new RectD(1200, 100, 420, 300);
        var spot = TaskCardPlacement.Place(TaskCardHost.Board, board, 900, 400, screen);
        Assert.True(spot.OnLeft);
        Assert.Equal(1040 - 8 - 400, spot.Y);

        // Squeezed between the anchor and the edge, it is clamped inside the visible frame rather than off screen.
        var squeezed = TaskCardPlacement.Place(TaskCardHost.Dashboard, new RectD(200, 0, 600, 400), 2, 300, new RectD(0, 0, 1100, 800));
        Assert.Equal((TaskCardPlacement.Inset, false), (squeezed.Y, squeezed.OnLeft));
        Assert.Equal(1100 - 8 - 300, squeezed.X);
        Assert.Equal(0.45, TaskCardPlacement.Delay(TaskCardHost.Dashboard));
        Assert.Equal(0.35, TaskCardPlacement.Delay(TaskCardHost.Board));
    }
}
