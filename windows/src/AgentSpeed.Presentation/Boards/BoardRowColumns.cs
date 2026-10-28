namespace AgentSpeed.Presentation.Boards;

/// <summary>
/// The right-hand columns of a ranking row, measured from the row's right end in pt (DESIGN 2c, Ranking rows): the value, the
/// 智力 cell while the column shows, timing's interval (none at S), and where the capsule column ends. With 智力 off the rows
/// take the columns of the boards without metrics (value 46; timing's interval 38).
/// </summary>
/// <param name="Padding">The row's own right padding (glass and neon 6, timing 4).</param>
/// <param name="Gap">Between columns (glass and neon 8, timing 7).</param>
/// <param name="Value">The value column's width.</param>
/// <param name="Intelligence">The 智力 cell's width; 0 without the column.</param>
/// <param name="Interval">Timing's interval column; 0 for the other styles and at S.</param>
public readonly record struct BoardRowColumns(double Padding, double Gap, double Value, double Intelligence, double Interval)
{
    /// <summary>The value's right edge from the row's right end.</summary>
    public double ValueRight => Padding;

    /// <summary>The 智力 cell's right edge from the row's right end.</summary>
    public double IntelligenceRight => Padding + Value + Gap;

    /// <summary>Timing's interval's right edge from the row's right end.</summary>
    public double IntervalRight => IntelligenceRight + (Intelligence > 0 ? Intelligence + Gap : 0);

    /// <summary>The capsule column's right edge from the row's right end.</summary>
    public double CapsuleRight => IntervalRight + (Interval > 0 ? Interval + Gap : 0);

    public static BoardRowColumns For(BoardStyle style, BoardSize size, BoardReadouts readouts)
    {
        var i = size switch
        {
            BoardSize.S => 0,
            BoardSize.L => 2,
            _ => 1,
        };

        var column = readouts.Intelligence;
        if (style == BoardStyle.Timing)
        {
            // The plain tower: position, stripe, name, capsule, interval 38, value 46 (S: value 44, no interval).
            return column
                ? new BoardRowColumns(4, 7, size == BoardSize.S ? 40 : 42, new[] { 22.0, 24, 26 }[i], size == BoardSize.S ? 0 : 34)
                : new BoardRowColumns(4, 7, size == BoardSize.S ? 44 : 46, 0, size == BoardSize.S ? 0 : 38);
        }

        return column ? new BoardRowColumns(6, 8, 44, new[] { 20.0, 22, 24 }[i], 0) : new BoardRowColumns(6, 8, 46, 0, 0);
    }
}
