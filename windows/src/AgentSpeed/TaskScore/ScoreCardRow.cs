using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.TaskScore;

/// <summary>A row that has a score card: the dashboard's agent rows (their view-models implement it).</summary>
internal interface IScoreCardRow
{
    /// <summary>The agent the row shows now: the card crossfades when another agent takes the row.</summary>
    object CardKey { get; }

    /// <summary>The row's card now; null when it has none.</summary>
    TaskScoreCard? Card { get; }
}

/// <summary>
/// Gives a WPF row its score card (DESIGN 12, <i>Placement and timing</i>): hover shows it after the host's delay, Space
/// shows it at once, it follows the keyboard focus while shown and Esc hides it. The card opens beside the row's window,
/// level with the row. Set <see cref="PresenterProperty"/>, <see cref="HostProperty"/> and <see cref="SourceProperty"/>.
/// The row becomes focusable, its text tooltip gives way to the card, and its help text carries the card's content for
/// screen readers (WPF has no full description; call <see cref="Describe"/> when the row's card changes).
/// </summary>
internal static class ScoreCardRow
{
    public static readonly DependencyProperty PresenterProperty = DependencyProperty.RegisterAttached(
        "Presenter", typeof(ScoreCardPresenter), typeof(ScoreCardRow), new PropertyMetadata(null, OnAttach));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(IScoreCardRow), typeof(ScoreCardRow), new PropertyMetadata(null, OnAttach));

    public static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached(
        "Host", typeof(TaskCardHost), typeof(ScoreCardRow), new PropertyMetadata(TaskCardHost.Dashboard));

    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "Attached", typeof(bool), typeof(ScoreCardRow), new PropertyMetadata(false));

    public static ScoreCardPresenter? GetPresenter(DependencyObject element) => (ScoreCardPresenter?)element.GetValue(PresenterProperty);

    public static void SetPresenter(DependencyObject element, ScoreCardPresenter? value) => element.SetValue(PresenterProperty, value);

    public static IScoreCardRow? GetSource(DependencyObject element) => (IScoreCardRow?)element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, IScoreCardRow? value) => element.SetValue(SourceProperty, value);

    public static TaskCardHost GetHost(DependencyObject element) => (TaskCardHost)element.GetValue(HostProperty);

    public static void SetHost(DependencyObject element, TaskCardHost value) => element.SetValue(HostProperty, value);

    /// <summary>The request for <paramref name="row"/>'s card: beside its window, level with its top, in screen pixels.</summary>
    public static TaskCardRequest? Request(FrameworkElement row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (GetSource(row) is not { Card: { } card } source || !row.IsVisible || PresentationSource.FromVisual(row) is null
            || Window.GetWindow(row) is not { } window)
        {
            return null;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !User32.GetWindowRect(hwnd, out var frame))
        {
            return null;
        }

        var top = (int)Math.Round(row.PointToScreen(new Point(0, 0)).Y);
        return new TaskCardRequest(card, source.CardKey, frame, top, GetHost(row));
    }

    /// <summary>Puts the row's card text into its help text (DESIGN 12, <i>Accessibility</i>: the card is the row's extra content).</summary>
    public static void Describe(FrameworkElement row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var text = GetSource(row)?.Card?.FullDescription ?? string.Empty;
        if (!string.Equals(AutomationProperties.GetHelpText(row), text, StringComparison.Ordinal))
        {
            AutomationProperties.SetHelpText(row, text);
        }
    }

    private static void OnAttach(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement row)
        {
            return;
        }

        Describe(row);
        if ((bool)row.GetValue(AttachedProperty))
        {
            return;
        }

        row.SetValue(AttachedProperty, true);
        row.Focusable = true;
        ToolTipService.SetIsEnabled(row, false);

        // The key is the row element: the agent under a reused row may change while the pointer rests on it.
        row.MouseEnter += (_, _) => With(row, presenter => presenter.Hover(row, GetHost(row), () => Request(row)));
        row.MouseLeave += (_, _) => With(row, presenter => presenter.Leave(row));
        row.PreviewKeyDown += OnKeyDown;
        row.GotKeyboardFocus += (_, _) =>
        {
            Describe(row);
            With(row, presenter => presenter.Focus(row, GetHost(row), () => Request(row)));
        };
        row.LostKeyboardFocus += (_, args) =>
        {
            if (args.NewFocus is DependencyObject next && GetPresenter(next) is { } same && ReferenceEquals(same, GetPresenter(row)))
            {
                return;
            }

            GetPresenter(row)?.Focus(null, GetHost(row), null);
        };
        row.Unloaded += (_, _) => With(row, presenter => presenter.Forget(row));
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement row || GetPresenter(row) is not { } presenter || GetSource(row) is not { } source)
        {
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && source.Card is not null)
        {
            presenter.ShowNow(row, GetHost(row), () => Request(row));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && presenter.IsShown)
        {
            presenter.Hide();
            e.Handled = true;
        }
    }

    private static void With(FrameworkElement row, Action<ScoreCardPresenter> action)
    {
        if (GetPresenter(row) is { } presenter && GetSource(row) is not null)
        {
            action(presenter);
        }
    }
}
