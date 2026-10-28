using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Theme;

namespace AgentSpeed.Taskbar;

/// <summary>
/// Draws the taskbar intelligence block (DESIGN 10, Menu bar and taskbar): <c>智力 125</c> over <c>思考 10 tok/s</c> in
/// 10 px medium tabular text, labels left-aligned in a column, both numbers right-aligned in one cell as wide as
/// <c>8.88k</c>, the unit after the second. Numbers in the label color at 85 %, labels, unit, <c>–</c> and a zero
/// thinking rate in the secondary color. Runs on the taskbar thread.
/// </summary>
internal sealed class TaskbarIntelligenceText
{
    public const double TextSize = 10;

    /// <summary>Baseline distance of the two lines, as the money and traffic blocks.</summary>
    public const double Pitch = 12;

    /// <summary>Before the block.</summary>
    public const double Gap = 8;

    private readonly Dictionary<(char, int), FormattedText> _glyphs = new();
    private readonly Dictionary<string, FormattedText> _texts = new(StringComparer.Ordinal);
    private Typeface _face = Face(Presentation.Localization.LanguageCodes.ZhHant);
    private string _language = Presentation.Localization.LanguageCodes.ZhHant;
    private double _scale = -1;
    private Color _primary;
    private Color _secondary;
    private double _digitWidth;

    /// <summary>The block's width with its gap; it depends on the language only below 1 M tok/s.</summary>
    public double Width(BarIntelligenceBlock block, double scale, Color primary, Color secondary)
    {
        Ensure(block.LanguageCode, scale, primary, secondary);
        return Gap + Cells(block).Width;
    }

    /// <summary>The block's height (vertical taskbars stack it under the value).</summary>
    public static double Height => Pitch * 2 + 2;

    /// <summary>Draws the block starting at <paramref name="x"/> (its gap included), the two lines centered on <paramref name="centerY"/>.</summary>
    public void Draw(DrawingContext dc, BarIntelligenceBlock block, double x, double centerY, double scale, Color primary, Color secondary)
    {
        Ensure(block.LanguageCode, scale, primary, secondary);
        var cells = Cells(block);
        var left = x + Gap;
        var numberRight = left + cells.Label + BarIntelligenceCells.LabelGap + cells.Number;
        var capHeight = (_face.CapsHeight > 0 ? _face.CapsHeight : 0.7) * TextSize;
        var first = Math.Round((centerY - Pitch / 2 + capHeight / 2) * scale) / scale;
        DrawLine(dc, block.Label, block.Index, block.Quiet, null, left, numberRight, first, scale);
        DrawLine(dc, block.ThinkingLabel, block.Thinking, block.ThinkingDim, block.Unit, left, numberRight, first + Pitch, scale);
    }

    private void DrawLine(DrawingContext dc, string label, string number, bool dim, string? unit, double left, double numberRight,
        double baseline, double scale)
    {
        var labelText = Text(label);
        dc.DrawText(labelText, new Point(Snap(left, scale), baseline - labelText.Baseline));
        var role = dim ? 1 : 0;
        var x = numberRight - Tabular(number, role);
        foreach (var c in number)
        {
            var glyph = Glyph(c, role);
            var width = char.IsDigit(c) ? _digitWidth : glyph.WidthIncludingTrailingWhitespace;
            dc.DrawText(glyph, new Point(Snap(x + (width - glyph.WidthIncludingTrailingWhitespace) / 2, scale), baseline - glyph.Baseline));
            x += width;
        }

        if (unit is not null)
        {
            var unitText = Text(unit);
            dc.DrawText(unitText, new Point(Snap(numberRight + BarIntelligenceCells.UnitGap, scale), baseline - unitText.Baseline));
        }
    }

    private BarIntelligenceCells Cells(BarIntelligenceBlock block) =>
        block.Cells(text => Text(text).WidthIncludingTrailingWhitespace, number => Tabular(number, 0));

    private double Tabular(string text, int role)
    {
        var width = 0.0;
        foreach (var c in text)
        {
            width += char.IsDigit(c) ? _digitWidth : Glyph(c, role).WidthIncludingTrailingWhitespace;
        }

        return width;
    }

    /// <summary>A label or the unit in the secondary color.</summary>
    private FormattedText Text(string text)
    {
        if (!_texts.TryGetValue(text, out var formatted))
        {
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, TextSize,
                new SolidColorBrush(_secondary), null, TextFormattingMode.Ideal, _scale);
            if (_texts.Count > 32)
            {
                _texts.Clear();
            }

            _texts[text] = formatted;
        }

        return formatted;
    }

    /// <summary>A number character: role 0 in the label color at 85 %, role 1 (dash, zero) in the secondary color.</summary>
    private FormattedText Glyph(char c, int role)
    {
        if (!_glyphs.TryGetValue((c, role), out var text))
        {
            var color = role == 0 ? Color.FromArgb((byte)(_primary.A * 0.85), _primary.R, _primary.G, _primary.B) : _secondary;
            text = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, TextSize,
                new SolidColorBrush(color), null, TextFormattingMode.Ideal, _scale);
            _glyphs[(c, role)] = text;
        }

        return text;
    }

    private void Ensure(string language, double scale, Color primary, Color secondary)
    {
        if (Math.Abs(scale - _scale) < 0.001 && primary == _primary && secondary == _secondary && language == _language)
        {
            return;
        }

        if (language != _language)
        {
            _language = language;
            _face = Face(language);
        }

        _scale = scale;
        _primary = primary;
        _secondary = secondary;
        _glyphs.Clear();
        _texts.Clear();
        _digitWidth = 0;
        for (var c = '0'; c <= '9'; c++)
        {
            _digitWidth = Math.Max(_digitWidth, Glyph(c, 0).WidthIncludingTrailingWhitespace);
        }
    }

    /// <summary>The UI face with the language's CJK fallback, so 智力 / 知 / 思考 take the right glyph forms.</summary>
    private static Typeface Face(string language) => new(AppFonts.Ui(language), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private static double Snap(double value, double scale) => Math.Round(value * scale) / scale;
}
