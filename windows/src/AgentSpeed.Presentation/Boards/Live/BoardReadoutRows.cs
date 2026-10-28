namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>Puts each row's live metrics (its 智力 cell, a group's tooltip lines) on the entries of a layout plan.</summary>
public static class BoardReadoutRows
{
    public static IReadOnlyList<BoardEntry> Word(IReadOnlyList<BoardEntry> entries, BoardReadouts readouts, BoardTexts texts)
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
            worded[i] = entry with { Readout = texts.Row(entry, readouts) };
        }

        return worded;
    }
}
