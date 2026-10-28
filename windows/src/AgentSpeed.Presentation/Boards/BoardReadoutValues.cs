using AgentSpeed.Core.Intelligence;

namespace AgentSpeed.Presentation.Boards;

/// <summary>How a board's cost reads (DESIGN 2c, Live metrics: Values).</summary>
public enum BoardMoneyState
{
    /// <summary>A rate in the display currency (<c>≥</c> before it when partly priced).</summary>
    Value,

    /// <summary>A real 0 (quiet, or a free model): drawn in the quiet ink, with its unit.</summary>
    Zero,

    /// <summary>Nothing priced: <c>–</c> in the quiet ink, no unit.</summary>
    Unknown,

    /// <summary>Every source (the group's source) is excluded as a subscription: <c>cost.subscription.tag</c>, no unit.</summary>
    Tag,
}

/// <summary>A cost per second as a board shows it (section 9 rate form, METRICS 9.8 currency).</summary>
/// <param name="Number">The digits, <c>0</c>, <c>–</c> or the subscription tag.</param>
/// <param name="Unit"><c>cost.unitPerSecond</c> with the current symbol (<c>US$/s</c>); <c>–</c> and the tag do not draw it (<see cref="HasUnit"/>).</param>
/// <param name="AtLeast">Part of the value has no known price: <c>≥</c> precedes it.</param>
/// <param name="SpokenNumber">The number as accessibility reads it, wrapped in <c>a11y.orb.costAtLeast</c> when partial.</param>
/// <param name="Currency">The currency's name (<c>currency.&lt;code&gt;</c>).</param>
public sealed record BoardMoney(string Number, string Unit, bool AtLeast, BoardMoneyState State, string SpokenNumber, string Currency)
{
    public bool Quiet => State != BoardMoneyState.Value;

    public bool HasUnit => State is BoardMoneyState.Value or BoardMoneyState.Zero;
}

/// <summary>
/// The hero's live metrics of one tick (DESIGN 2c, Live metrics: Values): 智力 over the board's sources, its thinking rate and
/// its cost per second, as shown.
/// </summary>
/// <param name="Intelligence">The integer index, or <c>–</c> while quiet.</param>
/// <param name="Shown">The index as shown; null while quiet.</param>
/// <param name="Thinking">METRICS 7 of <c>liveThinking</c>; <c>–</c> while quiet.</param>
/// <param name="Quiet">Nothing generated in the window over the board's sources (<c>Σ w = 0</c>).</param>
/// <param name="ThinkingZero">The shown thinking reads 0 under a live value (it hides after 3 s).</param>
public sealed record BoardHeroReadouts(string Intelligence, int? Shown, string Thinking, bool Quiet, bool ThinkingZero, BoardMoney Cost)
{
    public const string Dash = "–";

    /// <summary>A quiet hero in US dollars (before the first tick).</summary>
    public static BoardHeroReadouts Empty { get; } = new(Dash, null, Dash, true, false,
        new BoardMoney("0", "$/s", false, BoardMoneyState.Zero, "0", string.Empty));
}

/// <summary>
/// A ranking row's live metrics of one tick (DESIGN 2c, Ranking rows): its 智力 cell and, for a group row, its cost per second.
/// </summary>
/// <param name="Intelligence">The cell's text: the live index, the resting grade, or <c>–</c>.</param>
/// <param name="Spoken"><c>a11y.agent.intel</c> or <c>a11y.agent.grade</c>; empty for none.</param>
/// <param name="Cost">The group's cost (null for a sub row and while the row's value shows <c>–</c>).</param>
/// <param name="TooltipLines">The group tooltip's live metrics lines (DESIGN 2c, Tooltip), in order.</param>
public sealed record BoardRowReadout(string Intelligence, AgentIntelligenceForm Form, string Spoken, BoardMoney? Cost,
    IReadOnlyList<string> TooltipLines)
{
    public bool Live => Form == AgentIntelligenceForm.Live;
}
