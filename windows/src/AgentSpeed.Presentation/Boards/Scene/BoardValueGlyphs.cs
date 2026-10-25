using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Modes;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// The rows' values (the prototypes' <c>.speed</c>) composed from cached glyph layers, like the hero number: values change on
/// every tick, so they are never rasterized then (DESIGN 2c Two layers). Each size has one white glyph set; a row tints its
/// glyphs to its color, its warm flash or its dim. A value below 0.05 shows an en dash in the dim weight.
/// </summary>
public static class BoardValueGlyphs
{
    public const string Glyphs = "0123456789.kMB–";

    /// <summary>The en dash of a value below 0.05 (METRICS 7: never shown as a number).</summary>
    public const char Dash = '–';

    /// <summary>Room around a glyph's advance box in its layer.</summary>
    public const double Pad = 2;

    private static readonly string[] GroupKeys = Glyphs.Select(c => "#vg" + c).ToArray();
    private static readonly string[] SubKeys = Glyphs.Select(c => "#vs" + c).ToArray();

    /// <summary>The glyph keys of group values or of sub values, in <see cref="Glyphs"/> order.</summary>
    public static IReadOnlyList<string> Keys(bool sub) => sub ? SubKeys : GroupKeys;

    public static string? KeyOf(char c, bool sub)
    {
        var i = Glyphs.IndexOf(c, StringComparison.Ordinal);
        return i < 0 ? null : Keys(sub)[i];
    }

    /// <summary>What a row with <paramref name="live"/> shows.</summary>
    public static string Text(double live) => live > 0.05 ? SpeedFormat.Speed(live) : Dash.ToString();

    /// <summary>The value's font size: 13 pt, a sub row's 11 pt (glass 11.5).</summary>
    public static double Size(BoardStyle style, bool sub) => !sub ? 13 : style == BoardStyle.Glass ? 11.5 : 11;

    /// <summary>The CSS letter spacing of −0.02 em between glyphs.</summary>
    public static double Tracking(BoardStyle style, bool sub) => -0.02 * Size(style, sub);

    /// <summary>The value's right edge inside the row (the row's right padding).</summary>
    public static double Inset(BoardStyle style) => style == BoardStyle.Timing ? 4 : 6;

    /// <summary>
    /// The value's color: dim below 0.05; glass and neon color a hot value (heat above 0.45) by its heat, glass on group rows
    /// only; otherwise the primary ink, a sub row's secondary.
    /// </summary>
    public static Rgba Color(BoardStyle style, BoardPalette palette, bool sub, double live, double redline)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (!(live > 0.05))
        {
            return palette.Ink3;
        }

        var heat = HeatMath.Heat(live, redline);
        var hot = heat > 0.45 && !palette.HighContrast && style != BoardStyle.Timing && (!sub || style == BoardStyle.Neon);
        return hot ? EffortCapsulePalette.Heat(heat, palette.Dark) : sub ? palette.Ink2 : palette.Ink;
    }

    /// <summary>
    /// Draws <paramref name="text"/> ending at <paramref name="right"/>, its line centered on <paramref name="centerY"/> (canvas
    /// points), in <paramref name="color"/>; glyphs still missing are skipped.
    /// </summary>
    public static void Emit(BoardDrawList list, IBoardLayers layers, string text, bool sub, double right, double centerY, double tracking,
        Rgba color, double alpha, RectBounds clip)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(text);
        if (alpha * color.A <= 0.002)
        {
            return;
        }

        var width = 0.0;
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (TryGlyph(text[i], sub, layers, out var slot))
            {
                width += slot.Width - 2 * Pad + (count++ > 0 ? tracking : 0);
            }
        }

        var x = right - width;
        var tint = color.WithAlpha(1);
        for (var i = 0; i < text.Length; i++)
        {
            if (!TryGlyph(text[i], sub, layers, out var slot))
            {
                continue;
            }

            list.AddLayer(BoardLayerInstance.Create(slot, x - Pad, centerY - slot.Height / 2, alpha * color.A, tint, 1, clip));
            x += slot.Width - 2 * Pad + tracking;
        }
    }

    private static bool TryGlyph(char c, bool sub, IBoardLayers layers, out BoardLayerSlot slot)
    {
        slot = default;
        return KeyOf(c, sub) is { } key && layers.TryGet(key, out slot);
    }
}
