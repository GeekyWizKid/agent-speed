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
/// and speed (as the dashboard's rows) and <c>boards.subCount</c> when the group has sub-agents.
/// </summary>
public static class BoardAccessibility
{
    public static string Board(BoardDefinition board, string valueText, string spokenTitle, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(loc);
        return loc.Format("a11y.board", OrbNaming.AutomaticName(board.Sources, loc), valueText, spokenTitle);
    }

    /// <summary>The group rows of a tick in rank order (sub rows and the more lines are not list items).</summary>
    public static IReadOnlyList<BoardEntry> Items(BoardTick? tick) =>
        tick is null ? [] : [.. tick.Entries.Where(e => e.Kind == BoardEntryKind.Group)];

    public static string Row(BoardEntry entry, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(loc);
        var group = entry.Group;
        var parts = new List<string>(3) { loc.Format("a11y.board.row", entry.Rank, group.Root.Project, SpeedFormat.Speed(group.Live)) };
        var capsule = EffortCapsule.Of(group.Root.Modes);
        if (capsule.IsKnown)
        {
            parts.Add(capsule.AccessibilityText(loc));
        }

        if (group.Subs.Count > 0)
        {
            parts.Add(loc.Format("boards.subCount", group.Subs.Count));
        }

        return string.Join(loc.UsesLatinScript ? ", " : "，", parts);
    }
}
