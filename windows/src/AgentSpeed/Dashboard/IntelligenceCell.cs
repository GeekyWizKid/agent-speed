using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.TaskScore;

namespace AgentSpeed.Dashboard;

/// <summary>
/// An agent row's 智力 (DESIGN 4, Intelligence column; DESIGN 12): the task score right-aligned in the fixed cell, 12 DIP
/// semibold tabular on the 13 DIP live value's baseline. Live in the live value's number color, resting and <c>–</c> in
/// the tertiary color (high contrast: text and gray-text), the dotted proof mark under an unproven score. A rise of 3 or
/// more rolls the changed digits upward (0.24 s, the board hero's curve, entering digits softened), smaller rises replace
/// them in place, a fall crossfades in 0.2 s; a change of form crossfades the color in 0.2 s; Reduce Motion turns every
/// value change into a 0.15 s crossfade.
/// </summary>
internal sealed class IntelligenceCell : Grid
{
    /// <summary>The live value's size, whose baseline the number shares.</summary>
    public const double LiveValueSize = 13;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(IntelligenceCell), new PropertyMetadata(string.Empty, (d, e) => ((IntelligenceCell)d).SetText((string?)e.NewValue)));

    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(AgentIntelligenceForm), typeof(IntelligenceCell),
        new PropertyMetadata(AgentIntelligenceForm.None, (d, _) => ((IntelligenceCell)d).ShowForm(animate: true)));

    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.Register(
        nameof(HighContrast), typeof(bool), typeof(IntelligenceCell), new PropertyMetadata(false, (d, _) => ((IntelligenceCell)d).Paint()));

    /// <summary>The shown score, which plans the motion of a change (null: none).</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int?), typeof(IntelligenceCell), new PropertyMetadata(null, (d, e) => ((IntelligenceCell)d).OnValueChanged((int?)e.OldValue)));

    /// <summary>No verified result behind the score: the dotted underline (DESIGN 12, <i>Proof mark</i>).</summary>
    public static readonly DependencyProperty UnprovenProperty = DependencyProperty.Register(
        nameof(Unproven), typeof(bool), typeof(IntelligenceCell), new PropertyMetadata(false, (d, _) => ((IntelligenceCell)d).ShowMark(animate: true)));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(IntelligenceCell), new PropertyMetadata(false));

    private static readonly DependencyProperty MarkOpacityProperty = DependencyProperty.Register(
        "MarkOpacity", typeof(double), typeof(IntelligenceCell), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Duration Crossfade = new(TimeSpan.FromMilliseconds(200));
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly Grid _digits = new() { IsHitTestVisible = false };
    private readonly TextBlock _live = Number();
    private readonly TextBlock _quiet = Number();
    private readonly TextBlock _outgoing = Number();
    private readonly RollLayer _roll;

    public IntelligenceCell()
    {
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
        _digits.Children.Add(_quiet);
        _digits.Children.Add(_live);
        _outgoing.Opacity = 0;
        _roll = new RollLayer(this);
        Children.Add(_digits);
        Children.Add(_outgoing);
        Children.Add(_roll);
        Paint();
        ShowForm(animate: false);
        AlignBaseline();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public AgentIntelligenceForm Form
    {
        get => (AgentIntelligenceForm)GetValue(FormProperty);
        set => SetValue(FormProperty, value);
    }

    public bool HighContrast
    {
        get => (bool)GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
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

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    private bool Animates => IsLoaded && IsVisible;

    protected override AutomationPeer OnCreateAutomationPeer() => new IntelligenceCellPeer(this);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextElement.FontFamilyProperty)
        {
            AlignBaseline();
        }
    }

    /// <summary>The dotted underline under the digits, right-aligned like them (it takes no room in the row).</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var opacity = (double)GetValue(MarkOpacityProperty);
        if (opacity <= 0 || Value is null || string.IsNullOrEmpty(_live.Text) || ActualWidth <= 0)
        {
            return;
        }

        var number = Format(_live.Text, Brushes.Black);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var mark = TaskProofMark.Underline(AgentIntelligenceColumn.NumberSize, number.WidthIncludingTrailingWhitespace, dpi, highContrast: HighContrast);
        var baseline = Top(_live) + number.Baseline;
        var brush = HighContrast ? SystemColors.WindowTextBrush : Ink();
        ProofMarkDrawing.Underline(dc, mark, ActualWidth - number.WidthIncludingTrailingWhitespace, baseline, brush, opacity);
    }

    private static TextBlock Number()
    {
        var text = new TextBlock
        {
            FontSize = AgentIntelligenceColumn.NumberSize,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        return text;
    }

    private void SetText(string? text)
    {
        _live.Text = text ?? string.Empty;
        _quiet.Text = text ?? string.Empty;
        InvalidateVisual();
    }

    private void Paint()
    {
        if (HighContrast)
        {
            _live.Foreground = SystemColors.WindowTextBrush;
            _quiet.Foreground = SystemColors.GrayTextBrush;
            _outgoing.Foreground = Form == AgentIntelligenceForm.Live ? SystemColors.WindowTextBrush : SystemColors.GrayTextBrush;
            InvalidateVisual();
            return;
        }

        _live.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _quiet.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        _outgoing.SetResourceReference(TextBlock.ForegroundProperty, Form == AgentIntelligenceForm.Live ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
        InvalidateVisual();
    }

    /// <summary>The number's color now: the live text color while live, else the quiet one.</summary>
    private Brush Ink() => (Form == AgentIntelligenceForm.Live ? _live.Foreground : _quiet.Foreground) ?? Brushes.Gray;

    /// <summary>
    /// Centered like the live value, then lowered so both sizes share one baseline: a centered line's baseline sits
    /// (baseline − line spacing / 2) ems below the center, and a top margin lowers a centered text by half of it.
    /// </summary>
    private void AlignBaseline()
    {
        var family = TextElement.GetFontFamily(this);
        var below = family.Baseline > 0 && family.LineSpacing > 0 ? family.Baseline - family.LineSpacing / 2 : 0.414;
        var margin = new Thickness(0, 2 * below * (LiveValueSize - AgentIntelligenceColumn.NumberSize), 0, 0);
        _live.Margin = margin;
        _quiet.Margin = margin;
        _outgoing.Margin = margin;
    }

    private void ShowForm(bool animate)
    {
        var live = Form == AgentIntelligenceForm.Live;
        animate &= Animates;
        Fade(_live, live ? 1 : 0, animate);
        Fade(_quiet, live ? 0 : 1, animate);
        Paint();
    }

    private void ShowMark(bool animate)
    {
        var target = Unproven && Value is not null ? 1.0 : 0.0;
        BeginAnimation(MarkOpacityProperty, null);
        if (!animate || !Animates || ReduceMotion)
        {
            SetValue(MarkOpacityProperty, target);
            return;
        }

        BeginAnimation(MarkOpacityProperty, new DoubleAnimation(target, new Duration(TimeSpan.FromSeconds(TaskProofMark.FadeSeconds))));
    }

    private void OnValueChanged(int? old)
    {
        ShowMark(animate: true);
        if (!Animates)
        {
            return;
        }

        var change = TaskScoreMotion.Plan(old, Value, TaskScoreSurface.DashboardRow, ReduceMotion);
        switch (change.Kind)
        {
            case TaskScoreChangeKind.Roll:
                _roll.Start(TaskScoreText.Number(old), TaskScoreText.Number(Value), change.Duration);
                break;
            case TaskScoreChangeKind.Crossfade:
                _roll.Stop();
                _outgoing.Text = TaskScoreText.Number(old);
                _outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(change.Duration))));
                _digits.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(change.Duration)))
                {
                    FillBehavior = FillBehavior.Stop,
                });
                break;
        }
    }

    private static void Fade(UIElement element, double to, bool animate)
    {
        var from = element.Opacity;
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = to;
        if (!animate || Math.Abs(from - to) < 0.001)
        {
            return;
        }

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, Crossfade)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    private double Top(UIElement element) => element.TranslatePoint(default, this).Y;

    private FormattedText Format(string text, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            AgentIntelligenceColumn.NumberSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>
    /// The upward roll of the changed digits: each changed slot (aligned from the right) leaves upward and fades while its
    /// new digit enters from below, softened by a short vertical smear in place of the 1.4 DIP blur; unchanged digits stay.
    /// </summary>
    private sealed class RollLayer : FrameworkElement
    {
        private readonly IntelligenceCell _cell;
        private string _from = string.Empty;
        private string _to = string.Empty;
        private double _start = double.NegativeInfinity;
        private double _duration;
        private bool _rendering;

        public RollLayer(IntelligenceCell cell)
        {
            _cell = cell;
            IsHitTestVisible = false;
            ClipToBounds = true;
            Unloaded += (_, _) => Stop();
        }

        public void Start(string from, string to, double duration)
        {
            _from = from;
            _to = to;
            _duration = duration;
            _start = Clock.Elapsed.TotalSeconds;
            _cell._digits.Opacity = 0;
            if (!_rendering)
            {
                _rendering = true;
                CompositionTarget.Rendering += OnFrame;
            }

            InvalidateVisual();
        }

        public void Stop()
        {
            if (_rendering)
            {
                _rendering = false;
                CompositionTarget.Rendering -= OnFrame;
            }

            _start = double.NegativeInfinity;
            _cell._digits.Opacity = 1;
            InvalidateVisual();
        }

        protected override AutomationPeer? OnCreateAutomationPeer() => null;

        protected override void OnRender(DrawingContext dc)
        {
            if (double.IsNegativeInfinity(_start) || ActualWidth <= 0)
            {
                return;
            }

            var t = Easing.Clamp01((Clock.Elapsed.TotalSeconds - _start) / _duration);
            var e = Easing.OutCubic(t);
            var brush = _cell.Ink();
            var baseline = _cell.Top(_cell._live) + _cell.Format("0", brush).Baseline;
            var distance = AgentIntelligenceColumn.NumberSize;
            var right = ActualWidth;
            var slots = Math.Max(_from.Length, _to.Length);
            for (var i = 1; i <= slots; i++)
            {
                var next = i <= _to.Length ? _to[^i].ToString() : string.Empty;
                var prev = i <= _from.Length ? _from[^i].ToString() : string.Empty;
                var glyph = _cell.Format(next.Length > 0 ? next : prev, brush);
                var x = right - glyph.WidthIncludingTrailingWhitespace;
                if (next == prev)
                {
                    Draw(dc, next, brush, x, baseline, 0, 1);
                }
                else
                {
                    Draw(dc, prev, brush, x, baseline, -distance * e, 1 - e);
                    var smear = 1 - t;
                    if (smear > 0.5)
                    {
                        Draw(dc, next, brush, x, baseline, distance * (1 - e) - 0.7, e / 3);
                        Draw(dc, next, brush, x, baseline, distance * (1 - e) + 0.7, e / 3);
                    }

                    Draw(dc, next, brush, x, baseline, distance * (1 - e), smear > 0.5 ? e / 3 : e);
                }

                right = x;
            }
        }

        private void Draw(DrawingContext dc, string text, Brush brush, double x, double baseline, double dy, double opacity)
        {
            if (text.Length == 0 || opacity <= 0)
            {
                return;
            }

            var glyph = _cell.Format(text, brush);
            dc.PushOpacity(Math.Clamp(opacity, 0, 1));
            dc.DrawText(glyph, new Point(x, baseline - glyph.Baseline + dy));
            dc.Pop();
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            if (Clock.Elapsed.TotalSeconds - _start >= _duration)
            {
                Stop();
                return;
            }

            InvalidateVisual();
        }
    }

    /// <summary>One text element named by <c>AutomationProperties.Name</c>; nothing to read without a value.</summary>
    private sealed class IntelligenceCellPeer(IntelligenceCell owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(IntelligenceCell);

        protected override bool IsControlElementCore() => HasName;

        protected override bool IsContentElementCore() => HasName;

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        private bool HasName => !string.IsNullOrEmpty(AutomationProperties.GetName(Owner));
    }
}
