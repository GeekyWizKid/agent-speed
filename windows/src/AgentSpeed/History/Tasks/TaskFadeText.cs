using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgentSpeed.Presentation.History.Tasks;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// A value that crossfades (0.2 s) into its next text, weight and color, also with Reduce Motion (DESIGN 12,
/// <i>Motion</i>). <see cref="Value"/> applies a shown value's emphasis: semibold label color for the better side,
/// regular secondary beside it, the label color otherwise, and a tertiary <c>–</c> whose tooltip and spoken words say why.
/// </summary>
internal sealed class TaskFadeText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TaskFadeText), new FrameworkPropertyMetadata(string.Empty, OnShownChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(TaskValue), typeof(TaskFadeText), new FrameworkPropertyMetadata(null, OnShownChanged));

    /// <summary>The text reads in secondary (a small sample's values).</summary>
    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(bool), typeof(TaskFadeText), new FrameworkPropertyMetadata(false, OnShownChanged));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(TaskFadeText), new FrameworkPropertyMetadata(TextAlignment.Left, OnLayoutChanged));

    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping), typeof(TextWrapping), typeof(TaskFadeText), new FrameworkPropertyMetadata(TextWrapping.NoWrap, OnLayoutChanged));

    /// <summary>A block line height (as the summary cards' large values), NaN for the font's own.</summary>
    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
        nameof(LineHeight), typeof(double), typeof(TaskFadeText), new FrameworkPropertyMetadata(double.NaN, OnLayoutChanged));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TaskFadeText));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(TaskFadeText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits, OnShownChanged));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TaskFadeText));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TaskFadeText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits, OnShownChanged));

    private readonly TextBlock _front = new();
    private readonly TextBlock _back = new() { Opacity = 0 };
    private bool _captured;
    private int _fades;

    public TaskFadeText()
    {
        foreach (var block in new[] { _back, _front })
        {
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            AddVisualChild(block);
            AddLogicalChild(block);
        }
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TaskValue? Value
    {
        get => (TaskValue?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool Secondary
    {
        get => (bool)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>What the element shows now, as a screen reader says it.</summary>
    public string Spoken => Value?.Spoken ?? Text ?? string.Empty;

    protected override int VisualChildrenCount => 2;

    protected override System.Collections.IEnumerator LogicalChildren => new[] { _back, _front }.GetEnumerator();

    protected override Visual GetVisualChild(int index) => index == 0 ? _back : _front;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override Size MeasureOverride(Size availableSize)
    {
        _front.Measure(availableSize);
        _back.Measure(availableSize);
        return new Size(Math.Max(_front.DesiredSize.Width, _back.Opacity > 0 ? _back.DesiredSize.Width : 0), _front.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        _front.Arrange(rect);
        _back.Arrange(rect);
        return finalSize;
    }

    // A hit target over the whole cell, so the value's tooltip opens anywhere in it.
    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

    private static void OnShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TaskFadeText)d).Update();

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TaskFadeText)d;
        foreach (var block in new[] { view._front, view._back })
        {
            block.TextAlignment = view.TextAlignment;
            block.TextWrapping = view.TextWrapping;
            if (double.IsFinite(view.LineHeight) && view.LineHeight > 0)
            {
                block.LineHeight = view.LineHeight;
                block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            else
            {
                block.ClearValue(TextBlock.LineHeightProperty);
                block.ClearValue(TextBlock.LineStackingStrategyProperty);
            }
        }
    }

    private void Update()
    {
        var value = Value;
        var text = value?.Text ?? Text ?? string.Empty;
        var (weight, brushKey) = Look(value);
        ToolTip = value is { HasTooltip: true } ? value.Tooltip : null;
        if (_front.Text == text && _front.FontWeight == (weight ?? FontWeight) && Equals(_front.Tag, brushKey))
        {
            return;
        }

        if (IsLoaded && IsVisible && !string.IsNullOrEmpty(_front.Text))
        {
            if (!_captured)
            {
                // Several properties change in one update: the old look is taken once, at the first.
                _captured = true;
                _back.Text = _front.Text;
                _back.FontWeight = _front.FontWeight;
                _back.Foreground = _front.Foreground;
                var fade = ++_fades;
                var fadeOut = new DoubleAnimation(1, 0, TaskMotion.Fade) { EasingFunction = TaskMotion.FadeEasing };
                fadeOut.Completed += (_, _) =>
                {
                    if (fade == _fades)
                    {
                        _back.Text = string.Empty;
                        InvalidateMeasure();
                    }
                };
                _back.BeginAnimation(OpacityProperty, fadeOut);
                _front.BeginAnimation(OpacityProperty, TaskMotion.FadeTo(0, 1));
                Dispatcher.BeginInvoke(() => _captured = false, DispatcherPriority.Background);
            }
        }
        else
        {
            _back.BeginAnimation(OpacityProperty, null);
            _back.Opacity = 0;
            _back.Text = string.Empty;
            _front.BeginAnimation(OpacityProperty, null);
        }

        _front.Text = text;
        _front.Tag = brushKey;
        if (weight is { } strong)
        {
            _front.FontWeight = strong;
        }
        else
        {
            _front.ClearValue(TextBlock.FontWeightProperty);
        }

        if (brushKey is null)
        {
            _front.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            _front.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        InvalidateMeasure();
    }

    private (FontWeight? Weight, string? BrushKey) Look(TaskValue? value)
    {
        if (value is { IsNull: true })
        {
            return (null, "TextFillColorTertiaryBrush");
        }

        return (value?.Emphasis, Secondary) switch
        {
            (TaskEmphasis.Better, _) => (FontWeights.SemiBold, null),
            (TaskEmphasis.Other, _) or (_, true) => (null, "TextFillColorSecondaryBrush"),
            _ => (null, null),
        };
    }

    private sealed class Peer(TaskFadeText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(TaskFadeText);

        protected override string GetNameCore()
        {
            var name = AutomationProperties.GetName(owner);
            return string.IsNullOrEmpty(name) ? owner.Spoken : name;
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override bool IsControlElementCore() => true;
    }
}
