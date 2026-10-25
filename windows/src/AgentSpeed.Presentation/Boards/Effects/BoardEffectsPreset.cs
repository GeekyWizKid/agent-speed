using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>How a hero punch releases embers (DESIGN 2c, Hero number).</summary>
public enum BoardEmbers
{
    /// <summary>Glass: rising sparks.</summary>
    Rising,

    /// <summary>Timing: a horizontal spray to the right.</summary>
    Spray,

    /// <summary>Neon: rising sparks and a lightning arc across the digits.</summary>
    RisingWithArc,
}

/// <summary>
/// One style's effect numbers (DESIGN 2c, Motion and effects; <c>BoardFX.create</c> options and the call sites in
/// <c>spec/prototypes/board/board-*.html</c>).
/// </summary>
/// <param name="Comets">Comets per effect tier, idle to redline.</param>
/// <param name="RingWidth">Width of the lit ring.</param>
/// <param name="BloomBlur">Blur of the bloom outside the board at bloom 1 (the CSS <c>box-shadow</c> blur).</param>
/// <param name="BloomSpread">Spread of the bloom at bloom 1.</param>
/// <param name="Warp">Hyperspace streaks behind the board from h = 0.2.</param>
/// <param name="OvertakeBurst">The burst that leaves the climber's rank (its first color is replaced, see <paramref name="OvertakeColor"/>).</param>
/// <param name="OvertakeColor">The burst's first color, or null for the climber's source color.</param>
/// <param name="OvertakeArc">Lightning from the climber's rank to the row it passed.</param>
/// <param name="LeaderStrike">Lightning from the top edge to the new leader's rank and a flash of the board.</param>
/// <param name="SweepSeconds">Duration of the light sweep across an overtaking row.</param>
public sealed record BoardEffectsPreset(
    IReadOnlyList<int> Comets,
    double RingWidth,
    double BloomBlur,
    double BloomSpread,
    bool Warp,
    BoardEmbers Embers,
    BoardBurst OvertakeBurst,
    Rgba? OvertakeColor,
    bool OvertakeArc,
    bool LeaderStrike,
    double SweepSeconds)
{
    public int CometsAt(int tier) => Comets[Math.Clamp(tier, 0, Comets.Count - 1)];
}

public static class BoardEffectsPresets
{
    private static readonly Rgba Placeholder = Rgba.White;

    public static BoardEffectsPreset Glass { get; } = new([1, 1, 2, 3, 4], 1.6, 70, 6, false, BoardEmbers.Rising,
        new BoardBurst(14, 120, 0.7, new BoardColors(Placeholder, Rgba.White), 1.5, 60), null, false, false, 0.75);

    // The timing prototype draws no bloom; DESIGN 2c gives every style one, so timing takes glass's.
    public static BoardEffectsPreset Timing { get; } = new([1, 1, 2, 2, 3], 1.2, 70, 6, false, BoardEmbers.Spray,
        new BoardBurst(12, 130, 0.6, new BoardColors(Placeholder, Rgba.White), 1.4, 50), Rgba.FromHex("#22C55E"), false, false, 0.7);

    public static BoardEffectsPreset Neon { get; } = new([1, 2, 3, 4, 4], 1.4, 80, 8, true, BoardEmbers.RisingWithArc,
        new BoardBurst(16, 140, 0.7, new BoardColors(Placeholder, Rgba.White), 1.5, 60), null, true, true, 0.6);

    public static BoardEffectsPreset For(BoardStyle style) => style switch
    {
        BoardStyle.Timing => Timing,
        BoardStyle.Neon => Neon,
        _ => Glass,
    };
}
