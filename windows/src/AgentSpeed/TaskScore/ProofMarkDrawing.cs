using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.TaskScore;

/// <summary>
/// Draws the proof mark (待證記號, DESIGN 12) in WPF: the dotted underline under a number, dotted tracks and held
/// credits, glass's dotted arc with its hollow tip ring and neon's hollow segments. The geometry is
/// <see cref="TaskProofMark"/>'s, shared with the GPU renderers.
/// </summary>
internal static class ProofMarkDrawing
{
    /// <summary>The dotted underline under a number whose advance width starts at <paramref name="left"/> on <paramref name="baseline"/>.</summary>
    /// <param name="opacity">Multiplies the mark's own 70 % (for its 0.25 s fade).</param>
    public static void Underline(DrawingContext dc, TaskUnderline mark, double left, double baseline, Brush brush, double opacity = 1)
    {
        ArgumentNullException.ThrowIfNull(dc);
        ArgumentNullException.ThrowIfNull(mark);
        var alpha = mark.Opacity * Math.Clamp(opacity, 0, 1);
        if (alpha <= 0)
        {
            return;
        }

        dc.PushOpacity(alpha);
        Dots(dc, mark.Centers, left, baseline + mark.Below, mark.Diameter, brush);
        dc.Pop();
    }

    /// <summary>Round dots of <paramref name="diameter"/> centred on <paramref name="y"/> at <paramref name="left"/> + each centre.</summary>
    public static void Dots(DrawingContext dc, IReadOnlyList<double> centers, double left, double y, double diameter, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(dc);
        ArgumentNullException.ThrowIfNull(centers);
        var r = diameter / 2;
        foreach (var x in centers)
        {
            dc.DrawEllipse(brush, null, new Point(left + x, y), r, r);
        }
    }

    /// <summary>A dotted track or held credit filling <paramref name="rect"/>'s width, its dots centred on the rect's middle.</summary>
    public static void DottedRow(DrawingContext dc, Rect rect, double pitch, double diameter, Brush brush)
    {
        if (rect.Width <= 0)
        {
            return;
        }

        Dots(dc, TaskProofMark.SpacedCenters(rect.Width, pitch, diameter), rect.X, rect.Y + rect.Height / 2, diameter, brush);
    }

    /// <summary>
    /// Glass's value arc while unproven: round dots one stroke wide, two strokes apart, from <paramref name="start"/> to
    /// <paramref name="end"/> (radians, clockwise from the x axis), with a hollow tip ring at the end.
    /// </summary>
    public static void DottedArc(DrawingContext dc, Point center, double radius, double start, double end, double stroke, Brush brush, double tipRadius)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var r = stroke / 2;
        foreach (var angle in TaskProofMark.ArcDots(radius, start, end, stroke))
        {
            dc.DrawEllipse(brush, null, At(center, radius, angle), r, r);
        }

        if (tipRadius > 0)
        {
            TipRing(dc, At(center, radius, end), tipRadius, brush);
        }
    }

    /// <summary>The hollow tip ring: a 1 DIP ring where the solid tip would be.</summary>
    public static void TipRing(DrawingContext dc, Point at, double radius, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var pen = new Pen(brush, TaskProofMark.HollowStroke);
        pen.Freeze();
        var inner = Math.Max(0, radius - TaskProofMark.HollowInset);
        dc.DrawEllipse(null, pen, at, inner, inner);
    }

    /// <summary>Neon's lit segment while unproven: a 1 DIP stroke inside its outline.</summary>
    public static void HollowSegment(DrawingContext dc, Rect segment, double cornerRadius, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var inset = TaskProofMark.HollowInset;
        if (segment.Width <= 2 * inset || segment.Height <= 2 * inset)
        {
            return;
        }

        var pen = new Pen(brush, TaskProofMark.HollowStroke);
        pen.Freeze();
        var inner = new Rect(segment.X + inset, segment.Y + inset, segment.Width - 2 * inset, segment.Height - 2 * inset);
        var corner = Math.Max(0, cornerRadius - inset);
        dc.DrawRoundedRectangle(null, pen, inner, corner, corner);
    }

    /// <summary>A pen that draws round dots along a path: dash 0, gap 2 × its thickness, round caps (glass's arc and outlines).</summary>
    public static Pen DottedPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            DashStyle = new DashStyle([0, 2], 0),
            DashCap = PenLineCap.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Point At(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
}
