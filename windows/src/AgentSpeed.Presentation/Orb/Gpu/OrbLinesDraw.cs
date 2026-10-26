using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Lines;

namespace AgentSpeed.Presentation.Orb.Gpu;

/// <summary>Colors of the two-line readout's two kinds of text: the numbers, and the glyphs with the units.</summary>
/// <param name="ShadowSigma">σ of the shadow in points (the liquid below its surface).</param>
public readonly record struct OrbLinesColors(Rgba Number, Rgba Secondary, Rgba? Shadow = null, double ShadowSigma = 0)
{
    private static readonly Rgba Ink = Rgba.FromHex("#0B0D12");

    /// <summary>DESIGN 10: numbers at 80 % of the value token (dark white 78 %, light ink 76 %); glyphs and units 50 %.</summary>
    public static OrbLinesColors Standard(OrbGpuContext ctx)
    {
        if (ctx.HighContrast)
        {
            return new OrbLinesColors(ctx.Contrast.WindowText, ctx.Contrast.GrayText);
        }

        return ctx.Dark
            ? new OrbLinesColors(Rgba.White.WithAlpha(0.78), Rgba.White.WithAlpha(0.50))
            : new OrbLinesColors(Ink.WithAlpha(0.76), Rgba.Black.WithAlpha(0.50));
    }

    /// <summary>Below the liquid surface (the 2a.3 layer 10 split rule): white 94 / 72 % with a dark shadow, or ink 88 / 62 %.</summary>
    public static OrbLinesColors UnderLiquid(OrbGpuContext ctx)
    {
        if (ctx.HighContrast)
        {
            return Standard(ctx);
        }

        return ctx.Dark
            ? new OrbLinesColors(Rgba.White.WithAlpha(0.94), Rgba.White.WithAlpha(0.72), Rgba.Black.WithAlpha(0.55), 0.015 * ctx.Size)
            : new OrbLinesColors(Ink.WithAlpha(0.88), Ink.WithAlpha(0.62));
    }
}

/// <summary>
/// The orb's two-line readout on the GPU (DESIGN 10): each line a glyph, a number right-aligned in a cell as wide as
/// <c>8.88</c> and a unit left-aligned in a cell as wide as <c>MB/s</c>, sharing their left edge and centered on the orb.
/// Network speed draws vector arrows; the intelligence readout draws text glyphs (weight 600, 0.82 t) centered in the
/// arrow's cell and widened to their own advance, with the same cells, so switching modes moves nothing. Characters are
/// replaced in their cells once per sample (no animation); the block fades by the quiet rule and steps aside for the
/// new-peak pill. Allocates nothing per frame.
/// </summary>
public sealed class OrbLinesDraw
{
    /// <summary>The readout fades out (and back) around the new-peak pill.</summary>
    public const double PillFade = 0.15;

    private IOrbGlyphs _glyphs;

    public OrbLinesDraw(IOrbGlyphs glyphs)
    {
        _glyphs = glyphs ?? throw new ArgumentNullException(nameof(glyphs));
    }

    public IOrbGlyphs Glyphs
    {
        get => _glyphs;
        set => _glyphs = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The <c>8.88</c> number cell and the <c>MB/s</c> (bits: <c>Mb/s</c>) unit cell at text size <paramref name="t"/>.</summary>
    public (double Number, double Unit) Cells(double t, bool bits)
    {
        var digit = _glyphs.DigitCell(OrbFont.TrafficNumber, t);
        var point = _glyphs.TryGet(OrbTextKey.Create(".", OrbFont.TrafficNumber, t), out var dot) ? dot.Advance : 0.3 * t;
        var unit = _glyphs.TryGet(OrbTextKey.Create(bits ? "Mb/s" : "MB/s", OrbFont.Unit, UnitSize(t)), out var cell) ? cell.Advance : 2.2 * t;
        return (3 * digit + point, unit);
    }

    /// <summary>The glyph cell: the arrow's box, or a text glyph's own advance when that is wider.</summary>
    public double GlyphCell(double t, OrbLinesContent? content)
    {
        var cell = OrbLinesPlacement.ArrowWidth * t;
        if (content?.Kind != OrbLinesKind.Intelligence)
        {
            return cell;
        }

        return Math.Max(cell, Math.Max(TextGlyphAdvance(content.First.Glyph, t), TextGlyphAdvance(content.Second.Glyph, t)));
    }

    /// <summary>One line: glyph cell, gap, number cell, gap, unit cell (≈ 5.6 t).</summary>
    public double LineWidth(double t, OrbLinesContent? content)
    {
        var (number, unit) = Cells(t, content?.Bits ?? false);
        return GlyphCell(t, content) + 2 * OrbLinesPlacement.Gap * t + number + unit;
    }

    /// <summary>A line fits the style's free area at this size (computed with the platform font, never truncated or shrunk).</summary>
    public bool Fits(OrbLinesPlacement placement, double s, OrbLinesContent? content) =>
        s >= 64 && LineWidth(placement.TextSize(s), content) <= placement.Fit * s;

    /// <summary>The block's opacity at <paramref name="time"/>: the quiet rule, and around the new-peak pill where the pill shares the area.</summary>
    public static double Alpha(in OrbLinesReading reading, OrbScene scene, bool yieldsToPill)
    {
        var reduce = scene.Effects.ReduceMotion;
        var alpha = reading.Alpha(scene.Time, reduce);
        if (yieldsToPill && scene.PeakAge is { } age)
        {
            var fade = reduce ? 0.001 : PillFade;
            var total = OrbScene.PeakPillDuration;
            if (age < total)
            {
                alpha *= 1 - Math.Min(1, age / fade);
            }
            else if (age < total + fade)
            {
                alpha *= (age - total) / fade;
            }
        }

        return alpha;
    }

    /// <param name="shift">The readout stack's shift (points, DESIGN 10 Centering).</param>
    public void Add(OrbDrawList list, OrbGpuContext ctx, OrbScene scene, in OrbLinesReading reading, OrbLinesPlacement placement,
        double shift, in OrbLinesColors colors, OrbTextClip clip, bool yieldsToPill)
    {
        var alpha = Alpha(reading, scene, yieldsToPill);
        if (alpha <= 0.01)
        {
            return;
        }

        var s = ctx.Size;
        var t = placement.TextSize(s);
        var content = reading.Content;
        var (numberCell, _) = Cells(t, content.Bits);
        var width = LineWidth(t, content);
        if (s < 64 || width > placement.Fit * s)
        {
            return;
        }

        var left = ctx.CenterX - width / 2;
        var glyphCell = GlyphCell(t, content);
        var (top, bottom) = placement.Baselines(ctx.CenterY + shift, s);
        var (topLine, bottomLine) = content.Stack(placement.Below);
        var gap = OrbLinesPlacement.Gap * t;
        var cells = new LineCells(left, glyphCell, left + glyphCell + gap + numberCell, gap, t);
        var paint = new LinePaint(colors.Number.MultiplyAlpha(alpha), colors.Secondary.MultiplyAlpha(alpha), colors.Shadow?.MultiplyAlpha(alpha),
            colors.Shadow is null ? 0 : colors.ShadowSigma, clip);
        var arrows = content.Kind == OrbLinesKind.Traffic;
        Line(list, topLine, arrows, cells, top, paint);
        Line(list, bottomLine, arrows, cells, bottom, paint);
    }

    private void Line(OrbDrawList list, in OrbLine line, bool arrow, in LineCells cells, double baseline, in LinePaint paint)
    {
        var t = cells.Size;
        var glyphKey = arrow
            ? OrbTextKey.Create(line.Glyph, OrbFont.Arrow, t, 0, paint.Sigma)
            : OrbTextKey.Create(line.Glyph, OrbFont.TrafficNumber, UnitSize(t), 0, paint.Sigma);
        if (_glyphs.TryGet(glyphKey, out var glyph))
        {
            list.AddText(glyph, cells.Left + (cells.Glyph - glyph.Advance) / 2, baseline, paint.Secondary, paint.Shadow, paint.Clip);
        }

        var digit = _glyphs.DigitCell(OrbFont.TrafficNumber, t);
        var advance = 0.0;
        foreach (var ch in line.Number)
        {
            advance += CellWidth(ch, t, digit, paint.Sigma);
        }

        var x = cells.NumberRight - advance;
        foreach (var ch in line.Number)
        {
            var cell = CellWidth(ch, t, digit, paint.Sigma);
            if (_glyphs.TryGet(OrbTextKey.Create(OrbReadoutDraw.CharText(ch), OrbFont.TrafficNumber, t, 0, paint.Sigma), out var character))
            {
                list.AddText(character, x + (cell - character.Advance) / 2, baseline, paint.Number, paint.Shadow, paint.Clip);
            }

            x += cell;
        }

        if (line.Unit.Length > 0 && _glyphs.TryGet(OrbTextKey.Create(line.Unit, OrbFont.Unit, UnitSize(t), 0, paint.Sigma), out var unit))
        {
            list.AddText(unit, cells.NumberRight + cells.Gap, baseline, paint.Secondary, paint.Shadow, paint.Clip);
        }
    }

    private double TextGlyphAdvance(string text, double t) =>
        _glyphs.TryGet(OrbTextKey.Create(text, OrbFont.TrafficNumber, UnitSize(t)), out var glyph) ? glyph.Advance : 0;

    private double CellWidth(char ch, double t, double digit, double sigma) =>
        char.IsAsciiDigit(ch) ? digit
        : _glyphs.TryGet(OrbTextKey.Create(OrbReadoutDraw.CharText(ch), OrbFont.TrafficNumber, t, 0, sigma), out var glyph) ? glyph.Advance : 0;

    private static double UnitSize(double t) => OrbLinesPlacement.UnitScale * Math.Max(OrbReadoutLayout.MinTextSize, t);

    /// <summary>Horizontal cells of both lines at text size <see cref="Size"/>.</summary>
    private readonly record struct LineCells(double Left, double Glyph, double NumberRight, double Gap, double Size);

    private readonly record struct LinePaint(Rgba Number, Rgba Secondary, Rgba? Shadow, double Sigma, OrbTextClip Clip);
}
