using System.Windows;
using System.Windows.Media;

namespace AgentSpeed.History.Tasks;

/// <summary>Puts a small caption on the baseline of a large number beside it (DESIGN 12: the median with its caption beside it).</summary>
internal static class TaskBaseline
{
    /// <summary>
    /// The caption's bottom margin when both sit on the same bottom edge: the number in a block line of
    /// <paramref name="lineHeight"/> keeps its baseline at <c>Baseline / LineSpacing</c> of the line, the caption in its
    /// natural line at <c>Baseline</c> ems from the top.
    /// </summary>
    public static Thickness CaptionMargin(FontFamily number, double lineHeight, FontFamily caption, double captionSize, double left)
    {
        var numberBelow = number.LineSpacing > 0 ? lineHeight * (1 - number.Baseline / number.LineSpacing) : lineHeight * 0.19;
        var captionBelow = caption.LineSpacing > 0 ? captionSize * (caption.LineSpacing - caption.Baseline) : captionSize * 0.25;
        return new Thickness(left, 0, 0, Math.Max(0, Math.Round(numberBelow - captionBelow, 1)));
    }
}
