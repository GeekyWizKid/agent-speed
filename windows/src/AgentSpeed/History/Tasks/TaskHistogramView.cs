using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Theme;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// A side's score distribution (DESIGN 12, <i>Side headers</i>): ten bars, 2 DIP apart with top corners of 1.5 DIP, in
/// the source color at 70 % with the median's bin at 100 %, a 1 DIP median tick in the label color, a baseline, and
/// <c>0</c> and <c>100</c> under the ends. The axis always runs from 0 on the left to 100 on the right. Screen readers
/// see one image named with the quartiles and the median.
/// </summary>
internal sealed class TaskHistogramView : TaskMorphElement
{
    public static readonly DependencyProperty HistogramProperty = DependencyProperty.Register(
        nameof(Histogram), typeof(TaskHistogram), typeof(TaskHistogramView), new FrameworkPropertyMetadata(null, OnHistogramChanged));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(TaskHistogramView), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush), typeof(Brush), typeof(TaskHistogramView), new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
        nameof(AxisBrush), typeof(Brush), typeof(TaskHistogramView), new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineBrushProperty = DependencyProperty.Register(
        nameof(BaselineBrush), typeof(Brush), typeof(TaskHistogramView), new FrameworkPropertyMetadata(System.Windows.Media.Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TaskHistogramView),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Room above the plot for the median tick's overhang.</summary>
    private const double Overhang = 2;

    /// <summary>The space between the baseline and the axis labels.</summary>
    private const double AxisGap = 2;

    private State _from = State.Empty;
    private State _to = State.Empty;

    public TaskHistogram? Histogram
    {
        get => (TaskHistogram?)GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Brush TickBrush
    {
        get => (Brush)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public Brush AxisBrush
    {
        get => (Brush)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public Brush BaselineBrush
    {
        get => (Brush)GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    protected override bool HasContent => _to.Heights.Any(h => h > 0);

    protected override void CaptureCurrent() =>
        _from = Crossfading ? (Progress < 0.5 ? _from : _to) : State.Lerp(_from, _to, Progress);

    protected override void CaptureEmpty() => _from = _to with { Heights = new double[TaskStats.Bins] };

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : TaskPerformanceLayout.HistogramWidth(TaskPerformanceWidth.Wide);
        return new Size(width, Overhang + TaskPerformanceLayout.HistogramHeight + AxisGap + AxisLineHeight());
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var plot = TaskPerformanceLayout.HistogramHeight;
        var baseline = Overhang + plot;
        var highContrast = TaskAppearance.GetHighContrast(this);
        var baselinePen = MediaColors.FrozenPen(highContrast ? TaskAppearance.Brush(SystemColors.GrayTextColor) : BaselineBrush, 1);
        dc.DrawLine(baselinePen, new Point(0, Math.Round(baseline) + 0.5), new Point(width, Math.Round(baseline) + 0.5));

        var progress = Progress;
        if (Crossfading)
        {
            DrawState(dc, _from, width, baseline, 1 - progress);
            DrawState(dc, _to, width, baseline, progress);
        }
        else
        {
            DrawState(dc, State.Lerp(_from, _to, progress), width, baseline, 1);
        }

        var axis = Format("0");
        dc.DrawText(axis, new Point(0, baseline + AxisGap));
        var hundred = Format("100");
        dc.DrawText(hundred, new Point(width - hundred.WidthIncludingTrailingWhitespace, baseline + AxisGap));
    }

    private void DrawState(DrawingContext dc, State state, double width, double baseline, double opacity)
    {
        if (opacity <= 0.001)
        {
            return;
        }

        if (opacity < 0.999)
        {
            dc.PushOpacity(opacity);
        }

        var plot = TaskPerformanceLayout.HistogramHeight;
        var bin = TaskPerformanceLayout.BinWidth(width);
        var median = TaskAppearance.Brush(TaskAppearance.Bar(this, Color));
        var other = TaskAppearance.Brush(TaskAppearance.Bar(this, Color, TaskPerformanceLayout.HistogramOpacity));
        for (var i = 0; i < state.Heights.Length; i++)
        {
            var height = state.Heights[i] * plot;
            if (height <= 0.05)
            {
                continue;
            }

            height = Math.Max(TaskPerformanceLayout.HistogramMinBar, height);
            var x = i * (bin + TaskPerformanceLayout.HistogramGap);
            dc.DrawGeometry(i == state.MedianBin ? median : other, null, TopRounded(new Rect(x, baseline - height, bin, height)));
        }

        if (state.MedianAt is { } at)
        {
            var x = Math.Round(Math.Clamp(at, 0, 1) * width) + 0.5;
            x = Math.Clamp(x, 0.5, width - 0.5);
            dc.DrawLine(MediaColors.FrozenPen(TickBrush, 1), new Point(x, 0), new Point(x, baseline + 1));
        }

        if (opacity < 0.999)
        {
            dc.Pop();
        }
    }

    private static Geometry TopRounded(Rect rect)
    {
        var r = Math.Min(TaskPerformanceLayout.HistogramCorner, Math.Min(rect.Width / 2, rect.Height));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
            g.LineTo(new Point(rect.Left, rect.Top + r), false, false);
            g.ArcTo(new Point(rect.Left + r, rect.Top), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(rect.Right - r, rect.Top), false, false);
            g.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(rect.Right, rect.Bottom), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private double AxisLineHeight() => Format("0").Height;

    private FormattedText Format(string text) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), TaskPerformanceLayout.AxisSize,
        TaskAppearance.GetHighContrast(this) ? TaskAppearance.Brush(SystemColors.GrayTextColor) : AxisBrush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static void OnHistogramChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TaskHistogramView)d;
        var next = State.Of(e.NewValue as TaskHistogram);
        if (next.SameAs(view._to))
        {
            return;
        }

        view.BeginChange();
        view._to = next;
        view.EndChange();
    }

    /// <summary>Bar heights as fractions of the plot, the median's bin and the tick's place.</summary>
    private sealed record State(double[] Heights, int MedianBin, double? MedianAt)
    {
        public static State Empty { get; } = new(new double[TaskStats.Bins], -1, null);

        public static State Of(TaskHistogram? histogram)
        {
            if (histogram is null)
            {
                return Empty;
            }

            // Fractions of the plot; the minimum bar applies when drawing, at the plot's real height.
            var max = histogram.MaxCount;
            var heights = new double[TaskStats.Bins];
            for (var i = 0; i < heights.Length && i < histogram.Counts.Count; i++)
            {
                heights[i] = max > 0 && histogram.Counts[i] > 0 ? (double)histogram.Counts[i] / max : 0;
            }

            return new State(heights, histogram.MedianBin, histogram.MedianAt);
        }

        public static State Lerp(State from, State to, double t)
        {
            var heights = new double[TaskStats.Bins];
            for (var i = 0; i < heights.Length; i++)
            {
                heights[i] = Math.Max(0, from.Heights[i] + (to.Heights[i] - from.Heights[i]) * t);
            }

            double? at = (from.MedianAt, to.MedianAt) switch
            {
                ({ } a, { } b) => a + (b - a) * t,
                (_, { } b) => b,
                _ => null,
            };
            return new State(heights, to.MedianBin, at);
        }

        public bool SameAs(State other) =>
            MedianBin == other.MedianBin && MedianAt == other.MedianAt && Heights.AsSpan().SequenceEqual(other.Heights);
    }

    private sealed class Peer(TaskHistogramView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(TaskHistogramView);

        protected override string GetNameCore()
        {
            var name = AutomationProperties.GetName(owner);
            return string.IsNullOrEmpty(name) ? owner.Histogram?.Spoken ?? string.Empty : name;
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
