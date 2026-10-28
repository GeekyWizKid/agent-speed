using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>A side header of the butterfly or of the one-source layout; its data context is a <see cref="TaskSideView"/>.</summary>
internal sealed partial class TaskSideHeader : UserControl
{
    /// <summary>The butterfly's left side: everything right-aligned toward the centre, the dot after the name.</summary>
    public static readonly DependencyProperty MirroredProperty = DependencyProperty.Register(
        nameof(Mirrored), typeof(bool), typeof(TaskSideHeader), new PropertyMetadata(false, (d, _) => ((TaskSideHeader)d).ApplyAlignment()));

    public static readonly DependencyProperty HistogramWidthProperty = DependencyProperty.Register(
        nameof(HistogramWidth), typeof(double), typeof(TaskSideHeader),
        new PropertyMetadata(TaskPerformanceLayout.HistogramWidth(TaskPerformanceWidth.Wide), (d, e) => ((TaskSideHeader)d).Distribution.Width = (double)e.NewValue));

    public TaskSideHeader()
    {
        InitializeComponent();
        Distribution.Width = HistogramWidth;
        ApplyAlignment();
        Loaded += (_, _) => AlignCaption();
    }

    public bool Mirrored
    {
        get => (bool)GetValue(MirroredProperty);
        set => SetValue(MirroredProperty, value);
    }

    public double HistogramWidth
    {
        get => (double)GetValue(HistogramWidthProperty);
        set => SetValue(HistogramWidthProperty, value);
    }

    private void ApplyAlignment()
    {
        var mirrored = Mirrored;
        var toward = mirrored ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var text = mirrored ? TextAlignment.Right : TextAlignment.Left;
        foreach (var element in new FrameworkElement[] { Stack, NameRow, CountsRow, MedianRow, EmptyLine })
        {
            element.HorizontalAlignment = toward;
        }

        EmptyLine.TextAlignment = text;
        Grid.SetColumn(Dot, mirrored ? 2 : 0);
        Grid.SetColumn(NameText, mirrored ? 0 : 2);
        Grid.SetColumn(MedianBlock, mirrored ? 2 : 0);
        Grid.SetColumn(Distribution, mirrored ? 0 : 2);
        MedianBlock.HorizontalAlignment = toward;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == System.Windows.Documents.TextElement.FontFamilyProperty && CaptionText is not null)
        {
            AlignCaption();
        }
    }

    private void AlignCaption() => CaptionText.Margin = TaskBaseline.CaptionMargin(MedianText.FontFamily, MedianText.LineHeight,
        CaptionText.FontFamily, CaptionText.FontSize, 5);
}
