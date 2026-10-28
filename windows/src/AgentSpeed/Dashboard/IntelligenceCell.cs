using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Formatting;

namespace AgentSpeed.Dashboard;

/// <summary>
/// An agent row's intelligence (DESIGN 4, Intelligence column): the number right-aligned in the fixed cell, 12 DIP
/// semibold tabular on the 13 DIP live value's baseline. Live in the live value's number color, resting and <c>–</c> in
/// the tertiary color (high contrast: text and gray-text). Digits are replaced in place; a change of form crossfades the
/// color in 0.2 s, an opacity change kept with Reduce Motion.
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

    private static readonly Duration Crossfade = new(TimeSpan.FromMilliseconds(200));

    private readonly TextBlock _live = Number();
    private readonly TextBlock _quiet = Number();

    public IntelligenceCell()
    {
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_quiet);
        Children.Add(_live);
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

    protected override AutomationPeer OnCreateAutomationPeer() => new IntelligenceCellPeer(this);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextElement.FontFamilyProperty)
        {
            AlignBaseline();
        }
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
    }

    private void Paint()
    {
        if (HighContrast)
        {
            _live.Foreground = SystemColors.WindowTextBrush;
            _quiet.Foreground = SystemColors.GrayTextBrush;
            return;
        }

        _live.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _quiet.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
    }

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
    }

    private void ShowForm(bool animate)
    {
        var live = Form == AgentIntelligenceForm.Live;
        animate &= IsLoaded && IsVisible;
        Fade(_live, live ? 1 : 0, animate);
        Fade(_quiet, live ? 0 : 1, animate);
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
