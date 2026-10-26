using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Boards;

namespace AgentSpeed.Settings.BoardsPane;

/// <summary>
/// A still miniature of a board style for the Boards list and the style cards (DESIGN 2c Styles; colors from the
/// prototypes in spec/prototypes/board): glass's frosted panel with its aurora and sparkline, timing's slab with the heat
/// rail and position blocks, neon's deck with its tube edge and LED bar; light or dark with the app. Drawn once per change.
/// </summary>
internal sealed class BoardThumbnail : FrameworkElement
{
    public static readonly DependencyProperty StyleKindProperty = DependencyProperty.Register(nameof(StyleKind), typeof(BoardStyle), typeof(BoardThumbnail),
        new FrameworkPropertyMetadata(BoardStyle.Neon, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(nameof(Dark), typeof(bool), typeof(BoardThumbnail),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(nameof(Side), typeof(double), typeof(BoardThumbnail),
        new FrameworkPropertyMetadata(38.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Color Gold = Rgb(0xFA, 0xCC, 0x15);
    private static readonly Color Mint = Rgb(0x34, 0xD3, 0x99);
    private static readonly Color Amber = Rgb(0xF5, 0xA5, 0x24);
    private static readonly Color Red = Rgb(0xEF, 0x44, 0x44);
    private static readonly Color Cyan = Rgb(0x22, 0xD3, 0xEE);
    private static readonly Color Magenta = Rgb(0xE8, 0x5D, 0xF5);

    public BoardStyle StyleKind { get => (BoardStyle)GetValue(StyleKindProperty); set => SetValue(StyleKindProperty, value); }

    public bool Dark { get => (bool)GetValue(DarkProperty); set => SetValue(DarkProperty, value); }

    public double Side { get => (double)GetValue(SideProperty); set => SetValue(SideProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Side, Side);

    protected override void OnRender(DrawingContext dc)
    {
        var side = Side;
        var radius = BoardMetrics.For(StyleKind, BoardSize.M).Radius / BoardSizes.Side(BoardSize.M) * side;
        var box = new Rect(0.5, 0.5, side - 1, side - 1);
        var u = side / 64;
        switch (StyleKind)
        {
            case BoardStyle.Glass:
                DrawGlass(dc, box, radius, u);
                break;
            case BoardStyle.Timing:
                DrawTiming(dc, box, radius, u);
                break;
            default:
                DrawNeon(dc, box, radius, u);
                break;
        }
    }

    private void DrawGlass(DrawingContext dc, Rect box, double radius, double u)
    {
        var stage = Dark
            ? Gradient(Rgb(0x0D, 0x10, 0x20), Rgb(0x1B, 0x10, 0x30), Rgb(0x08, 0x15, 0x1A))
            : Gradient(Rgb(0xDF, 0xE6, 0xF3), Rgb(0xF3, 0xE2, 0xEE), Rgb(0xDC, 0xEF, 0xE9));
        var glass = Dark ? Argb(0.58, 20, 22, 33) : Argb(0.62, 252, 252, 255);
        var edge = Dark ? Argb(0.13, 255, 255, 255) : Argb(0.08, 0, 0, 0);
        dc.DrawRoundedRectangle(stage, null, box, radius, radius);
        dc.PushClip(new RectangleGeometry(box, radius, radius));
        var aurora = new RadialGradientBrush(Argb(Dark ? 0.55 : 0.35, 0x8B, 0x5C, 0xF6), Colors.Transparent)
        {
            Center = new Point(0.3, 0.2), GradientOrigin = new Point(0.3, 0.2), RadiusX = 0.6, RadiusY = 0.45,
        };
        aurora.Freeze();
        dc.DrawRectangle(aurora, null, box);
        var teal = new RadialGradientBrush(Argb(Dark ? 0.4 : 0.3, 0x2D, 0xD4, 0xBF), Colors.Transparent)
        {
            Center = new Point(0.78, 0.25), GradientOrigin = new Point(0.78, 0.25), RadiusX = 0.45, RadiusY = 0.35,
        };
        teal.Freeze();
        dc.DrawRectangle(teal, null, box);
        dc.DrawRoundedRectangle(Frozen(glass), null, box, radius, radius);
        var spark = new StreamGeometry();
        using (var g = spark.Open())
        {
            double[] ys = [0.62, 0.55, 0.58, 0.44, 0.48, 0.36, 0.40, 0.30];
            g.BeginFigure(new Point(box.X + 8 * u, box.Y + 26 * u * ys[0] + 4 * u), false, false);
            for (var i = 1; i < ys.Length; i++)
            {
                g.LineTo(new Point(box.X + (8 + i * 6.8) * u, box.Y + 26 * u * ys[i] + 4 * u), true, true);
            }
        }

        spark.Freeze();
        dc.DrawGeometry(null, Pen(Amber, 1.4 * u), spark);
        Rows(dc, box, u, Dark ? Argb(0.9, 244, 246, 252) : Argb(0.85, 17, 20, 30), Mint, 0.09);
        dc.Pop();
        dc.DrawRoundedRectangle(null, Pen(edge, 1), box, radius, radius);
    }

    private void DrawTiming(DrawingContext dc, Rect box, double radius, double u)
    {
        var slab = Dark ? Rgb(0x0C, 0x0E, 0x14) : Rgb(0xFB, 0xFB, 0xFD);
        var block = Dark ? Rgb(0x1B, 0x1F, 0x2A) : Rgb(0xE6, 0xE9, 0xF0);
        var ink = Dark ? Argb(0.9, 245, 247, 251) : Argb(0.88, 14, 17, 24);
        var edge = Dark ? Argb(0.09, 255, 255, 255) : Argb(0.08, 0, 0, 0);
        dc.DrawRoundedRectangle(Frozen(slab), null, box, radius, radius);
        dc.PushClip(new RectangleGeometry(box, radius, radius));
        var rail = new LinearGradientBrush(new GradientStopCollection
        {
            new(Rgb(0x22, 0xC5, 0x5E), 0), new(Amber, 0.55), new(Red, 1),
        }, 0);
        rail.Freeze();
        dc.DrawRectangle(rail, null, new Rect(box.X, box.Y, box.Width * 0.72, 2.2 * u));
        dc.DrawEllipse(Frozen(Red), null, new Point(box.Right - 9 * u, box.Y + 9 * u), 2 * u, 2 * u);
        dc.DrawRoundedRectangle(Frozen(ink), null, new Rect(box.X + 8 * u, box.Y + 9 * u, 24 * u, 7 * u), 1.5 * u, 1.5 * u);
        for (var i = 0; i < 3; i++)
        {
            var y = box.Y + (28 + i * 11) * u;
            dc.DrawRoundedRectangle(Frozen(i == 0 ? Gold : block), null, new Rect(box.X + 7 * u, y, 7 * u, 7 * u), 1.2 * u, 1.2 * u);
            dc.DrawRoundedRectangle(Frozen(Fade(ink, 0.7 - i * 0.15)), null, new Rect(box.X + 17 * u, y + 1.5 * u, (26 - i * 4) * u, 4 * u), 2 * u, 2 * u);
            dc.DrawRoundedRectangle(Frozen(Fade(ink, 0.45)), null, new Rect(box.Right - 15 * u, y + 1.5 * u, 8 * u, 4 * u), 2 * u, 2 * u);
        }

        dc.Pop();
        dc.DrawRoundedRectangle(null, Pen(edge, 1), box, radius, radius);
    }

    private void DrawNeon(DrawingContext dc, Rect box, double radius, double u)
    {
        var deck = Dark ? Gradient(Rgb(0x05, 0x06, 0x0C), Rgb(0x0A, 0x0D, 0x18), Rgb(0x05, 0x06, 0x0C))
            : Gradient(Rgb(0xF7, 0xF8, 0xFC), Rgb(0xEC, 0xEF, 0xF7), Rgb(0xF7, 0xF8, 0xFC));
        var unlit = Dark ? Argb(0.16, 140, 170, 255) : Argb(0.14, 20, 30, 80);
        var ink = Dark ? Argb(0.9, 243, 246, 255) : Argb(0.88, 10, 13, 24);
        dc.DrawRoundedRectangle(deck, null, box, radius, radius);
        dc.PushClip(new RectangleGeometry(box, radius, radius));
        for (var i = 0; i < 12; i++)
        {
            var lit = i < 8;
            var color = lit ? Lerp(Cyan, Magenta, i / 11.0) : unlit;
            dc.DrawRoundedRectangle(Frozen(color), null, new Rect(box.X + (8 + i * 4) * u, box.Y + 20 * u, 3 * u, 3.2 * u), 0.6 * u, 0.6 * u);
        }

        dc.DrawRoundedRectangle(Frozen(ink), null, new Rect(box.X + 8 * u, box.Y + 9 * u, 22 * u, 7 * u), 1.5 * u, 1.5 * u);
        Rows(dc, box, u, ink, Mint, 0.06);
        dc.Pop();
        var tube = new LinearGradientBrush(Cyan, Magenta, new Point(0, 0), new Point(1, 1));
        tube.Freeze();
        if (Dark)
        {
            dc.DrawRoundedRectangle(null, new Pen(Frozen(Fade(Cyan, 0.25)), 3.2 * u), box, radius, radius);
        }

        dc.DrawRoundedRectangle(null, new Pen(tube, 1.3 * u), box, radius, radius);
    }

    /// <summary>Three ranking rows: the leader's gold rank dot, names fading down the list and a share underline.</summary>
    private static void Rows(DrawingContext dc, Rect box, double u, Color ink, Color share, double fill)
    {
        for (var i = 0; i < 3; i++)
        {
            var y = box.Y + (30 + i * 10.5) * u;
            dc.DrawRoundedRectangle(Frozen(Fade(ink, fill)), null, new Rect(box.X + 5 * u, y - 2.5 * u, box.Width - 10 * u, 8.5 * u), 2 * u, 2 * u);
            dc.DrawEllipse(Frozen(i == 0 ? Gold : Fade(ink, 0.35)), null, new Point(box.X + 10 * u, y + 1.7 * u), 1.8 * u, 1.8 * u);
            dc.DrawRoundedRectangle(Frozen(Fade(ink, 0.75 - i * 0.18)), null, new Rect(box.X + 15 * u, y, (24 - i * 5) * u, 3.4 * u), 1.7 * u, 1.7 * u);
            dc.DrawRectangle(Frozen(Fade(share, 0.8 - i * 0.2)), null, new Rect(box.X + 15 * u, y + 4.6 * u, (30 - i * 9) * u, 0.9 * u));
        }
    }

    private static LinearGradientBrush Gradient(Color a, Color b, Color c)
    {
        var brush = new LinearGradientBrush(new GradientStopCollection { new(a, 0), new(b, 0.55), new(c, 1) }, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(Color color, double width)
    {
        var pen = new Pen(Frozen(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Argb(double a, byte r, byte g, byte b) => Color.FromArgb((byte)Math.Round(a * 255), r, g, b);

    private static Color Fade(Color color, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * color.A), color.R, color.G, color.B);

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
