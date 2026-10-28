using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.TaskScore;

/// <summary>The score card's bars share their brushes and a short ease to new lengths (Reduce Motion: at once).</summary>
internal abstract class ScoreBarBase : FrameworkElement
{
    public static readonly DependencyProperty InkProperty = Brush(nameof(Ink));
    public static readonly DependencyProperty TrackProperty = Brush(nameof(Track));
    public static readonly DependencyProperty DotsProperty = Brush(nameof(Dots));

    public static readonly DependencyProperty OutlinedTrackProperty = DependencyProperty.Register(
        nameof(OutlinedTrack), typeof(bool), typeof(ScoreBarBase), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A bar eases to a new length in this long.</summary>
    protected const double EaseSeconds = 0.24;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private bool _rendering;

    protected ScoreBarBase()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
        SetResourceReference(DotsProperty, TaskCardAppearance.TertiaryKey);
        Unloaded += (_, _) => StopFrames();
    }

    public Brush? Ink
    {
        get => (Brush?)GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>The dots of an unproven track.</summary>
    public Brush? Dots
    {
        get => (Brush?)GetValue(DotsProperty);
        set => SetValue(DotsProperty, value);
    }

    /// <summary>High contrast: the track is a hairline outline in its brush rather than a fill.</summary>
    public bool OutlinedTrack
    {
        get => (bool)GetValue(OutlinedTrackProperty);
        set => SetValue(OutlinedTrackProperty, value);
    }

    protected static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>The bars are drawn twins of the card's numbers, which carry the words.</summary>
    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    protected void DrawTrack(DrawingContext dc, Rect rect, double radius)
    {
        if (Track is not { } track)
        {
            return;
        }

        if (OutlinedTrack)
        {
            var pen = new Pen(track, 1);
            pen.Freeze();
            var inner = new Rect(rect.X + 0.5, rect.Y + 0.5, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
            dc.DrawRoundedRectangle(null, pen, inner, Math.Max(0, radius - 0.5), Math.Max(0, radius - 0.5));
        }
        else
        {
            dc.DrawRoundedRectangle(track, null, rect, radius, radius);
        }
    }

    protected void StartFrames()
    {
        if (_rendering || !IsLoaded)
        {
            return;
        }

        _rendering = true;
        CompositionTarget.Rendering += OnFrame;
    }

    protected abstract bool Moving(double now);

    private void StopFrames()
    {
        if (_rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (!Moving(Now))
        {
            StopFrames();
        }
    }

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(ScoreBarBase), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}

/// <summary>
/// A dimension's bar (DESIGN 12, <i>Score card</i>, 3): 4 DIP tall, radius 2, filled <c>value / max</c> in the score ink;
/// with no verification result yet the track itself is dotted and unfilled; pending V holds its credit as dots.
/// </summary>
internal sealed class ScoreBar : ScoreBarBase
{
    public const double BarHeight = 4;

    private double _from;
    private double _to;
    private double _start = double.NegativeInfinity;
    private double _ghost;
    private bool _dotted;
    private bool _reduceMotion;

    /// <summary>Takes a row's fill, held credit and track; the fill eases unless <paramref name="animate"/> is false.</summary>
    public void Apply(TaskDimensionRow row, bool animate, bool reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(row);
        _reduceMotion = reduceMotion;
        var now = Now;
        var fill = Math.Clamp(row.Fill, 0, 1);
        if (Math.Abs(fill - _to) > 1e-6)
        {
            _from = animate && !reduceMotion && IsLoaded ? Current(now) : fill;
            _to = fill;
            _start = now;
            StartFrames();
        }

        _ghost = Math.Clamp(row.Ghost, 0, 1);
        _dotted = row.DottedTrack;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, BarHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var rect = new Rect(0, 0, width, BarHeight);
        if (_dotted)
        {
            if (Dots is { } dots)
            {
                ProofMarkDrawing.DottedRow(dc, rect, TaskProofMark.TrackPitch, TaskProofMark.TrackDot, dots);
            }

            return;
        }

        DrawTrack(dc, rect, BarHeight / 2);
        if (_ghost > 0 && Ink is { } ghostInk)
        {
            ProofMarkDrawing.DottedRow(dc, new Rect(0, 0, width * _ghost, BarHeight), TaskProofMark.GhostPitch, TaskProofMark.GhostDot, ghostInk);
        }

        var fill = Current(Now);
        if (fill > 0 && Ink is { } ink)
        {
            dc.DrawRoundedRectangle(ink, null, new Rect(0, 0, Math.Max(BarHeight, width * fill), BarHeight), BarHeight / 2, BarHeight / 2);
        }
    }

    protected override bool Moving(double now) => !_reduceMotion && now - _start < EaseSeconds;

    private double Current(double now) =>
        _reduceMotion ? _to : Easing.Lerp(_from, _to, Easing.OutCubic((now - _start) / EaseSeconds));
}

/// <summary>
/// The composition bar under the status line (DESIGN 12, <i>Score card</i>, 2): the width stands for 100 points, the five
/// dimensions stack V C R E K with 2 DIP between them in the score ink on the track, 6 DIP tall, radius 3; while pending
/// V's place holds the dotted credit a passing re-run restores.
/// </summary>
internal sealed class CompositionBar : ScoreBarBase
{
    public const double BarHeight = 6;
    public const double Gap = 2;

    private IReadOnlyList<TaskCompositionSegment> _segments = [];

    public void Apply(IReadOnlyList<TaskCompositionSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.SequenceEqual(_segments))
        {
            return;
        }

        _segments = segments;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, BarHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var track = new Rect(0, 0, width, BarHeight);
        DrawTrack(dc, track, BarHeight / 2);
        if (Ink is not { } ink)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(track, BarHeight / 2, BarHeight / 2));
        foreach (var rect in TaskProofMark.Composition(_segments, width, Gap))
        {
            var r = new Rect(rect.X, 0, rect.Width, BarHeight);
            if (rect.IsGhost)
            {
                ProofMarkDrawing.DottedRow(dc, r, TaskProofMark.CompositionGhostPitch, TaskProofMark.CompositionGhostDot, ink);
            }
            else
            {
                dc.DrawRectangle(ink, null, r);
            }
        }

        dc.Pop();
    }

    protected override bool Moving(double now) => false;
}
