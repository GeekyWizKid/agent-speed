using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Heat;

namespace AgentSpeed.Presentation.Boards.Demo;

/// <summary>
/// The demo's data (placeholder rows for the board harnesses): <see cref="BoardSim"/> ticks fed through the real
/// <see cref="BoardGrouping"/>, <see cref="BoardRanking"/> and <see cref="BoardLayoutPlan"/> into a <see cref="BoardTick"/>.
/// </summary>
public sealed class BoardDemoFeed
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly BoardRanking _ranking = new();
    private double _lastPeak;

    public BoardDemoFeed(BoardSimScenario scenario = BoardSimScenario.Swarm, uint seed = 7)
    {
        Sim = new BoardSim(scenario, seed);
        _lastPeak = Sim.Peak;
    }

    public BoardSim Sim { get; }

    public double Redline { get; set; } = HeatMath.DefaultRedline;

    public SpeedMetric Metric { get; set; } = SpeedMetric.Output;

    /// <summary>Overtakes and new leaders since start.</summary>
    public int Events { get; private set; }

    public void Reset(BoardSimScenario scenario)
    {
        Sim.Reset(scenario);
        _ranking.Reset();
        _lastPeak = Sim.Peak;
    }

    /// <summary>Advances the story by one engine tick and lays it out for <paramref name="style"/> at <paramref name="size"/>.</summary>
    public BoardTick Step(BoardStyle style, BoardSize size, string? expanded = null)
    {
        Sim.Step();
        return Current(style, size, expanded);
    }

    /// <summary>The story's state after the last tick, ranked once more (a tick of the ranking).</summary>
    public BoardTick Current(BoardStyle style, BoardSize size, string? expanded = null)
    {
        var agents = Sim.Statuses(Epoch);
        var groups = BoardGrouping.Group(agents, Metric);
        var inputs = new BoardRankInput[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            inputs[i] = groups[i].RankInput;
        }

        var result = _ranking.Tick(inputs);
        var byId = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var ranked = result.Order.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        var entries = BoardLayoutPlan.Arrange(ranked, BoardSizes.Limits(size), BoardMetrics.For(style, size), Metric, expanded);
        Events += result.Events.Count;
        var total = Sim.Total;
        var newPeak = Sim.Peak > _lastPeak + 1;
        _lastPeak = Sim.Peak;
        var generating = agents.Any(a => a.Phase == AgentPhase.Generating);
        return new BoardTick(total, SpeedFormat.Speed(total), HeatMath.RawTier(total, Redline), Redline, Metric, generating, entries, result.Events,
            newPeak);
    }
}
