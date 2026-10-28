using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.TaskScore;

/// <summary>
/// A score number with its <c>/100</c> on one baseline and the proof mark under unproven digits (DESIGN 12): the card's
/// 34 DIP number. A rise of 3 or more counts up through every value (easeOutCubic, at most 0.8 s), a fall crossfades in
/// 0.2 s, a change of form crossfades the color in 0.2 s, and Reduce Motion turns every change into a 0.15 s crossfade.
/// Frames run only while something moves.
/// </summary>
internal sealed class ScoreNumber : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int?), typeof(ScoreNumber), new FrameworkPropertyMetadata(null, (d, _) => ((ScoreNumber)d).OnValueChanged()));

    public static readonly DependencyProperty UnprovenProperty = DependencyProperty.Register(
        nameof(Unproven), typeof(bool), typeof(ScoreNumber), new FrameworkPropertyMetadata(false, (d, _) => ((ScoreNumber)d).OnMarkChanged()));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(TaskNumberTone), typeof(ScoreNumber), new FrameworkPropertyMetadata(TaskNumberTone.None, (d, e) => ((ScoreNumber)d).OnToneChanged((TaskNumberTone)e.OldValue)));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(ScoreNumber), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(ScoreNumber), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.Register(
        nameof(HighContrast), typeof(bool), typeof(ScoreNumber), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(ScoreNumber),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(ScoreNumber),
        new FrameworkPropertyMetadata(34.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LiveBrushProperty = RegisterBrush(nameof(LiveBrush));
    public static readonly DependencyProperty RestingBrushProperty = RegisterBrush(nameof(RestingBrush));
    public static readonly DependencyProperty NoneBrushProperty = RegisterBrush(nameof(NoneBrush));
    public static readonly DependencyProperty SuffixBrushProperty = RegisterBrush(nameof(SuffixBrush));

    /// <summary>The <c>/100</c>: 13 DIP, 3 DIP after the number.</summary>
    public const double SuffixSize = 13;

    public const double SuffixGap = 3;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly TaskScoreTicker _ticker = new(TaskScoreSurface.Card);
    private TaskNumberTone _toneFrom = TaskNumberTone.None;
    private double _toneStart = double.NegativeInfinity;
    private double _markFrom;
    private double _markTo;
    private double _markStart = double.NegativeInfinity;
    private bool _rendering;

    public ScoreNumber()
    {
        SnapsToDevicePixels = true;
        SetResourceReference(LiveBrushProperty, TaskCardAppearance.PrimaryKey);
        SetResourceReference(RestingBrushProperty, TaskCardAppearance.SecondaryKey);
        SetResourceReference(NoneBrushProperty, TaskCardAppearance.TertiaryKey);
        SetResourceReference(SuffixBrushProperty, TaskCardAppearance.TertiaryKey);
        Unloaded += (_, _) => StopFrames();
    }

    public int? Value
    {
        get => (int?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool Unproven
    {
        get => (bool)GetValue(UnprovenProperty);
        set => SetValue(UnprovenProperty, value);
    }

    public TaskNumberTone Tone
    {
        get => (TaskNumberTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    public bool HighContrast
    {
        get => (bool)GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush? LiveBrush
    {
        get => (Brush?)GetValue(LiveBrushProperty);
        set => SetValue(LiveBrushProperty, value);
    }

    public Brush? RestingBrush
    {
        get => (Brush?)GetValue(RestingBrushProperty);
        set => SetValue(RestingBrushProperty, value);
    }

    public Brush? NoneBrush
    {
        get => (Brush?)GetValue(NoneBrushProperty);
        set => SetValue(NoneBrushProperty, value);
    }

    public Brush? SuffixBrush
    {
        get => (Brush?)GetValue(SuffixBrushProperty);
        set => SetValue(SuffixBrushProperty, value);
    }

    /// <summary>Shows <paramref name="value"/> at once, without motion (a card that opens shows its number as it is).</summary>
    public void Reset(int? value, bool unproven, TaskNumberTone tone)
    {
        SetCurrentValue(ValueProperty, value);
        SetCurrentValue(UnprovenProperty, unproven);
        SetCurrentValue(ToneProperty, tone);
        _ticker.Set(value, Now, ReduceMotion, animate: false);
        _markTo = _markFrom = unproven && value is not null ? 1 : 0;
        _markStart = double.NegativeInfinity;
        _toneFrom = tone;
        _toneStart = double.NegativeInfinity;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NumberPeer(this);

    protected override Size MeasureOverride(Size availableSize)
    {
        var now = Now;
        var number = Format(TaskScoreText.Number(_ticker.Shown(now)), FontSize, ToneBrush(now), NumberWeight(now));
        var width = number.WidthIncludingTrailingWhitespace;
        if (_ticker.Outgoing(now) is { } outgoing)
        {
            width = Math.Max(width, Format(TaskScoreText.Number(outgoing), FontSize, ToneBrush(now), FontWeights.SemiBold).WidthIncludingTrailingWhitespace);
        }

        if (!string.IsNullOrEmpty(Suffix))
        {
            width += SuffixGap + Format(Suffix, SuffixSize, SuffixBrush, FontWeights.Medium).WidthIncludingTrailingWhitespace;
        }

        // The dotted underline takes no room in layout unless the font's line leaves none under the baseline.
        var below = number.Baseline + TaskProofMark.Below * FontSize + TaskProofMark.Diameter(FontSize, 1, HighContrast) / 2;
        return new Size(Math.Ceiling(width), Math.Ceiling(Math.Max(number.Height, below)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var now = Now;
        var brush = ToneBrush(now);
        var (outgoingOpacity, incomingOpacity) = _ticker.Fade(now);
        var shown = _ticker.Shown(now);
        var number = Format(TaskScoreText.Number(shown), FontSize, brush, NumberWeight(now));
        var baseline = number.Baseline;
        var width = number.WidthIncludingTrailingWhitespace;

        if (_ticker.Outgoing(now) is { } outgoing && outgoingOpacity > 0)
        {
            var old = Format(TaskScoreText.Number(outgoing), FontSize, brush, FontWeights.SemiBold);
            dc.PushOpacity(outgoingOpacity);
            dc.DrawText(old, new Point(0, 0));
            dc.Pop();
            width = Math.Max(width, old.WidthIncludingTrailingWhitespace);
        }

        dc.PushOpacity(incomingOpacity);
        dc.DrawText(number, new Point(0, 0));
        dc.Pop();

        var mark = MarkOpacity(now);
        if (mark > 0 && shown is not null)
        {
            var underline = TaskProofMark.Underline(FontSize, number.WidthIncludingTrailingWhitespace, VisualTreeHelper.GetDpi(this).PixelsPerDip,
                highContrast: HighContrast);
            ProofMarkDrawing.Underline(dc, underline, 0, baseline, brush ?? Brushes.Gray, mark * incomingOpacity);
        }

        if (!string.IsNullOrEmpty(Suffix))
        {
            var suffix = Format(Suffix, SuffixSize, SuffixBrush, FontWeights.Medium);
            dc.DrawText(suffix, new Point(width + SuffixGap, baseline - suffix.Baseline));
        }
    }

    private static double Now => Clock.Elapsed.TotalSeconds;

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(ScoreNumber), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private void OnValueChanged()
    {
        var animate = IsLoaded && IsVisible;
        _ticker.Set(Value, Now, ReduceMotion, animate);
        if (Value is null)
        {
            _markTo = _markFrom = 0;
        }

        InvalidateMeasure();
        InvalidateVisual();
        StartFramesIfMoving();
    }

    private void OnMarkChanged()
    {
        var now = Now;
        var target = Unproven ? 1.0 : 0.0;
        if (target == _markTo)
        {
            return;
        }

        _markFrom = MarkOpacity(now);
        _markTo = target;
        _markStart = IsLoaded && IsVisible && !ReduceMotion ? now : double.NegativeInfinity;
        InvalidateVisual();
        StartFramesIfMoving();
    }

    private void OnToneChanged(TaskNumberTone old)
    {
        _toneFrom = old;
        _toneStart = IsLoaded && IsVisible ? Now : double.NegativeInfinity;
        InvalidateMeasure();
        InvalidateVisual();
        StartFramesIfMoving();
    }

    private double MarkOpacity(double now)
    {
        var t = Easing.Clamp01((now - _markStart) / TaskProofMark.FadeSeconds);
        return Easing.Lerp(_markFrom, _markTo, t);
    }

    private double ToneProgress(double now) => Easing.Clamp01((now - _toneStart) / TaskScoreMotion.FormSeconds);

    /// <summary>The <c>–</c> of none reads in medium weight, a number in semibold.</summary>
    private FontWeight NumberWeight(double now) => _ticker.Shown(now) is null ? FontWeights.Medium : FontWeights.SemiBold;

    /// <summary>The number's color, crossfading between forms (kept with Reduce Motion: an opacity change, not motion).</summary>
    private Brush? ToneBrush(double now)
    {
        var to = BrushOf(Tone);
        var t = ToneProgress(now);
        if (t >= 1 || _toneFrom == Tone || BrushOf(_toneFrom) is not SolidColorBrush from || to is not SolidColorBrush target)
        {
            return to;
        }

        var a = from.Color;
        var b = target.Color;
        byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        var brush = new SolidColorBrush(Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B)));
        brush.Freeze();
        return brush;
    }

    private Brush? BrushOf(TaskNumberTone tone) => tone switch
    {
        TaskNumberTone.Live => LiveBrush,
        TaskNumberTone.Resting => RestingBrush,
        _ => NoneBrush,
    };

    private FormattedText Format(string text, double size, Brush? brush, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void StartFramesIfMoving()
    {
        if (_rendering || !IsLoaded || !Moving(Now))
        {
            return;
        }

        _rendering = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopFrames()
    {
        if (_rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private bool Moving(double now) =>
        _ticker.IsAnimating(now) || ToneProgress(now) < 1 || (now - _markStart) < TaskProofMark.FadeSeconds;

    private void OnFrame(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
        if (!Moving(Now))
        {
            StopFrames();
        }
    }

    private sealed class NumberPeer(ScoreNumber owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(ScoreNumber);

        protected override string GetNameCore()
        {
            var number = (ScoreNumber)Owner;
            return TaskScoreText.Number(number.Value) + number.Suffix;
        }
    }
}
