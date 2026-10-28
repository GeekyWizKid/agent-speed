using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>A narrow side card; its data context is a <see cref="TaskSideView"/>. High contrast outlines it instead of tinting it.</summary>
internal sealed partial class TaskSideCard : UserControl
{
    public TaskSideCard()
    {
        InitializeComponent();
        Loaded += (_, _) => AlignCaption();
    }

    public static CornerRadius CardRadius { get; } = new(TaskPerformanceLayout.NarrowCardRadius);

    public static Thickness CardPadding { get; } = new(TaskPerformanceLayout.NarrowCardPadding);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == System.Windows.Documents.TextElement.FontFamilyProperty && CaptionText is not null)
        {
            AlignCaption();
        }

        if (e.Property == TaskAppearance.HighContrastProperty && Fill is not null)
        {
            var highContrast = (bool)e.NewValue;
            Fill.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
            Outline.Visibility = highContrast ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AlignCaption() => CaptionText.Margin = TaskBaseline.CaptionMargin(MedianText.FontFamily, MedianText.LineHeight,
        CaptionText.FontFamily, CaptionText.FontSize, 5);
}
