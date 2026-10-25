using AgentSpeed.Core;
using AgentSpeed.Presentation.Heat;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// What one engine tick tells a board's scene (DESIGN 2c): the hero's live value and its formatted text, heat, tier and
/// redline, whether anything generates, the rows of the layout plan at their resting places, the passes of the tick and a
/// new <c>peakToday_A</c>. The live feed (<see cref="BoardFeed"/>) and the demo (<see cref="Demo.BoardDemoFeed"/>) both make it.
/// </summary>
public sealed record BoardTick(
    double Value,
    string ValueText,
    SpeedTier Tier,
    double Redline,
    SpeedMetric Metric,
    bool Generating,
    IReadOnlyList<BoardEntry> Entries,
    IReadOnlyList<BoardRankEvent> Events,
    bool NewPeak)
{
    public double Heat => HeatMath.Heat(Value, Redline);

    public double Overdrive => HeatMath.Overdrive(Value, Redline);

    /// <summary>The value a row shows: the group speed, a sub-agent's live value, or a more line's sum.</summary>
    public double LiveOf(BoardEntry entry) => entry.Kind switch
    {
        BoardEntryKind.Group => entry.Group.Live,
        BoardEntryKind.Sub => entry.Agent?.Live.ValueFor(Metric) ?? 0,
        _ => entry.RestLive,
    };
}
