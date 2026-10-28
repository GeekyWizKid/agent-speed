using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// The Usage History window's task performance section (DESIGN 12, <i>Usage History: task performance</i>). Its data
/// context is a <see cref="TaskPerformanceViewModel"/>; it reports its content width (which picks the wide, medium or
/// narrow layout), keeps the pair pickers in step, opens the ⓘ popover, and fades a new state in.
/// </summary>
internal sealed partial class TaskPerformanceSection : UserControl
{
    private const double WidePickerMax = 200;

    private TaskPerformanceViewModel? _model;
    private bool _syncingPickers;
    private bool? _narrowHeader;

    public TaskPerformanceSection()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as TaskPerformanceViewModel);
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            {
                _model?.SetContentWidth(e.NewSize.Width);
            }
        };
        PickerA.SelectionChanged += OnPicked;
        PickerB.SelectionChanged += OnPicked;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SectionPeer(this);

    private void Attach(TaskPerformanceViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        if (model is null)
        {
            return;
        }

        model.PropertyChanged += OnModelChanged;
        if (ActualWidth > 0)
        {
            model.SetContentWidth(ActualWidth);
        }

        SyncPickers();
        ApplyHeaderLayout();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TaskPerformanceViewModel.OptionsA):
            case nameof(TaskPerformanceViewModel.OptionsB):
            case nameof(TaskPerformanceViewModel.PickedA):
            case nameof(TaskPerformanceViewModel.PickedB):
                SyncPickers();
                break;
            case nameof(TaskPerformanceViewModel.IsNarrow):
                ApplyHeaderLayout();
                break;
            case nameof(TaskPerformanceViewModel.Mode):
                FadeIn();
                if (_model is { ShowsAnalyzing: true })
                {
                    Dispatcher.BeginInvoke(AnnounceAnalyzing, DispatcherPriority.Loaded);
                }

                break;
        }
    }

    // ---- Pickers ----

    private void SyncPickers()
    {
        if (_model is not { } model)
        {
            return;
        }

        _syncingPickers = true;
        try
        {
            if (!ReferenceEquals(PickerA.ItemsSource, model.OptionsA))
            {
                PickerA.ItemsSource = model.OptionsA;
            }

            if (!ReferenceEquals(PickerB.ItemsSource, model.OptionsB))
            {
                PickerB.ItemsSource = model.OptionsB;
            }

            PickerA.SelectedItem = model.PickedA;
            PickerB.SelectedItem = model.PickedB;
        }
        finally
        {
            _syncingPickers = false;
        }
    }

    private void OnPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPickers || sender is not ComboBox { SelectedItem: TaskSourceOption option } picker)
        {
            return;
        }

        // The new pair rebuilds both pickers' lists; not from inside the picker's own selection event.
        var first = ReferenceEquals(picker, PickerA);
        Dispatcher.BeginInvoke(() => _model?.Pick(first, option.Source), DispatcherPriority.Input);
    }

    /// <summary>Narrow: the pickers take a line of their own under the title, sharing it equally.</summary>
    private void ApplyHeaderLayout()
    {
        var narrow = _model?.IsNarrow ?? false;
        if (_narrowHeader == narrow)
        {
            return;
        }

        _narrowHeader = narrow;
        Grid.SetRow(Trailing, narrow ? 1 : 0);
        Grid.SetColumn(Trailing, narrow ? 0 : 2);
        Grid.SetColumnSpan(Trailing, narrow ? 3 : 1);
        Trailing.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Trailing.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(12, 0, 0, 0);
        var share = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        PickerAColumn.Width = share;
        PickerBColumn.Width = share;
        foreach (var picker in new[] { PickerA, PickerB })
        {
            picker.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            picker.MaxWidth = narrow ? double.PositiveInfinity : WidePickerMax;
        }

        OnlyOneText.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        OnlyOneText.TextAlignment = narrow ? TextAlignment.Left : TextAlignment.Right;
    }

    // ---- State ----

    /// <summary>A new state — records after re-analysing, or none any more — fades in (0.2 s), also with Reduce Motion.</summary>
    private void FadeIn()
    {
        if (!IsLoaded || !IsVisible)
        {
            return;
        }

        Body.BeginAnimation(OpacityProperty, TaskMotion.FadeTo(0, 1));
    }

    /// <summary>WPF raises no live-region event by itself: screen readers hear the re-analysing line once it shows.</summary>
    private void AnnounceAnalyzing()
    {
        if (AnalyzingLine.IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)
            && UIElementAutomationPeer.CreatePeerForElement(AnalyzingLine) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // ---- Info ----

    private void OnInfo(object sender, RoutedEventArgs e)
    {
        if (InfoFlyout.IsOpen)
        {
            InfoFlyout.IsOpen = false;
            return;
        }

        InfoScroller.ScrollToTop();
        InfoFlyout.IsOpen = true;
        Dispatcher.BeginInvoke(() => InfoScroller.Focus(), DispatcherPriority.Input);
    }

    private void OnInfoClosed(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this) is { IsActive: true } window && !window.IsKeyboardFocusWithin && InfoButton.IsVisible)
        {
            InfoButton.Focus();
        }
    }

    private void OnInfoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            InfoFlyout.IsOpen = false;
            InfoButton.Focus();
            e.Handled = true;
        }
    }

    /// <summary>The section is a group named <c>history.tasks.title</c>.</summary>
    private sealed class SectionPeer(TaskPerformanceSection owner) : UserControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(TaskPerformanceSection);
    }
}
