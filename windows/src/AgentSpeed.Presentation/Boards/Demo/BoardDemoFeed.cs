using System.Globalization;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Boards.Demo;

/// <summary>
/// The demo's data for the board harnesses: <see cref="BoardSim"/> ticks fed through the real <see cref="BoardGrouping"/>,
/// <see cref="BoardRanking"/> and <see cref="BoardLayoutPlan"/> into a <see cref="BoardTick"/>, with the hero's title, counts
/// and history and the footer's texts.
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

    /// <summary>Localizes the speed title (none: the title stays empty).</summary>
    public Localizer? Localizer { get; set; }

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
        var generating = agents.Count(a => a.Phase == AgentPhase.Generating);
        var tools = agents.Count(a => a.Phase == AgentPhase.Tools);
        var title = SpeedTitles.Raw(total, Redline);
        return new BoardTick(total, SpeedFormat.Speed(total), HeatMath.RawTier(total, Redline), Redline, Metric, generating > 0, entries,
            result.Events, newPeak)
        {
            Title = Localizer is { } loc ? DisplayText.TitleName(title, loc) : string.Empty,
            GeneratingCount = generating,
            ToolsCount = tools,
            History = Seconds(Sim.History),
            PeakToday = Sim.Peak,
            TodayText = SpeedFormat.Tokens((long)Sim.Today),
            CostText = "$" + Sim.Cost.ToString("0.00", CultureInfo.InvariantCulture),
            PeakText = SpeedFormat.Speed(Sim.Peak),
        };
    }

    /// <summary>The sim's per-tick totals as one value per second, the latest last.</summary>
    internal static double[] Seconds(IReadOnlyList<double> ticks)
    {
        var perSecond = (int)Math.Round(1 / BoardSim.TickSeconds);
        var count = (ticks.Count + perSecond - 1) / perSecond;
        var seconds = new double[count];
        for (var i = 0; i < count; i++)
        {
            seconds[count - 1 - i] = ticks[ticks.Count - 1 - i * perSecond];
        }

        return seconds;
    }
}
