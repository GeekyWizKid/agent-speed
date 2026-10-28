using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>
/// What assistive technology reads of a board (DESIGN 2c Accessibility): the board as a list labelled <c>a11y.board</c>
/// (sources, value, title) and each group row as an item labelled <c>a11y.board.row</c>, followed by the root's effort
/// and speed (as the dashboard's rows) and <c>boards.subCount</c> when the group has sub-agents; the live metrics add their
/// words to both (DESIGN 2c, Live metrics: Accessibility).
/// </summary>
public static class BoardAccessibility
{
    public static string Board(BoardDefinition board, string valueText, string spokenTitle, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(loc);
        return loc.Format("a11y.board", OrbNaming.AutomaticName(board.Sources, loc), valueText, spokenTitle);
    }

    /// <summary>
    /// The board's label with what is switched on of its live metrics, in order: <c>a11y.orb.intel</c> (智力 and a shown 思考),
    /// <c>a11y.orb.intel.only</c> (思考 off or hidden) or <c>a11y.board.thinking</c> (智力 off), <c>intel.none</c> in place of
    /// either intelligence form while quiet; then <c>a11y.board.cost</c>, <c>a11y.board.costUnknown</c> or
    /// <c>cost.subscriptionOnly</c>.
    /// </summary>
    public static string Board(BoardDefinition board, BoardTick tick, string spokenTitle, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(tick);
        var label = Board(board, tick.ValueText, spokenTitle, loc);
        var readouts = tick.Readouts;
        if (!readouts.Hero)
        {
            return label;
        }

        var gauge = tick.Gauge;
        var parts = new List<string>(3) { label };
        var thinking = readouts.Thinking && !tick.ThinkingHidden;
        if (readouts.Intelligence)
        {
            parts.Add(gauge.Shown is null ? loc["intel.none"]
                : thinking ? loc.Format("a11y.orb.intel", gauge.Intelligence, gauge.Thinking)
                : loc.Format("a11y.orb.intel.only", gauge.Intelligence));
        }
        else if (thinking)
        {
            parts.Add(loc.Format("a11y.board.thinking", gauge.Shown is null ? "0" : gauge.Thinking));
        }

        if (readouts.Cost)
        {
            parts.Add(gauge.Cost.State switch
            {
                BoardMoneyState.Tag => loc["cost.subscriptionOnly"],
                BoardMoneyState.Unknown => loc["a11y.board.costUnknown"],
                _ => loc.Format("a11y.board.cost", gauge.Cost.SpokenNumber, gauge.Cost.Currency),
            });
        }

        return string.Join(loc.UsesLatinScript ? ", " : "，", parts);
    }

    /// <summary>The group rows of a tick in rank order (sub rows and the more lines are not list items).</summary>
    public static IReadOnlyList<BoardEntry> Items(BoardTick? tick) =>
        tick is null ? [] : [.. tick.Entries.Where(e => e.Kind == BoardEntryKind.Group)];

    public static string Row(BoardEntry entry, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(loc);
        var group = entry.Group;
        var parts = new List<string>(5) { loc.Format("a11y.board.row", entry.Rank, group.Root.Project, SpeedFormat.Speed(group.Live)) };
        var readout = entry.Readout;
        if (readout is { Spoken.Length: > 0 })
        {
            parts.Add(readout.Spoken);
        }

        var capsule = EffortCapsule.Of(group.Root.Modes);
        if (capsule.IsKnown)
        {
            parts.Add(capsule.AccessibilityText(loc));
        }

        if (group.Subs.Count > 0)
        {
            parts.Add(loc.Format("boards.subCount", group.Subs.Count));
        }

        // While the second line shows a cost.
        if (entry.TwoLine && readout?.Cost is { } cost)
        {
            parts.Add(cost.State switch
            {
                BoardMoneyState.Tag => loc["cost.subscription.tag"],
                BoardMoneyState.Unknown => loc["cost.unknown"],
                _ => loc.Format("a11y.board.rowCost", cost.SpokenNumber, cost.Currency),
            });
        }

        return string.Join(loc.UsesLatinScript ? ", " : "，", parts);
    }
}
