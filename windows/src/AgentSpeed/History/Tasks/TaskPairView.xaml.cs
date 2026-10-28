using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>The butterfly (wide and medium); its data context is the <see cref="TaskPerformanceViewModel"/>.</summary>
internal sealed partial class TaskPairView : UserControl
{
    /// <summary>The space between a value, its track and the centre (the prototype's column gap).</summary>
    public const double ColumnGap = 10;

    /// <summary>The centre column's width, inherited by every row's grid (140 wide, 120 medium).</summary>
    public static readonly DependencyProperty CenterWidthProperty = DependencyProperty.RegisterAttached(
        "CenterWidth", typeof(GridLength), typeof(TaskPairView),
        new FrameworkPropertyMetadata(new GridLength(TaskPerformanceLayout.CenterColumn(TaskPerformanceWidth.Wide)), FrameworkPropertyMetadataOptions.Inherits));

    public TaskPairView()
    {
        InitializeComponent();
    }

    /// <summary>The value columns at the outer edges (64 DIP).</summary>
    public static GridLength ValueColumn { get; } = new(TaskPerformanceLayout.ValueColumn);

    public static GridLength Gap { get; } = new(ColumnGap);

    public static GridLength GetCenterWidth(DependencyObject element) => (GridLength)element.GetValue(CenterWidthProperty);

    public static void SetCenterWidth(DependencyObject element, GridLength value) => element.SetValue(CenterWidthProperty, value);
}
