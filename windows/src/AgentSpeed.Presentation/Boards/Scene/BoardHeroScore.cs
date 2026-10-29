using System.Globalization;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>One end of glass's value arc on display: where it is (a share of the sweep) and how visible; alpha 0 draws none.</summary>
public readonly record struct BoardScoreTip(double At, double Alpha);

/// <summary>
/// Glass's value arc at one moment: solid from the start to <see cref="Settled"/>, then on to <see cref="Changing"/> at
/// <see cref="ChangingAlpha"/> (a crossfade's growing or receding stretch), with up to two tips (the old and the new end).
/// </summary>
public readonly record struct BoardScoreSweep(double Settled, double Changing, double ChangingAlpha, BoardScoreTip First, BoardScoreTip Second);

/// <summary>
/// The hero's 智力 over time (DESIGN 12, <i>Motion</i> and <i>Proof mark</i>; 2c, Updates, motion and quiet): a rise of 3 or more
/// counts up through every integer over <c>min(0.8, 0.3 + 0.012 Δ)</c> s with easeOutCubic timing, the arc's end and the meter
/// following the shown value; a rise of 1 or 2 replaces the digits; every fall, and a change to or from <c>–</c>, crossfades (0.2
/// s, 0.15 s); a change between live and resting crossfades the color (0.2 s, kept with Reduce Motion); the proof mark fades in
/// and out with the state (0.25 s; Reduce Motion: at once); Reduce Motion turns every value change into a 0.15 s crossfade.
/// Nothing runs between changes, and nothing allocates per frame.
/// </summary>
public sealed class BoardHeroScore
{
    private static readonly string[] Numbers =
        [.. Enumerable.Range(0, TaskScoreText.MaxScore + 1).Select(i => i.ToString(CultureInfo.InvariantCulture))];

    private readonly TaskScoreTicker _ticker = new(TaskScoreSurface.BoardHero);
    private TaskSetReading _reading = TaskSetReading.None;
    private bool _live;
    private bool _fromLive;
    private bool _fromUnproven;
    private double _formStart = double.NegativeInfinity;
    private bool _unproven;
    private double _proofStart = double.NegativeInfinity;

    /// <summary>The reading on display (its target, while a change runs).</summary>
    public TaskSetReading Reading => _reading;

    /// <summary>The change in progress or the last one.</summary>
    public TaskScoreChange Change => _ticker.Change;

    /// <summary>The number has a value (none draws <c>–</c>, no value arc and no tip).</summary>
    public bool HasScore => _reading.HasScore;

    /// <summary>Takes a tick's reading at <paramref name="now"/>; <paramref name="animate"/> false shows it at once.</summary>
    public void Apply(TaskSetReading reading, double now, bool animate, bool reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var live = reading.HasScore && reading.Form == TaskForm.Live;
        if (reading.Shown != _ticker.Target)
        {
            _fromLive = Liveness(now) >= 0.5;
            _fromUnproven = _reading.HasScore && Proof(now) >= 0.5;
        }

        _ticker.Set(reading.Shown, now, reduceMotion, animate);
        _formStart = animate && _reading.HasScore && reading.HasScore && live != _live ? now : _formStart;
        if (!animate)
        {
            _formStart = double.NegativeInfinity;
        }

        var unproven = reading.IsUnproven;
        if (unproven != _unproven)
        {
            _proofStart = animate && !reduceMotion ? now : double.NegativeInfinity;
        }

        _live = live;
        _unproven = unproven;
        _reading = reading;
    }

    /// <summary>Ends every change at once.</summary>
    public void Settle()
    {
        _ticker.Set(_ticker.Target, 0, true, animate: false);
        _formStart = double.NegativeInfinity;
        _proofStart = double.NegativeInfinity;
    }

    /// <summary>A count-up, a crossfade, the color's or the proof mark's fade runs (60 fps, in the roll class).</summary>
    public bool Animating(double now) =>
        _ticker.IsAnimating(now) || now - _formStart < TaskScoreMotion.FormSeconds || now - _proofStart < TaskProofMark.FadeSeconds;

    /// <summary>The integer on display (a count-up's intermediate value); null while none.</summary>
    public int? Shown(double now) => _ticker.Shown(now);

    /// <summary>The number's text at <paramref name="now"/> (cached: a count-up allocates nothing).</summary>
    public string Text(double now) => Number(Shown(now));

    /// <summary>The text fading out during a crossfade, else null.</summary>
    public string? Outgoing(double now) => _ticker.Change.Kind == TaskScoreChangeKind.Crossfade && _ticker.IsAnimating(now)
        ? Number(_ticker.Change.From)
        : null;

    /// <summary>The crossfade's opacities of the outgoing and the incoming number (0 and 1 outside a crossfade).</summary>
    public (double Outgoing, double Incoming) Fade(double now) => _ticker.Fade(now);

    /// <summary>The outgoing number was live, and carried the proof mark (its colors while it fades out).</summary>
    public bool OutgoingLive => _fromLive;

    public bool OutgoingUnproven => _fromUnproven;

    /// <summary>How live the colors are: 1 live, 0 resting or none, between them while the 0.2 s color crossfade runs.</summary>
    public double Liveness(double now)
    {
        var target = _live ? 1.0 : 0;
        var t = (now - _formStart) / TaskScoreMotion.FormSeconds;
        return t is >= 0 and < 1 ? (1 - target) + (2 * target - 1) * Easing.OutQuad(t) : target;
    }

    /// <summary>How present the proof mark is: 1 unproven, 0 proven or none, between them while it fades.</summary>
    public double Proof(double now)
    {
        var target = _unproven ? 1.0 : 0;
        var t = (now - _proofStart) / TaskProofMark.FadeSeconds;
        return t is >= 0 and < 1 ? (1 - target) + (2 * target - 1) * Easing.OutQuad(t) : target;
    }

    /// <summary>How live the arc, the tip and the meter look and how present their proof mark is: the outgoing value's while it fades to none.</summary>
    public (double Live, double Proof) Look(double now) =>
        HasScore ? (Liveness(now), Proof(now)) : _ticker.IsAnimating(now) ? (_fromLive ? 1 : 0, _fromUnproven ? 1 : 0) : (0, 0);

    /// <summary>Glass's value arc: the count-up's eased value, a crossfade's two ends, or the shown value.</summary>
    public BoardScoreSweep Sweep(double now)
    {
        var change = _ticker.Change;
        if (_ticker.IsAnimating(now))
        {
            if (change.Kind == TaskScoreChangeKind.Crossfade)
            {
                var t = _ticker.Fade(now).Incoming;
                var from = Share(change.From);
                var to = Share(change.To);
                if (from is { } a && to is { } b)
                {
                    return new BoardScoreSweep(Math.Min(a, b), Math.Max(a, b), b > a ? t : 1 - t, new BoardScoreTip(a, 1 - t), new BoardScoreTip(b, t));
                }

                if (to is { } incoming)
                {
                    return new BoardScoreSweep(0, incoming, t, default, new BoardScoreTip(incoming, t));
                }

                return from is { } outgoing ? new BoardScoreSweep(0, outgoing, 1 - t, new BoardScoreTip(outgoing, 1 - t), default) : default;
            }

            if (change.Kind == TaskScoreChangeKind.CountUp && change.From is { } start && change.To is { } end)
            {
                var value = (start + (end - start) * Easing.OutCubic(_ticker.Progress(now))) / TaskScoreText.MaxScore;
                return new BoardScoreSweep(value, value, 0, new BoardScoreTip(value, 1), default);
            }
        }

        return Share(change.To) is { } f ? new BoardScoreSweep(f, f, 0, new BoardScoreTip(f, 1), default) : default;
    }

    /// <summary>
    /// Neon's meter: the lit counts before and after a crossfade and its progress (a count-up lights the segments of the shown
    /// value; otherwise both counts are the target's).
    /// </summary>
    public (int From, int To, double Progress) Meter(double now)
    {
        var change = _ticker.Change;
        if (_ticker.IsAnimating(now) && change.Kind == TaskScoreChangeKind.Crossfade)
        {
            return (Lit(change.From), Lit(change.To), _ticker.Fade(now).Incoming);
        }

        var lit = Lit(Shown(now));
        return (lit, lit, 1);
    }

    /// <summary><c>lit = round(shown × 12 / 100)</c>: one segment per 8⅓ points.</summary>
    public static int Lit(int? shown) => shown is { } value
        ? Math.Clamp((int)Math.Round(value * (double)BoardGaugeSpec.Segments / TaskScoreText.MaxScore, MidpointRounding.AwayFromZero), 0,
            BoardGaugeSpec.Segments)
        : 0;

    /// <summary>The number as drawn: the cached integer, or <c>–</c>.</summary>
    public static string Number(int? shown) => shown is { } value and >= 0 and <= TaskScoreText.MaxScore ? Numbers[value] : TaskScoreText.Number(shown);

    private static double? Share(int? shown) => shown is { } value ? Math.Clamp(value / (double)TaskScoreText.MaxScore, 0, 1) : null;
}
