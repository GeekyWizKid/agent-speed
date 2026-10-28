using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Theme;

namespace AgentSpeed.TaskScore;

/// <summary>What a score card is shown for: the card, the panel or board it sits beside and the row's top, in screen pixels.</summary>
/// <param name="Anchor">The dashboard flyout's or the board's frame.</param>
/// <param name="RowTop">The row's top (the hero: the dial's), which the card's top is level with.</param>
internal sealed record TaskCardRequest(TaskScoreCard Card, RECT Anchor, int RowTop, TaskCardHost Host);

/// <summary>
/// The score card's window (DESIGN 12, <i>Window</i>): 300 DIP wide and as tall as its content, in the flyout style of
/// the unknown-prices popover — the flyout fill (solid with transparency effects off), radius 8, a 1 px stroke and a soft
/// shadow. A tooltip: it never takes focus and the pointer passes through it. It appears with a 0.15 s fade and a 2 DIP
/// slide away from the row (Reduce Motion: the fade only) and switches rows with a 0.12 s crossfade.
/// </summary>
internal sealed class ScoreCardWindow : Window
{
    private const double ShadowMargin = 14;
    private const double HideSeconds = 0.1;

    private readonly Border _card;
    private readonly Grid _stack = new();
    private readonly TranslateTransform _slide = new();
    private ScoreCardView _front = new();
    private ScoreCardView _back = new() { Opacity = 0, Visibility = Visibility.Collapsed };
    private TaskCardRequest? _request;
    private TaskCardAppearance _appearance;
    private TaskCardSpot? _spot;

    public ScoreCardWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        IsHitTestVisible = false;
        Style = new Style(typeof(Window));
        Left = -32000;
        Top = -32000;
        Title = "Agent Speed";
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, AppFonts.UiFontKey);

        _stack.Children.Add(_back);
        _stack.Children.Add(_front);
        _card = new Border
        {
            Width = TaskCardPlacement.Width,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(ScoreCardView.CardPadding - 1),
            Margin = new Thickness(ShadowMargin),
            Child = _stack,
            RenderTransform = _slide,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.2, Color = Colors.Black },
        };
        Content = _card;

        SourceInitialized += (_, _) => WindowEffects.AddExtendedStyle(new WindowInteropHelper(this).Handle,
            WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE | WS_EX.TRANSPARENT);
        SizeChanged += (_, _) => Reposition();
    }

    /// <summary>Carries out <paramref name="action"/> for <paramref name="request"/>; <see cref="TaskCardAction.None"/> updates the open card in place.</summary>
    public void Present(TaskCardAction action, TaskCardRequest request, TaskCardAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(request);
        var restyle = appearance != _appearance || !IsVisible;
        _appearance = appearance;
        _request = request;
        if (restyle)
        {
            Paint();
        }

        switch (action)
        {
            case TaskCardAction.Show:
                _back.Visibility = Visibility.Collapsed;
                _front.Apply(request.Card, appearance, sameRow: false);
                Appear();
                break;
            case TaskCardAction.Switch when IsVisible:
                (_front, _back) = (_back, _front);
                _front.Apply(request.Card, appearance, sameRow: false);
                Crossfade();
                break;
            case TaskCardAction.Switch:
                _front.Apply(request.Card, appearance, sameRow: false);
                Appear();
                break;
            default:
                _front.Apply(request.Card, appearance, sameRow: true);
                break;
        }

        Reposition();
    }

    public void Dismiss()
    {
        _request = null;
        if (!IsVisible)
        {
            return;
        }

        if (_appearance.ReduceMotion)
        {
            HideNow();
            return;
        }

        var fade = new DoubleAnimation(0, Duration(HideSeconds)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if (_request is null)
            {
                HideNow();
            }
        };
        _card.BeginAnimation(OpacityProperty, fade);
        _card.Opacity = 0;
    }

    private static Duration Duration(double seconds) => new(TimeSpan.FromSeconds(seconds));

    private void HideNow()
    {
        _card.BeginAnimation(OpacityProperty, null);
        Hide();
    }

    private void Paint()
    {
        if (_appearance.Solid)
        {
            _card.Background = _appearance.SolidFill;
        }
        else
        {
            _card.SetResourceReference(Border.BackgroundProperty, "FlyoutBackground");
        }

        if (_appearance.HighContrast)
        {
            _card.BorderBrush = SystemColors.WindowTextBrush;
            _card.Effect = null;
        }
        else
        {
            _card.SetResourceReference(Border.BorderBrushProperty, "FlyoutBorderBrush");
            _card.Effect ??= new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.2, Color = Colors.Black };
        }
    }

    private void Appear()
    {
        _front.Opacity = 1;
        _front.Visibility = Visibility.Visible;
        _back.Visibility = Visibility.Collapsed;
        if (!IsVisible)
        {
            _card.Opacity = 0;
            Show();
        }

        Reposition();
        var fade = _appearance.ReduceMotion ? 0.15 : TaskCardPlacement.FadeSeconds;
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Duration(fade)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _slide.X = 0;
        if (!_appearance.ReduceMotion && _spot is { } spot)
        {
            _slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(spot.SlideFrom, 0, Duration(TaskCardPlacement.FadeSeconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    /// <summary>The old card fades out over the new one, which fades in, in 0.12 s; the window moves to the new row at once.</summary>
    private void Crossfade()
    {
        _card.BeginAnimation(OpacityProperty, null);
        _card.Opacity = 1;
        _front.Visibility = Visibility.Visible;
        _back.Visibility = Visibility.Visible;
        var duration = Duration(TaskCardPlacement.SwitchSeconds);
        var outgoing = _back;
        var fadeOut = new DoubleAnimation(1, 0, duration);
        fadeOut.Completed += (_, _) =>
        {
            if (!ReferenceEquals(outgoing, _front))
            {
                outgoing.Visibility = Visibility.Collapsed;
            }
        };
        outgoing.BeginAnimation(OpacityProperty, fadeOut);
        _front.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        Panel.SetZIndex(_front, 1);
        Panel.SetZIndex(_back, 0);
    }

    private void Reposition()
    {
        if (!IsVisible || _request is not { } request)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var monitor = Monitors.FromRect(request.Anchor);
        var scale = monitor?.Scale ?? 1;
        var work = monitor?.WorkArea ?? new RECT(0, 0, 10000, 10000);
        RectD Dip(RECT r) => new(r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale);
        var cardHeight = Math.Max(0, ActualHeight - 2 * ShadowMargin);
        var spot = TaskCardPlacement.Place(request.Host, Dip(request.Anchor), request.RowTop / scale, cardHeight, Dip(work));
        _spot = spot;
        var x = (int)Math.Round((spot.X - ShadowMargin) * scale);
        var y = (int)Math.Round((spot.Y - ShadowMargin) * scale);
        User32.SetWindowPos(hwnd, HWND.TOPMOST, x, y, 0, 0, SWP.NOSIZE | SWP.NOACTIVATE);
    }
}
