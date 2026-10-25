using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>How a row springs to a new place per style (DESIGN 2c Rows: glass critically damped, settle ≈ 0.45 s; timing
/// damping 0.72, slight overshoot; neon 0.9).</summary>
public static class BoardRowSprings
{
    public static DampedSpring For(BoardStyle style, double initial) => style switch
    {
        BoardStyle.Timing => new DampedSpring(0.45, 0.72, initial),
        BoardStyle.Neon => new DampedSpring(0.45, 0.9, initial),
        _ => new DampedSpring(0.43, 1, initial),
    };
}

/// <summary>
/// One row's motion in a board (DESIGN 2c Rows): its place (a spring to the plan's y), entering (fade in 0.3 s from 10 pt
/// below), leaving (fade out 0.3 s), and the value's flash (a rise of more than 5 % flashes warm for 0.8 s, a fall dims it).
/// With Reduce Motion a row that moves crossfades at its new place instead of travelling.
/// </summary>
public sealed class BoardRowMotion
{
    public const double FadeSeconds = 0.3;
    public const double EnterOffset = 10;
    public const double FlashSeconds = 0.8;
    public const double DimSeconds = 0.5;
    public const double FlashThreshold = 0.05;

    /// <summary>The share underline's width transition (CSS <c>width .55s cubic-bezier(.2,.8,.2,1)</c>).</summary>
    public const double ShareSeconds = 0.55;

    private DampedSpring _y;
    private double _target;
    private double _enterStart = double.NegativeInfinity;
    private double _leaveStart = double.NaN;
    private double _flashStart = double.NegativeInfinity;
    private double _last = double.NaN;
    private double _shareFrom;
    private double _shareTo;
    private double _shareStart = double.NegativeInfinity;

    public BoardRowMotion(string key, BoardStyle style, BoardEntry entry, double now, bool appear)
    {
        Key = key;
        ValueKey = key + "#v";
        Entry = entry;
        _target = entry.Y;
        _y = BoardRowSprings.For(style, entry.Y);
        if (appear)
        {
            _enterStart = now;
        }
    }

    public string Key { get; }

    /// <summary>The layer key of the row's value (flashes are tinted on it only).</summary>
    public string ValueKey { get; }

    public BoardEntry Entry { get; private set; }

    public double Live { get; private set; }

    /// <summary>The row's share of the board's value (the underline), eased toward the latest tick's.</summary>
    public double Share(double now)
    {
        var t = (now - _shareStart) / ShareSeconds;
        return t is >= 0 and < 1 ? _shareFrom + (_shareTo - _shareFrom) * Easing.OutCubic(t) : _shareTo;
    }

    /// <summary>Sets the share of the latest tick; the underline eases to it unless motion is reduced.</summary>
    public void SetShare(double share, double now, bool animate)
    {
        share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
        if (Math.Abs(share - _shareTo) < 1e-6)
        {
            return;
        }

        _shareFrom = animate ? Share(now) : share;
        _shareTo = share;
        _shareStart = animate ? now : double.NegativeInfinity;
    }

    /// <summary>The underline is still easing.</summary>
    public bool Sharing(double now) => now - _shareStart < ShareSeconds;

    /// <summary>The flash: +1 warm, −1 dim, 0 none.</summary>
    public int FlashDirection { get; private set; }

    public bool Leaving => !double.IsNaN(_leaveStart);

    public double Y => _y.Value;

    public double Target => _target;

    public void Restyle(BoardStyle style) => _y = BoardRowSprings.For(style, _y.Value);

    /// <summary>A new tick's entry for this row (a new place, and a flash when its value changed by more than 5 %).</summary>
    public void Update(BoardEntry entry, double live, double now, bool reduceMotion)
    {
        Entry = entry;
        _leaveStart = double.NaN;
        if (Math.Abs(entry.Y - _target) > 0.01)
        {
            _target = entry.Y;
            if (reduceMotion)
            {
                _y.Snap(_target);
                _enterStart = now;
            }
        }

        if (double.IsFinite(_last) && _last > 0)
        {
            var change = (live - _last) / _last;
            if (Math.Abs(change) > FlashThreshold)
            {
                FlashDirection = change > 0 ? 1 : -1;
                _flashStart = now;
            }
        }

        _last = live;
        Live = live;
    }

    public void Leave(double now)
    {
        if (!Leaving)
        {
            _leaveStart = now;
        }
    }

    public void Step(double dt) => _y.Step(_target, dt);

    /// <summary>Opacity from entering and leaving.</summary>
    public double Alpha(double now)
    {
        var enter = Math.Clamp((now - _enterStart) / FadeSeconds, 0, 1);
        var leave = Leaving ? 1 - Math.Clamp((now - _leaveStart) / FadeSeconds, 0, 1) : 1;
        return Easing.OutCubic(enter) * leave;
    }

    /// <summary>The extra offset of an entering row (it rises 10 pt while fading in).</summary>
    public double EnterLift(double now, bool reduceMotion) =>
        reduceMotion ? 0 : EnterOffset * (1 - Easing.OutCubic(Math.Clamp((now - _enterStart) / FadeSeconds, 0, 1)));

    /// <summary>How far the value's flash is, 0 at its start and 1 when it ended.</summary>
    public double FlashProgress(double now) =>
        Math.Clamp((now - _flashStart) / (FlashDirection < 0 ? DimSeconds : FlashSeconds), 0, 1);

    public bool Gone(double now) => Leaving && now - _leaveStart >= FadeSeconds;

    /// <summary>Whether the row still needs frames (moving, fading or flashing).</summary>
    public bool Animating(double now) => Moving(now) || Flashing(now);

    /// <summary>Moving to its place, entering or leaving (DESIGN 2c Frame policy: a row move needs 60 fps).</summary>
    public bool Moving(double now) => !_y.IsSettled(_target, 0.05) || now - _enterStart < FadeSeconds || Leaving;

    /// <summary>Its value flashes warm or dims, or its underline eases (no travel: 30 fps is enough).</summary>
    public bool Flashing(double now) => FlashProgress(now) < 1 || Sharing(now);

    public void Settle()
    {
        _y.Snap(_target);
        _enterStart = double.NegativeInfinity;
        _flashStart = double.NegativeInfinity;
        _shareStart = double.NegativeInfinity;
    }
}
