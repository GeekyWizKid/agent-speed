using AgentSpeed.Boards.Gpu;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// A board fed by the demo (the C# port of <c>spec/prototypes/board/sim.js</c>) on a caller's clock, for the frame harness,
/// the bench and the window check: a sim tick every 0.5 s goes through the real grouping, ranking and layout plan into the
/// scene; frames are built by the same <see cref="BoardFrame"/> as a window's.
/// </summary>
internal sealed class BoardRig : IDisposable
{
    private double _nextTick = double.NegativeInfinity;

    public BoardRig(BoardStyle style, BoardSize size, BoardSimScenario scenario, BoardEffectsPolicy policy, Localizer localizer, uint seed = 7)
    {
        Style = style;
        Size = size;
        Feed = new BoardDemoFeed(scenario, seed);
        Frame = new BoardFrame(style, size, new PlaceholderBoardPainter(localizer), seed);
        Frame.Scene.Configure(style, size, policy);
    }

    public BoardStyle Style { get; }

    public BoardSize Size { get; }

    public BoardDemoFeed Feed { get; }

    public BoardFrame Frame { get; }

    public BoardScene Scene => Frame.Scene;

    /// <summary>Ticks applied so far.</summary>
    public int Ticks { get; private set; }

    /// <summary>The last tick applied.</summary>
    public BoardTick? LastTick { get; private set; }

    /// <summary>Applies the sim's ticks due at <paramref name="now"/>; true when one was applied.</summary>
    public bool Advance(double now)
    {
        if (now < _nextTick)
        {
            return false;
        }

        _nextTick = double.IsNegativeInfinity(_nextTick) ? now + BoardSim.TickSeconds : _nextTick + BoardSim.TickSeconds;
        if (_nextTick <= now)
        {
            _nextTick = now + BoardSim.TickSeconds;
        }

        var tick = Feed.Step(Style, Size);
        Scene.Apply(tick, now);
        Frame.Invalidate();
        LastTick = tick;
        Ticks++;
        return true;
    }

    public void Dispose() => Frame.Dispose();
}
