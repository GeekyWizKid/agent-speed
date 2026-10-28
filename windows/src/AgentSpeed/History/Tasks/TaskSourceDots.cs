using System.Windows;
using System.Windows.Media;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// Source dots (DESIGN 12): one dot, or several overlapping 4 DIP apart with the first in front. Each dot in front is cut
/// out of the one behind it with a 1 DIP ring, so the ring takes the row's color whatever lies under it.
/// </summary>
internal sealed class TaskSourceDots : FrameworkElement
{
    public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
        nameof(Sources), typeof(IReadOnlyList<SourceKind>), typeof(TaskSourceDots),
        new FrameworkPropertyMetadata(Array.Empty<SourceKind>(), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotSizeProperty = DependencyProperty.Register(
        nameof(DotSize), typeof(double), typeof(TaskSourceDots),
        new FrameworkPropertyMetadata(TaskPerformanceLayout.ModelDot, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Ring = 1;

    public TaskSourceDots()
    {
        SnapsToDevicePixels = true;
    }

    public IReadOnlyList<SourceKind> Sources
    {
        get => (IReadOnlyList<SourceKind>)GetValue(SourcesProperty);
        set => SetValue(SourcesProperty, value);
    }

    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Sources?.Count ?? 0;
        return count == 0 ? default : new Size(DotSize + TaskPerformanceLayout.ModelDotStep * (count - 1), DotSize);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var sources = Sources;
        if (sources is null || sources.Count == 0)
        {
            return;
        }

        var radius = DotSize / 2;
        var y = ActualHeight / 2;
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            var center = new Point(radius + i * TaskPerformanceLayout.ModelDotStep, y);
            Geometry dot = new EllipseGeometry(center, radius, radius);
            if (i > 0)
            {
                var front = new Point(center.X - TaskPerformanceLayout.ModelDotStep, y);
                dot = new CombinedGeometry(GeometryCombineMode.Exclude, dot, new EllipseGeometry(front, radius + Ring, radius + Ring));
            }

            dot.Freeze();
            var color = TaskAppearance.Dot(this, TaskAppearance.SourceColor(sources[i]));
            dc.DrawGeometry(TaskAppearance.Brush(color), null, dot);
        }
    }
}
