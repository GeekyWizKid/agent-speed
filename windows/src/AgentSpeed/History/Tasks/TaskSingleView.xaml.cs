using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>The one-source layout; its data context is the <see cref="TaskPerformanceViewModel"/>.</summary>
internal sealed partial class TaskSingleView : UserControl
{
    /// <summary>The label column, inherited by every row: 180 DIP, at most 40 % of a narrow section.</summary>
    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.RegisterAttached(
        "LabelWidth", typeof(GridLength), typeof(TaskSingleView),
        new FrameworkPropertyMetadata(new GridLength(TaskPerformanceLayout.SingleLabelColumn), FrameworkPropertyMetadataOptions.Inherits));

    private const double NarrowLabelShare = 0.4;

    public TaskSingleView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            var width = Math.Min(TaskPerformanceLayout.SingleLabelColumn, Math.Floor(e.NewSize.Width * NarrowLabelShare));
            if (GetLabelWidth(this).Value != width)
            {
                SetLabelWidth(this, new GridLength(Math.Max(0, width)));
            }
        };
    }

    public static GridLength GetLabelWidth(DependencyObject element) => (GridLength)element.GetValue(LabelWidthProperty);

    public static void SetLabelWidth(DependencyObject element, GridLength value) => element.SetValue(LabelWidthProperty, value);
}
