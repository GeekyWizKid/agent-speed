using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// A block a screen reader reads as one element named by <c>AutomationProperties.Name</c>, which says everything its
/// texts show (DESIGN 12, <i>Accessibility</i>): a comparison row is one text; a side header is one group whose only
/// child is its distribution image.
/// </summary>
internal sealed class TaskAutomationBlock : Border
{
    /// <summary>A group that keeps its images (a side header); otherwise a single text.</summary>
    public static readonly DependencyProperty IsGroupProperty = DependencyProperty.Register(
        nameof(IsGroup), typeof(bool), typeof(TaskAutomationBlock), new PropertyMetadata(false));

    public bool IsGroup
    {
        get => (bool)GetValue(IsGroupProperty);
        set => SetValue(IsGroupProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(TaskAutomationBlock owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            owner.IsGroup ? AutomationControlType.Group : AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(TaskAutomationBlock);

        protected override List<AutomationPeer>? GetChildrenCore()
        {
            if (!owner.IsGroup)
            {
                return null;
            }

            var images = base.GetChildrenCore()?.Where(p => p is FrameworkElementAutomationPeer { Owner: TaskHistogramView }).ToList();
            return images is { Count: > 0 } ? images : null;
        }

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
