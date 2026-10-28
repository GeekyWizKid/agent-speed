using AgentSpeed.Core;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>Puts each row's live metrics (its 智力 cell, a group's tooltip lines) on the entries of a layout plan.</summary>
public static class BoardReadoutRows
{
    public static IReadOnlyList<BoardEntry> Word(IReadOnlyList<BoardEntry> entries, BoardReadouts readouts, SpeedMetric metric, BoardTexts texts)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(texts);
        if (readouts.IsNone)
        {
            return entries;
        }

        var worded = new BoardEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var live = entry.Kind switch
            {
                BoardEntryKind.Group => entry.Group.Live,
                BoardEntryKind.Sub => entry.Agent is { } agent ? BoardGrouping.LiveOf(agent, metric) : 0,
                _ => 0,
            };

            worded[i] = entry with { Readout = texts.Row(entry, readouts, live) };
        }

        return worded;
    }
}
