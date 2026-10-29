using System.Windows;
using System.Windows.Media.Animation;

namespace AgentSpeed.History.Tasks;

/// <summary>The section's timing (DESIGN 12, <i>Motion</i>; DESIGN 6): values crossfade in 0.2 s, bars spring in 0.35 s.</summary>
internal static class TaskMotion
{
    public static readonly Duration Fade = TimeSpan.FromMilliseconds(200);
    public static readonly Duration Spring = TimeSpan.FromMilliseconds(350);
    public static readonly IEasingFunction FadeEasing = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction SpringEasing = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 });

    public static DoubleAnimation FadeTo(double from, double to)
    {
        var animation = new DoubleAnimation(from, to, Fade) { EasingFunction = FadeEasing };
        animation.Freeze();
        return animation;
    }

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}

/// <summary>
/// A drawn element that moves from what it showed to a new target: it springs (≤ 0.35 s) or, with Reduce Motion,
/// crossfades the old drawing into the new one (0.2 s). It grows in from nothing the first time it is shown.
/// </summary>
internal abstract class TaskMorphElement : FrameworkElement
{
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(TaskMorphElement), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _entered;

    protected TaskMorphElement()
    {
        SnapsToDevicePixels = true;
        Loaded += (_, _) =>
        {
            if (_entered)
            {
                return;
            }

            _entered = true;
            if (IsVisible && !TaskAppearance.GetReduceMotion(this) && HasContent)
            {
                CaptureEmpty();
                Run(crossfade: false);
            }
        };
    }

    /// <summary>0 at the old drawing, 1 at the new one; past 1 while a spring overshoots.</summary>
    protected double Progress => (double)GetValue(ProgressProperty);

    /// <summary>The move is a crossfade: draw the old state at <c>1 − Progress</c> and the new one at <c>Progress</c>.</summary>
    protected bool Crossfading { get; private set; }

    /// <summary>Whether there is anything to grow in on first show.</summary>
    protected abstract bool HasContent { get; }

    /// <summary>Takes the drawing as now shown as the move's start.</summary>
    protected abstract void CaptureCurrent();

    /// <summary>Takes an empty drawing (zero-length bars) as the move's start.</summary>
    protected abstract void CaptureEmpty();

    protected static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>Call before the target changes; then set the new target and call <see cref="EndChange"/>.</summary>
    protected void BeginChange() => CaptureCurrent();

    protected void EndChange()
    {
        if (!IsLoaded || !IsVisible)
        {
            BeginAnimation(ProgressProperty, null);
            SetValue(ProgressProperty, 1.0);
            Crossfading = false;
            InvalidateVisual();
            return;
        }

        Run(TaskAppearance.GetReduceMotion(this));
    }

    private void Run(bool crossfade)
    {
        Crossfading = crossfade;
        var animation = crossfade
            ? new DoubleAnimation(0, 1, TaskMotion.Fade) { EasingFunction = TaskMotion.FadeEasing }
            : new DoubleAnimation(0, 1, TaskMotion.Spring) { EasingFunction = TaskMotion.SpringEasing };
        animation.Freeze();
        BeginAnimation(ProgressProperty, animation);
    }
}
