using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Theme;

namespace AgentSpeed.TaskScore;

/// <summary>
/// The card's state chip (DESIGN 12, <i>State chips</i>): 18 DIP tall, fully rounded, 7 DIP side padding, a 10 DIP glyph
/// and 11 DIP semibold text 4 DIP apart. Passing is mint, failing amber, the rest neutral — secondary text, no fill and a
/// dotted outline. High contrast: the text color, a 1 px solid stroke, no fill. Read as text.
/// </summary>
internal sealed class StateChip : FrameworkElement
{
    public const double ChipHeight = 18;
    public const double Padding = 7;
    public const double GlyphSize = 10;
    public const double GlyphGap = 4;
    public const double TextSize = 11;

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(StateChip),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    private static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        "Secondary", typeof(Brush), typeof(StateChip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private TaskChip? _chip;
    private TaskCardAppearance _appearance;

    public StateChip()
    {
        SnapsToDevicePixels = true;
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(SecondaryProperty, TaskCardAppearance.SecondaryKey);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public string Text => _chip?.Text ?? string.Empty;

    public void Apply(TaskChip? chip, TaskCardAppearance appearance)
    {
        if (chip == _chip && appearance == _appearance)
        {
            return;
        }

        _chip = chip;
        _appearance = appearance;
        Visibility = chip is null ? Visibility.Collapsed : Visibility.Visible;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChipPeer(this);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_chip is not { } chip)
        {
            return default;
        }

        var text = Label(chip.Text, Brushes.Black);
        var glyph = chip.Glyph == TaskChipGlyph.None ? 0 : GlyphSize + GlyphGap;
        return new Size(Math.Ceiling(Padding * 2 + glyph + text.WidthIncludingTrailingWhitespace), ChipHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_chip is not { } chip)
        {
            return;
        }

        var ink = _appearance.ChipText(chip.Tone) ?? (Brush?)GetValue(SecondaryProperty) ?? Brushes.Gray;
        var rect = new Rect(0, 0, ActualWidth, ChipHeight);
        var radius = ChipHeight / 2;
        if (_appearance.HighContrast)
        {
            var pen = new Pen(ink, 1);
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, Inset(rect, 0.5), radius - 0.5, radius - 0.5);
        }
        else if (chip.Tone == TaskChipTone.Neutral)
        {
            dc.DrawRoundedRectangle(null, ProofMarkDrawing.DottedPen(_appearance.ChipOutline, 1), Inset(rect, 0.5), radius - 0.5, radius - 0.5);
        }
        else
        {
            dc.DrawRoundedRectangle(_appearance.ChipFill(chip.Tone), null, rect, radius, radius);
        }

        var x = Padding;
        var middle = ChipHeight / 2;
        switch (chip.Glyph)
        {
            case TaskChipGlyph.DotRing:
                DotRing(dc, new Point(x + GlyphSize / 2, middle), ink);
                x += GlyphSize + GlyphGap;
                break;
            case TaskChipGlyph.Check or TaskChipGlyph.Alert or TaskChipGlyph.Clock:
                var symbol = new FormattedText(Symbol(chip.Glyph), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(AppFonts.Symbols, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), GlyphSize, ink, PixelsPerDip);
                dc.DrawText(symbol, new Point(x + (GlyphSize - symbol.Width) / 2, middle - symbol.Height / 2));
                x += GlyphSize + GlyphGap;
                break;
        }

        var text = Label(chip.Text, ink);
        dc.DrawText(text, new Point(x, middle - text.Height / 2));
    }

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private static string Symbol(TaskChipGlyph glyph) => glyph switch
    {
        TaskChipGlyph.Check => "",
        TaskChipGlyph.Alert => "",
        _ => "",
    };

    /// <summary>The unverified chip's glyph: a drawn ring of 8 dots, the proof mark's dotted language.</summary>
    private static void DotRing(DrawingContext dc, Point center, Brush brush)
    {
        const double ring = 3.6;
        const double dot = 0.8;
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4 - Math.PI / 2;
            dc.DrawEllipse(brush, null, new Point(center.X + ring * Math.Cos(angle), center.Y + ring * Math.Sin(angle)), dot, dot);
        }
    }

    private static Rect Inset(Rect rect, double by) => new(rect.X + by, rect.Y + by, Math.Max(0, rect.Width - 2 * by), Math.Max(0, rect.Height - 2 * by));

    private FormattedText Label(string text, Brush brush) =>
        new(text, TaskCardText.Culture(this), FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            TextSize, brush, PixelsPerDip);

    private sealed class ChipPeer(StateChip owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(StateChip);

        protected override string GetNameCore() => ((StateChip)Owner).Text;
    }
}
