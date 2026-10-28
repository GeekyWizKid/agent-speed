using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Formatting;

/// <summary>The taskbar intelligence block's cells (DIP), left to right: label, number, unit.</summary>
public readonly record struct BarIntelligenceCells(double Label, double Number, double Unit)
{
    public const double LabelGap = 3;
    public const double UnitGap = 3;

    /// <summary>The block's width without the gap before it.</summary>
    public double Width => Label + LabelGap + Number + UnitGap + Unit;
}

/// <summary>
/// The taskbar intelligence block (DESIGN 10, Menu bar and taskbar): after the value, <c>智力 125</c> over
/// <c>思考 10 tok/s</c> — the live intelligence of all sources (METRICS 6.9). Labels sit in a column, both numbers
/// right-aligned in one cell as wide as <c>8.88k</c>, so the block's width depends on the language only. It never hides
/// while chosen: with nothing generated in the window both numbers read <c>–</c> and <c>tok/s</c> stays. A thinking rate
/// that reads 0 for 3 s under a live value leaves line 1 alone, centred in the same cells. It needs neither network
/// measuring nor the helper.
/// </summary>
/// <param name="ThinkingDim">The thinking number takes the secondary color: <c>–</c>, or a zero rate under a live value.</param>
/// <param name="Spoken">
/// <c>a11y.orb.intel</c>, <c>a11y.orb.intel.only</c> with line 2 hidden, or <c>intel.none</c> while quiet: read after the
/// value and in the tooltip.
/// </param>
public sealed record BarIntelligenceBlock(
    string Label, string ThinkingLabel, string Index, string Thinking, string Unit, bool Quiet, bool ThinkingDim, string Spoken)
{
    public const string Dash = "–";

    /// <summary>The widest METRICS 7 form below 1 M tok/s; <c>888</c> fits in it too.</summary>
    public const string WidestNumber = "8.88k";

    /// <summary>The language whose UI face draws the labels.</summary>
    public string LanguageCode { get; init; } = LanguageCodes.ZhHant;

    /// <summary>Line 2 is not drawn: its rate has read 0 for 3 s under a live value (DESIGN 10, Thinking hidden at zero).</summary>
    public bool ThinkingHidden { get; init; }

    /// <summary>The thinking rate reads 0 under a live value: line 2 hides after its hold.</summary>
    public bool ThinkingZero => !Quiet && ThinkingDim;

    /// <summary>Null unless the intelligence block is chosen and the widget shows a value (not glyph only).</summary>
    public static BarIntelligenceBlock? From(AppSettings settings, SpeedSnapshot snapshot, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshot);
        return settings.Traffic.Bar != BarTraffic.Intelligence || settings.BarMode == BarDisplayMode.GlyphOnly
            ? null
            : Of(OrbSubset.Reading(snapshot, null).Intelligence, localizer);
    }

    /// <summary>
    /// The block with the thinking line's hold applied at <paramref name="time"/> (seconds): line 2 hidden once its rate has
    /// read 0 for 3 s under a live value. Feed every update through the same <paramref name="hold"/>.
    /// </summary>
    public static BarIntelligenceBlock? From(AppSettings settings, SpeedSnapshot snapshot, Localizer localizer, ThinkingZeroHold hold,
        double time)
    {
        ArgumentNullException.ThrowIfNull(hold);
        var block = From(settings, snapshot, localizer);
        return hold.Hides(block?.ThinkingZero == true, time) ? block!.HidingThinking(localizer) : block;
    }

    public static BarIntelligenceBlock Of(IntelligenceSums sums, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var label = localizer["intel.glyph"];
        var thinkingLabel = localizer["intel.thinking.glyph"];
        var unit = localizer["unit.tokS"];
        if (sums.Shown is not { } shown)
        {
            return new BarIntelligenceBlock(label, thinkingLabel, Dash, Dash, unit, Quiet: true, ThinkingDim: true, localizer["intel.none"])
            {
                LanguageCode = localizer.LanguageCode,
            };
        }

        var index = shown.ToString(localizer.Culture);
        var zero = ThinkingZeroHold.ReadsZero(sums.LiveThinking);
        var thinking = zero ? "0" : SpeedFormat.Speed(sums.LiveThinking);
        return new BarIntelligenceBlock(label, thinkingLabel, index, thinking, unit, Quiet: false, zero,
            localizer.Format("a11y.orb.intel", index, thinking)) { LanguageCode = localizer.LanguageCode };
    }

    /// <summary>Line 1 alone: line 2 not drawn and the label reads <c>a11y.orb.intel.only</c>; a block whose rate is not 0 stays.</summary>
    public BarIntelligenceBlock HidingThinking(Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return ThinkingZero ? this with { ThinkingHidden = true, Spoken = localizer.Format("a11y.orb.intel.only", Index) } : this;
    }

    /// <summary>
    /// Line 1's baseline in a block centred on <paramref name="centerY"/> with lines <paramref name="pitch"/> apart (line 2
    /// one pitch below it): alone it sits halfway between the two baselines, its cap height centred on the item's text centre.
    /// </summary>
    public double FirstBaseline(double centerY, double capHeight, double pitch) =>
        ThinkingHidden ? centerY + capHeight / 2 : centerY - pitch / 2 + capHeight / 2;

    /// <summary>
    /// The cells for a measure of text and of tabular numbers: the number cell is <see cref="WidestNumber"/> wide unless a
    /// number is wider (1 M tok/s or more), so it never changes with the values below that.
    /// </summary>
    public BarIntelligenceCells Cells(Func<string, double> text, Func<string, double> number)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(number);
        return new BarIntelligenceCells(
            Math.Max(text(Label), text(ThinkingLabel)),
            Math.Max(number(WidestNumber), Math.Max(number(Index), number(Thinking))),
            text(Unit));
    }
}
