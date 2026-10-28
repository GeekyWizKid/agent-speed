using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.TaskScore;

/// <summary>A row that has a score card: the dashboard's agent rows (their view-models implement it).</summary>
internal interface IScoreCardRow
{
    /// <summary>Stable while the row shows the same agent.</summary>
    object CardKey { get; }

    /// <summary>The row's card now; null when it has none.</summary>
    TaskScoreCard? Card { get; }
}

/// <summary>
/// Gives a WPF row its score card (DESIGN 12, <i>Placement and timing</i>): hover shows it after the host's delay, Space
/// shows it at once, it follows the keyboard focus while shown and Esc hides it. The card opens beside the row's window,
/// level with the row. Set <see cref="PresenterProperty"/>, <see cref="HostProperty"/> and <see cref="SourceProperty"/>.
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
        if (GetSource(row)?.Card is not { } card || !row.IsVisible || PresentationSource.FromVisual(row) is null
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
        return new TaskCardRequest(card, frame, top, GetHost(row));
    }

    private static void OnAttach(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement row || (bool)row.GetValue(AttachedProperty))
        {
            return;
        }

        row.SetValue(AttachedProperty, true);
        row.MouseEnter += (_, _) => With(row, (presenter, key) => presenter.Hover(key, GetHost(row), () => Request(row)));
        row.MouseLeave += (_, _) => With(row, (presenter, key) => presenter.Leave(key));
        row.PreviewKeyDown += OnKeyDown;
        row.GotKeyboardFocus += (_, _) => With(row, (presenter, key) => presenter.Focus(key, GetHost(row), () => Request(row)));
        row.LostKeyboardFocus += (_, args) =>
        {
            if (args.NewFocus is DependencyObject next && GetPresenter(next) is { } same && ReferenceEquals(same, GetPresenter(row)))
            {
                return;
            }

            GetPresenter(row)?.Focus(null, GetHost(row), null);
        };
        row.Unloaded += (_, _) => With(row, (presenter, key) => presenter.Forget(key));
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement row || GetPresenter(row) is not { } presenter || GetSource(row) is not { } source)
        {
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && source.Card is not null)
        {
            presenter.ShowNow(source.CardKey, GetHost(row), () => Request(row));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && presenter.IsShown)
        {
            presenter.Hide();
            e.Handled = true;
        }
    }

    private static void With(FrameworkElement row, Action<ScoreCardPresenter, object> action)
    {
        if (GetPresenter(row) is { } presenter && GetSource(row) is { } source)
        {
            action(presenter, source.CardKey);
        }
    }
}
