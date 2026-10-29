using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>The narrow pair layout; its data context is the <see cref="TaskPerformanceViewModel"/>.</summary>
internal sealed partial class TaskPairNarrowView : UserControl
{
    public TaskPairNarrowView()
    {
        InitializeComponent();
    }

    /// <summary>The two value columns (72 DIP).</summary>
    public static GridLength ValueColumn { get; } = new(TaskPerformanceLayout.NarrowValueColumn);
}
