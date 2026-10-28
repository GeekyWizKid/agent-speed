using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Traffic;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Orb.Lines;

/// <summary>The readout's samples in each mode (DESIGN 10, Floating orb readout and Intelligence readout).</summary>
public static class OrbLinesText
{
    /// <summary>Traffic counts as activity from this ↓ + ↑ rate on (bytes per second).</summary>
    public const double LoudTraffic = 1000;

    public const string Down = "↓";
    public const string Up = "↑";

    public static OrbLinesSample Traffic(TrafficRate rate, bool bits, Localizer localizer)
    {
        var text = new TrafficText(localizer, bits);
        var rx = text.Rate(rate.Rx);
        var tx = text.Rate(rate.Tx);
        var content = new OrbLinesContent(
            OrbLinesKind.Traffic, new OrbLine(Down, rx.Number, rx.Unit), new OrbLine(Up, tx.Number, tx.Unit), bits, text.SpokenRates(rate));
        return new OrbLinesSample(OrbLinesKind.Traffic, content, rate.Sum >= LoudTraffic);
    }

    /// <summary>
    /// The intelligence line (integer index) and the thinking line (<c>liveThinking</c> tok/s, hidden after 3 s at 0); quiet
    /// without output in the window or a generating agent. With nothing generated the last shown values stay while the
    /// readout fades.
    /// </summary>
    public static OrbLinesSample Intelligence(IntelligenceSums sums, bool generating, Localizer localizer)
    {
        if (sums.Shown is not { } index)
        {
            return new OrbLinesSample(OrbLinesKind.Intelligence, null, generating);
        }

        var shown = index.ToString(localizer.Culture);
        var thinking = SpeedFormat.Speed(sums.LiveThinking);
        var content = new OrbLinesContent(
            OrbLinesKind.Intelligence,
            new OrbLine(localizer["intel.glyph"], shown, string.Empty),
            new OrbLine(localizer["intel.thinking.glyph"], thinking, localizer["unit.tokS"]),
            false,
            localizer.Format("a11y.orb.intel", shown, thinking),
            localizer.Format("intel.tooltip", shown, HistoryText.Percent(sums.ThinkingShare)) + "\n" + localizer["intel.disclaimer"])
        {
            ThinkingZero = ThinkingZeroHold.ReadsZero(sums.LiveThinking),
            SpokenAlone = localizer.Format("a11y.orb.intel.only", shown),
        };
        return new OrbLinesSample(OrbLinesKind.Intelligence, content, true);
    }
}
