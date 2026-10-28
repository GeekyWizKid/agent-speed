using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// A comparison bar (DESIGN 12, <i>Comparison rows</i>): a 6 DIP track, radius 3, filled in the source color from one
/// end — the centre of the butterfly — outward; on dark at 85 %. An empty value leaves the track empty.
/// </summary>
internal sealed class TaskCompareTrack : TaskMorphElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(TaskCompareTrack), new FrameworkPropertyMetadata(0.0, OnFractionChanged));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(TaskCompareTrack), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The fill starts at the right end (the butterfly's left side), else at the left.</summary>
    public static readonly DependencyProperty FromEndProperty = DependencyProperty.Register(
        nameof(FromEnd), typeof(bool), typeof(TaskCompareTrack), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _from;
    private double _to;

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool FromEnd
    {
        get => (bool)GetValue(FromEndProperty);
        set => SetValue(FromEndProperty, value);
    }

    protected override bool HasContent => _to > 0;

    protected override void CaptureCurrent() => _from = Crossfading ? (Progress < 0.5 ? _from : _to) : Lerp(_from, _to, Progress);

    protected override void CaptureEmpty() => _from = 0;

    protected override Size MeasureOverride(Size availableSize) => new(0, TaskPerformanceLayout.TrackHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = Math.Min(height / 2, 3);
        dc.DrawRoundedRectangle(TaskAppearance.Brush(TaskAppearance.Track(this)), null, new Rect(0, 0, width, height), radius, radius);
        var opacity = TaskAppearance.GetDark(this) ? TaskPerformanceLayout.DarkTrackOpacity : 1;
        var fill = TaskAppearance.Brush(TaskAppearance.Bar(this, Color, opacity));
        var progress = Progress;
        if (Crossfading)
        {
            DrawFill(dc, fill, _from, width, height, radius, 1 - progress);
            DrawFill(dc, fill, _to, width, height, radius, progress);
        }
        else
        {
            DrawFill(dc, fill, Lerp(_from, _to, progress), width, height, radius, 1);
        }
    }

    private void DrawFill(DrawingContext dc, Brush fill, double fraction, double width, double height, double radius, double opacity)
    {
        var length = Math.Clamp(fraction, 0, 1) * width;
        if (length < 0.25 || opacity <= 0.001)
        {
            return;
        }

        length = Math.Max(length, Math.Min(width, 2 * radius));
        var rect = FromEnd ? new Rect(width - length, 0, length, height) : new Rect(0, 0, length, height);
        if (opacity < 0.999)
        {
            dc.PushOpacity(opacity);
        }

        dc.DrawRoundedRectangle(fill, null, rect, radius, radius);
        if (opacity < 0.999)
        {
            dc.Pop();
        }
    }

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var track = (TaskCompareTrack)d;
        var target = e.NewValue is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        if (Math.Abs(target - track._to) < 1e-6)
        {
            return;
        }

        track.BeginChange();
        track._to = target;
        track.EndChange();
    }
}
