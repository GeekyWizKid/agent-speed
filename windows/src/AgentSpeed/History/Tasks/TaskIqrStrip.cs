using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// A model's interquartile strip (DESIGN 12, <i>By model</i>): a 48 × 4 DIP track, <c>q(0.25)</c>–<c>q(0.75)</c> in the
/// first source's color at 45 %, and a 1.5 DIP median tick in the label color reaching 2 DIP past the track.
/// </summary>
internal sealed class TaskIqrStrip : TaskMorphElement
{
    public static readonly DependencyProperty StripProperty = DependencyProperty.Register(
        nameof(Strip), typeof(TaskStrip?), typeof(TaskIqrStrip), new FrameworkPropertyMetadata(null, OnStripChanged));

    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush), typeof(Brush), typeof(TaskIqrStrip), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double TickOverhang = 2;
    private const double TickWidth = 1.5;

    private Range _from;
    private Range _to;
    private Color _color = Colors.Gray;

    public TaskStrip? Strip
    {
        get => (TaskStrip?)GetValue(StripProperty);
        set => SetValue(StripProperty, value);
    }

    public Brush TickBrush
    {
        get => (Brush)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    protected override bool HasContent => _to.Shown;

    protected override void CaptureCurrent() => _from = Crossfading ? (Progress < 0.5 ? _from : _to) : Range.Lerp(_from, _to, Progress);

    protected override void CaptureEmpty() => _from = _to with { Lower = _to.Median, Upper = _to.Median };

    protected override Size MeasureOverride(Size availableSize) =>
        new(TaskPerformanceLayout.StripWidth, TaskPerformanceLayout.StripHeight + 2 * TickOverhang);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var top = (ActualHeight - TaskPerformanceLayout.StripHeight) / 2;
        var height = TaskPerformanceLayout.StripHeight;
        var radius = height / 2;
        dc.DrawRoundedRectangle(TaskAppearance.Brush(TaskAppearance.Track(this)), null, new Rect(0, top, width, height), radius, radius);
        var progress = Progress;
        if (Crossfading)
        {
            DrawRange(dc, _from, width, top, 1 - progress);
            DrawRange(dc, _to, width, top, progress);
        }
        else
        {
            DrawRange(dc, Range.Lerp(_from, _to, progress), width, top, 1);
        }
    }

    private void DrawRange(DrawingContext dc, Range range, double width, double top, double opacity)
    {
        if (!range.Shown || opacity <= 0.001)
        {
            return;
        }

        if (opacity < 0.999)
        {
            dc.PushOpacity(opacity);
        }

        var height = TaskPerformanceLayout.StripHeight;
        var radius = height / 2;
        var left = Math.Clamp(range.Lower, 0, 1) * width;
        var right = Math.Clamp(range.Upper, 0, 1) * width;
        if (right - left > 0.25)
        {
            var fill = TaskAppearance.Brush(TaskAppearance.Bar(this, _color, TaskPerformanceLayout.StripOpacity));
            var r = Math.Min(radius, (right - left) / 2);
            dc.DrawRoundedRectangle(fill, null, new Rect(left, top, right - left, height), r, r);
        }

        var x = Math.Clamp(range.Median, 0, 1) * width;
        x = Math.Clamp(x, TickWidth / 2, width - TickWidth / 2);
        dc.DrawRoundedRectangle(TickBrush, null, new Rect(x - TickWidth / 2, top - TickOverhang, TickWidth, height + 2 * TickOverhang),
            TickWidth / 2, TickWidth / 2);
        if (opacity < 0.999)
        {
            dc.Pop();
        }
    }

    private static void OnStripChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TaskIqrStrip)d;
        var strip = e.NewValue as TaskStrip?;
        var next = strip is { } s ? new Range(true, s.Lower, s.Upper, s.Median) : default;
        if (strip is { } color)
        {
            view._color = TaskAppearance.SourceColor(color.Color);
        }

        if (next == view._to)
        {
            view.InvalidateVisual();
            return;
        }

        view.BeginChange();
        view._to = next;
        view.EndChange();
    }

    private readonly record struct Range(bool Shown, double Lower, double Upper, double Median)
    {
        public static Range Lerp(Range from, Range to, double t)
        {
            if (!from.Shown)
            {
                return to;
            }

            if (!to.Shown)
            {
                return default;
            }

            return new Range(true, from.Lower + (to.Lower - from.Lower) * t, from.Upper + (to.Upper - from.Upper) * t,
                from.Median + (to.Median - from.Median) * t);
        }
    }
}
