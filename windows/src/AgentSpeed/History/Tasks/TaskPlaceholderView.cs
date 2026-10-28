using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// The re-analysing state's static placeholders (DESIGN 12, <i>States</i>): the side headers and five comparison rows,
/// in the track color, laid out where the real ones will appear so nothing jumps when the records fill in.
/// </summary>
internal sealed class TaskPlaceholderView : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(TaskPerformanceWidth), typeof(TaskPlaceholderView),
        new FrameworkPropertyMetadata(TaskPerformanceWidth.Wide, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double HeaderHeight = 110;
    private const double CardHeight = 96;
    private const double Rows = 5;

    public TaskPlaceholderView()
    {
        SnapsToDevicePixels = true;
    }

    public TaskPerformanceWidth Layout
    {
        get => (TaskPerformanceWidth)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = Layout == TaskPerformanceWidth.Narrow
            ? CardHeight + 12 + Rows * TaskPerformanceLayout.RowHeight
            : HeaderHeight + 7 + Rows * TaskPerformanceLayout.RowHeight;
        return new Size(0, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var brush = TaskAppearance.Brush(TaskAppearance.Track(this));
        if (Layout == TaskPerformanceWidth.Narrow)
        {
            RenderNarrow(dc, brush, width);
        }
        else
        {
            RenderButterfly(dc, brush, width);
        }
    }

    private static void Bar(DrawingContext dc, Brush brush, double x, double y, double width, double height)
    {
        if (width <= 0)
        {
            return;
        }

        var radius = Math.Min(height / 2, 4);
        dc.DrawRoundedRectangle(brush, null, new Rect(x, y, width, height), radius, radius);
    }

    private void RenderButterfly(DrawingContext dc, Brush brush, double width)
    {
        var value = TaskPerformanceLayout.ValueColumn;
        var center = TaskPerformanceLayout.CenterColumn(Layout);
        var track = Math.Max(0, (width - 2 * value - 4 * TaskPairView.ColumnGap - center) / 2);
        var sideRight = value + TaskPairView.ColumnGap + track;
        var sideLeft = sideRight + 2 * TaskPairView.ColumnGap + center;
        var histogram = TaskPerformanceLayout.HistogramWidth(Layout);

        // Side headers, each aligned toward the centre: name, counts, then the distribution and the median.
        Bar(dc, brush, sideRight - 64, 4, 64, 10);
        Bar(dc, brush, sideRight - 132, 26, 132, 8);
        Bar(dc, brush, sideRight - 44, 52, 44, 30);
        Bar(dc, brush, sideRight - 44 - 12 - histogram, 62, histogram, 20);
        Bar(dc, brush, sideLeft, 4, 64, 10);
        Bar(dc, brush, sideLeft, 26, 132, 8);
        Bar(dc, brush, sideLeft, 52, 44, 30);
        Bar(dc, brush, sideLeft + 44 + 12, 62, histogram, 20);

        dc.DrawRectangle(brush, null, new Rect(0, HeaderHeight, width, 1));
        var top = HeaderHeight + 7;
        for (var i = 0; i < Rows; i++)
        {
            var mid = top + i * TaskPerformanceLayout.RowHeight + TaskPerformanceLayout.RowHeight / 2;
            Bar(dc, brush, value - 30, mid - 4, 30, 8);
            Bar(dc, brush, value + TaskPairView.ColumnGap, mid - 3, track, 6);
            var label = Math.Min(center - 24, 76);
            Bar(dc, brush, sideRight + TaskPairView.ColumnGap + (center - label) / 2, mid - 4, label, 8);
            Bar(dc, brush, sideLeft, mid - 3, track, 6);
            Bar(dc, brush, width - value, mid - 4, 30, 8);
        }
    }

    private void RenderNarrow(DrawingContext dc, Brush brush, double width)
    {
        const double gap = 8;
        var card = Math.Max(0, (width - gap) / 2);
        var faint = TaskAppearance.Brush(TaskAppearance.WithOpacity(TaskAppearance.Track(this), 0.5));
        foreach (var x in new[] { 0, card + gap })
        {
            var radius = TaskPerformanceLayout.NarrowCardRadius;
            dc.DrawRoundedRectangle(faint, null, new Rect(x, 0, card, CardHeight), radius, radius);
            var pad = TaskPerformanceLayout.NarrowCardPadding;
            Bar(dc, brush, x + pad, pad + 2, Math.Min(64, card - 2 * pad), 9);
            Bar(dc, brush, x + pad, pad + 22, Math.Min(36, card - 2 * pad), 22);
            Bar(dc, brush, x + pad, pad + 52, Math.Min(96, card - 2 * pad), 7);
            Bar(dc, brush, x + pad, CardHeight - pad - 14, card - 2 * pad, 14);
        }

        var top = CardHeight + 12;
        var column = TaskPerformanceLayout.NarrowValueColumn;
        for (var i = 0; i < Rows; i++)
        {
            var mid = top + i * TaskPerformanceLayout.RowHeight + TaskPerformanceLayout.RowHeight / 2;
            Bar(dc, brush, 0, mid - 4, Math.Min(96, width - 2 * column), 8);
            Bar(dc, brush, width - column - 32, mid - 4, 32, 8);
            Bar(dc, brush, width - 32, mid - 4, 32, 8);
        }
    }
}
