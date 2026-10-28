using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>
/// The tooltip of a hovered group row (DESIGN 2c Interaction, after 0.35 s): project and source, model, the root's effort
/// and speed lines (as the dashboard's rows), the group speed with its sub-agents' share, then the live metrics lines of the
/// switched-on readouts (DESIGN 2c, Live metrics: Tooltip).
/// </summary>
public static class BoardTooltip
{
    public const double DelaySeconds = 0.35;

    /// <summary>A group row's lines with its live metrics lines (worded on the entry by the tick).</summary>
    public static IReadOnlyList<string> Lines(BoardEntry entry, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var lines = Lines(entry.Group, loc);
        return entry.Readout is { TooltipLines.Count: > 0 } readout ? [.. lines, .. readout.TooltipLines] : lines;
    }

    public static IReadOnlyList<string> Lines(BoardGroup group, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(loc);
        var root = group.Root;
        var lines = new List<string>(6) { root.Project + " · " + OrbNaming.SourceName(root.Source, loc) };
        if (!string.IsNullOrWhiteSpace(root.Model))
        {
            lines.Add(root.Model);
        }

        lines.AddRange(EffortCapsule.Of(root.Modes).TooltipLines(loc));
        lines.Add(group.Subs.Count > 0
            ? loc.Format("win.boards.tip.speedSubs", SpeedFormat.Speed(group.Live), SpeedFormat.Speed(group.SubLive))
            : loc.Format("win.boards.tip.speed", SpeedFormat.Speed(group.Live)));
        return lines;
    }
}
