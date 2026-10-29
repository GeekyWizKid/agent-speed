using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// The task score's shapes on a board's draw list (DESIGN 12, <i>Proof mark</i>; 2c, Sub-gauge): the dotted underline under a
/// number, glass's value arc — solid, or round dots one stroke wide and two apart — with its tip or its hollow tip ring, and
/// neon's meter segments, filled or hollow. Resting draws the arc, the tip and the lit segments at 35 % without glow (high
/// contrast: in the quiet ink, so a resting arc stays apart from its track). Geometry follows <see cref="TaskProofMark"/>; dots
/// sit on device pixels; nothing allocates.
/// </summary>
public static class BoardScoreDraw
{
    /// <summary>A resting arc, tip and lit segment keep this much of their live opacity.</summary>
    public const double RestingAlpha = 0.35;

    /// <summary>The hollow tip ring's stroke, a share of the tip's radius.</summary>
    public const double TipRingShare = 0.4;

    /// <summary>
    /// The dotted underline under a number whose advance width <paramref name="width"/> starts at <paramref name="left"/> on
    /// <paramref name="baseline"/>: <c>d = max(1 px, 0.1 × size)</c> (high contrast: + 0.5), <c>n = max(2, ⌊(w − d) / 2.4 d⌋ + 1)</c>
    /// dots spread from edge to edge, 0.22 × the size below the baseline, in the number's color at 70 % (high contrast: 100 %).
    /// Dots outside <paramref name="clip"/> (when given) are left out.
    /// </summary>
    public static void Underline(BoardDrawList list, double left, double width, double baseline, double size, Rgba color, double alpha,
        bool highContrast, RectBounds clip = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        var tint = color.MultiplyAlpha((highContrast ? 1 : TaskProofMark.Opacity) * Math.Clamp(alpha, 0, 1));
        if (tint.A <= 0.002 || !(width > 0) || !(size > 0))
        {
            return;
        }

        var scale = list.Scale > 0 ? list.Scale : 1;
        var d = TaskProofMark.Diameter(size, scale, highContrast);
        var w = Math.Max(d, width);
        var count = UnderlineCount(w, d);
        var step = (w - d) / (count - 1);
        var y = Snap(baseline + TaskProofMark.Below * size, d, scale);
        var clipped = clip != default;
        if (clipped && (y < clip.Top || y > clip.Bottom))
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            list.Add(OrbGpuInstance.Circle(Snap(left + d / 2 + i * step, d, scale), y, d / 2, tint));
        }
    }

    /// <summary>The underline's dot count over <paramref name="width"/> (as <see cref="TaskProofMark.UnderlineCenters"/>).</summary>
    public static int UnderlineCount(double width, double diameter) =>
        Math.Max(2, (int)Math.Floor((Math.Max(diameter, width) - diameter) / (TaskProofMark.Pitch * diameter)) + 1);

    /// <summary>
    /// Glass's dial (DESIGN 2c, Sub-gauge, glass): the track over the whole sweep, then the value from <paramref name="sweep"/> in
    /// the pearl gradient with its glow — solid, or round dots while unproven (<paramref name="proof"/> 1, crossfading as it
    /// changes) — and its tip, a hollow ring while unproven. <paramref name="live"/> 0 draws the resting look.
    /// </summary>
    public static void Dial(BoardDrawList list, BoardGaugeSpec spec, double cx, double cy, BoardGaugePalette palette, BoardScoreSweep sweep,
        double live, double proof, double alpha)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(palette);
        var r = spec.ArcRadius;
        var stroke = spec.ArcStroke;
        list.Add(OrbGpuInstance.BandArc(cx, cy, r, stroke, BoardGaugeSpec.ArcStart, BoardGaugeSpec.ArcSweep, palette.Track.MultiplyAlpha(alpha),
            roundCaps: true));
        var solid = alpha * (1 - proof);
        var dotted = alpha * proof;
        var fading = sweep.Changing > sweep.Settled + 1e-6;
        if (solid > 0.002)
        {
            Stretch(list, cx, cy, r, stroke, 0, sweep.Settled, palette, live, solid, endCap: !fading);
            if (fading)
            {
                Stretch(list, cx, cy, r, stroke, sweep.Settled, sweep.Changing, palette, live, solid * sweep.ChangingAlpha, endCap: true,
                    startCap: sweep.Settled <= 1e-6);
            }
        }

        if (dotted > 0.002)
        {
            Dots(list, cx, cy, r, stroke, sweep, palette, live, dotted);
        }

        Tip(list, spec, cx, cy, sweep.First, palette, live, proof, alpha);
        Tip(list, spec, cx, cy, sweep.Second, palette, live, proof, alpha);
    }

    /// <summary>
    /// Neon's 12 segments (DESIGN 2c, Sub-gauge, neon): lit from the left in <c>mind</c> (glowing on dark while live), a crossfade
    /// between two counts at <paramref name="meter"/>'s progress; an unproven score's lit segments hollow (a 1 DIP inner stroke,
    /// no fill, no glow), unlit ones in <c>unlit</c>.
    /// </summary>
    public static void Meter(BoardDrawList list, BoardGaugeSpec spec, double cx, double top, BoardGaugePalette palette, (int From, int To, double Progress) meter,
        double live, double proof, double alpha)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(palette);
        var width = spec.Dial;
        var segment = (width - (BoardGaugeSpec.Segments - 1) * BoardGaugeSpec.SegmentGap) / BoardGaugeSpec.Segments;
        var left = cx - width / 2;
        var half = BoardGaugeSpec.SegmentHeight / 2;
        var cy = top + half;
        var t = Math.Clamp(meter.Progress, 0, 1);
        var color = Lit(palette, palette.Mind, live);
        var glows = palette.Glows ? live * (1 - proof) : 0;
        var inset = TaskProofMark.HollowInset;
        for (var i = 0; i < BoardGaugeSpec.Segments; i++)
        {
            var sx = left + i * (segment + BoardGaugeSpec.SegmentGap) + segment / 2;
            var was = i < meter.From ? 1.0 : 0;
            var lit = was + ((i < meter.To ? 1.0 : 0) - was) * t;
            if (1 - lit > 0.002)
            {
                list.Add(OrbGpuInstance.RoundedRect(sx, cy, segment / 2, half, 0, 1, palette.Unlit.MultiplyAlpha(alpha * (1 - lit))));
            }

            if (lit <= 0.002)
            {
                continue;
            }

            var filled = alpha * lit * (1 - proof);
            if (filled > 0.002)
            {
                list.Add(OrbGpuInstance.RoundedRect(sx, cy, segment / 2, half, 0, 1, color.MultiplyAlpha(filled), 1,
                    glows > 0 ? OrbGpuInstance.Glow(2.5) : 0, glows > 0 ? 0.8 * alpha * lit * glows : 0));
            }

            var hollow = alpha * lit * proof;
            if (hollow > 0.002)
            {
                list.Add(OrbGpuInstance.RoundedRing(sx, cy, segment / 2 - inset, half - inset, 1 - inset, TaskProofMark.HollowStroke,
                    color.MultiplyAlpha(hollow)));
            }
        }
    }

    // A stretch of the value arc between two shares of the sweep: its glow, the pearl gradient in pieces of at most 60°
    // (butt-joined), and round caps where the arc starts or ends.
    private static void Stretch(BoardDrawList list, double cx, double cy, double r, double stroke, double from, double to, BoardGaugePalette palette,
        double live, double alpha, bool endCap, bool startCap = true)
    {
        var a0 = BoardGaugeSpec.ArcStart + BoardGaugeSpec.ArcSweep * from;
        var sweep = BoardGaugeSpec.ArcSweep * (to - from);
        if (alpha <= 0.002 || sweep <= 0.05)
        {
            return;
        }

        if (palette.Glows && live > 0.002)
        {
            list.Add(OrbGpuInstance.BandArc(cx, cy, r, stroke, a0, sweep, palette.PearlB.WithAlpha(1), roundCaps: startCap && endCap, core: 0,
                glow: OrbGpuInstance.Glow(1.5), glowAlpha: 0.6 * alpha * live));
        }

        var pieces = Math.Max(1, (int)Math.Ceiling(sweep / 60));
        var step = sweep / pieces;
        for (var i = 0; i < pieces; i++)
        {
            var p0 = a0 + i * step;
            list.Add(OrbGpuInstance.BandArc(cx, cy, r, stroke, p0, step, ArcColor(palette, p0 + step, live, alpha), ArcColor(palette, p0, live, alpha)));
        }

        if (startCap)
        {
            Cap(list, cx, cy, r, stroke, a0, palette, live, alpha);
        }

        if (endCap)
        {
            Cap(list, cx, cy, r, stroke, a0 + sweep, palette, live, alpha);
        }
    }

    // The unproven arc: dots one stroke wide on a pitch of two strokes from the start of the sweep; those past the settled
    // part take the crossfade's opacity.
    private static void Dots(BoardDrawList list, double cx, double cy, double r, double stroke, BoardScoreSweep sweep, BoardGaugePalette palette,
        double live, double alpha)
    {
        var length = BoardGaugeSpec.ArcSweep * Math.PI / 180 * r;
        var pitch = 2 * stroke;
        var end = sweep.Changing * length + 1e-6;
        var settled = sweep.Settled * length + 1e-6;
        var glow = palette.Glows && live > 0.002;
        for (var at = 0.0; at <= end; at += pitch)
        {
            var a = alpha * (at <= settled ? 1 : sweep.ChangingAlpha);
            if (a <= 0.002)
            {
                continue;
            }

            var degrees = BoardGaugeSpec.ArcStart + at / r * 180 / Math.PI;
            var (x, y) = On(cx, cy, r, degrees);
            if (glow)
            {
                list.Add(OrbGpuInstance.Circle(x, y, stroke / 2, palette.PearlB, 0, OrbGpuInstance.Glow(1.5), 0.6 * a * live));
            }

            list.Add(OrbGpuInstance.Circle(x, y, stroke / 2, ArcColor(palette, degrees, live, a)));
        }
    }

    // The tip at the value's end (white with a pearl-b glow on dark, pearl-b on light), or its hollow ring while unproven.
    private static void Tip(BoardDrawList list, BoardGaugeSpec spec, double cx, double cy, BoardScoreTip tip, BoardGaugePalette palette, double live,
        double proof, double alpha)
    {
        var a = alpha * tip.Alpha;
        if (a <= 0.002)
        {
            return;
        }

        var (x, y) = On(cx, cy, spec.ArcRadius, BoardGaugeSpec.ArcStart + BoardGaugeSpec.ArcSweep * tip.At);
        var radius = spec.TipRadius;
        var glows = palette.Glows && palette.Dark ? live : 0;
        var solid = a * (1 - proof);
        if (solid > 0.002)
        {
            if (glows > 0.002)
            {
                list.Add(OrbGpuInstance.Circle(x, y, radius, palette.PearlB, 0, OrbGpuInstance.Glow(1.5), solid * glows));
            }

            var fill = palette.Glows ? Lit(palette, palette.Dark ? Rgba.White : palette.PearlB, live) : Lit(palette, palette.Number, live);
            list.Add(OrbGpuInstance.Circle(x, y, radius, fill.MultiplyAlpha(solid)));
        }

        var hollow = a * proof;
        if (hollow > 0.002)
        {
            var width = Math.Max(TipRingShare * radius, 1 / Math.Max(1, list.Scale));
            var ring = palette.HighContrast ? Lit(palette, palette.Number, live) : Lit(palette, palette.PearlB, live);
            if (glows > 0.002)
            {
                list.Add(OrbGpuInstance.Ring(x, y, radius - width / 2, width, palette.PearlB, 0, OrbGpuInstance.Glow(1.5), 0.6 * hollow * glows));
            }

            list.Add(OrbGpuInstance.Ring(x, y, radius - width / 2, width, ring.MultiplyAlpha(hollow)));
        }
    }

    private static void Cap(BoardDrawList list, double cx, double cy, double r, double stroke, double degrees, BoardGaugePalette palette, double live,
        double alpha)
    {
        var (x, y) = On(cx, cy, r, degrees);
        list.Add(OrbGpuInstance.Circle(x, y, stroke / 2, ArcColor(palette, degrees, live, alpha)));
    }

    /// <summary>
    /// A live color in its resting look at <paramref name="live"/> 0: 35 % of itself (high contrast: the quiet ink, so a resting
    /// arc or segment stays apart from the track and the unlit segments).
    /// </summary>
    public static Rgba Lit(BoardGaugePalette palette, Rgba color, double live)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var l = Math.Clamp(live, 0, 1);
        return palette.HighContrast ? Rgba.Lerp(palette.Quiet, color, l) : color.MultiplyAlpha(RestingAlpha + (1 - RestingAlpha) * l);
    }

    // The pearl gradient at an angle of the arc (the SVG's objectBoundingBox gradient from the lower left to the upper right of
    // the arc's box, x ± r, y from −r to +r/2), in its live or resting look; without glows (high contrast) one solid ink.
    private static Rgba ArcColor(BoardGaugePalette palette, double degrees, double live, double alpha)
    {
        if (!palette.Glows)
        {
            return Lit(palette, palette.PearlA, live).MultiplyAlpha(alpha);
        }

        var radians = degrees * Math.PI / 180;
        var u = (Math.Cos(radians) + 1) / 2;
        var v = (Math.Sin(radians) + 1) / 1.5;
        var t = Math.Clamp((u + 1 - v) / 2, 0, 1);
        var color = t < 0.5 ? Rgba.Lerp(palette.PearlC, palette.PearlA, t * 2) : Rgba.Lerp(palette.PearlA, palette.PearlB, (t - 0.5) * 2);
        return Lit(palette, color.WithAlpha(1), live).MultiplyAlpha(alpha);
    }

    private static (double X, double Y) On(double cx, double cy, double r, double degrees) =>
        (cx + r * Math.Cos(degrees * Math.PI / 180), cy + r * Math.Sin(degrees * Math.PI / 180));

    // A dot centre on the device pixel grid: on a pixel's centre for an odd diameter in pixels, on its edge for an even one.
    private static double Snap(double value, double diameter, double scale)
    {
        var offset = (int)Math.Round(diameter * scale) % 2 == 1 ? 0.5 : 0;
        return (Math.Round(value * scale - offset) + offset) / scale;
    }
}
