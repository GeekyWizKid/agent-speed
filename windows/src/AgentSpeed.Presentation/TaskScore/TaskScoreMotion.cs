using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.TaskScore;

/// <summary>Where a score is drawn: each surface has its own climb (DESIGN 12, <i>Motion</i>).</summary>
public enum TaskScoreSurface
{
    /// <summary>The orb readout: counts up at 30 fps.</summary>
    Orb,

    /// <summary>The board hero: counts up in the roll class (60 fps); the arc's end and the meter follow the shown value.</summary>
    BoardHero,

    /// <summary>The open score card's number: counts up.</summary>
    Card,

    /// <summary>The dashboard agent row: rolls its changed digits upward.</summary>
    DashboardRow,

    /// <summary>Board rows: cached content, digits replaced in place.</summary>
    BoardRow,

    /// <summary>The taskbar block: a status item, digits replaced in place.</summary>
    Taskbar,
}

public enum TaskScoreChangeKind
{
    None,

    /// <summary>The digits change in place, in one frame.</summary>
    Replace,

    /// <summary>The integer counts up through every intermediate value with easeOutCubic timing.</summary>
    CountUp,

    /// <summary>The changed digits roll upward (0.24 s, 1.4 DIP blur on entering digits).</summary>
    Roll,

    /// <summary>An opacity change: every fall, every change under Reduce Motion, and appearing from or going to <c>–</c>.</summary>
    Crossfade,
}

/// <summary>How one change of the shown integer animates.</summary>
public readonly record struct TaskScoreChange(TaskScoreChangeKind Kind, int? From, int? To, double Duration)
{
    public static TaskScoreChange Still(int? value) => new(TaskScoreChangeKind.None, value, value, 0);

    /// <summary>Linear progress 0..1 at <paramref name="elapsed"/> seconds.</summary>
    public double Progress(double elapsed) => Duration > 0 ? Easing.Clamp01(elapsed / Duration) : 1;

    /// <summary>The integer to draw at <paramref name="elapsed"/>: a count-up's intermediate value, otherwise the target.</summary>
    public int? ValueAt(double elapsed)
    {
        if (Kind != TaskScoreChangeKind.CountUp || From is not { } from || To is not { } to)
        {
            return To;
        }

        return (int)Math.Round(from + (to - from) * Easing.OutCubic(Progress(elapsed)), MidpointRounding.AwayFromZero);
    }

    /// <summary>A crossfade's opacities of the outgoing and the incoming value.</summary>
    public (double Outgoing, double Incoming) Fade(double elapsed)
    {
        if (Kind != TaskScoreChangeKind.Crossfade)
        {
            return (0, 1);
        }

        var t = Easing.OutQuad(Progress(elapsed));
        return (1 - t, t);
    }

    public bool IsRunning(double elapsed) => Kind is not (TaskScoreChangeKind.None or TaskScoreChangeKind.Replace) && elapsed < Duration;
}

/// <summary>The score's motion rules (DESIGN 12, <i>Motion</i>): calm drops, quick climbs, nothing continuous.</summary>
public static class TaskScoreMotion
{
    /// <summary>A rise of at least this much climbs; a rise of 1 or 2 replaces the digits everywhere.</summary>
    public const int ClimbThreshold = 3;

    public const double CountUpBase = 0.3;
    public const double CountUpPerPoint = 0.012;
    public const double CountUpMax = 0.8;

    /// <summary>The dashboard row's roll: the board hero's 0.24 s curve with a 1.4 DIP blur on entering digits.</summary>
    public const double RollSeconds = 0.24;

    public const double RollBlur = 1.4;

    /// <summary>Any drop: replaced in place with a 0.2 s crossfade; never a downward roll, a color change, a shake or a flash.</summary>
    public const double FallSeconds = 0.2;

    /// <summary>Live ↔ resting crossfades the color in 0.2 s, kept with Reduce Motion.</summary>
    public const double FormSeconds = 0.2;

    /// <summary>Reduce Motion: every change is a 0.15 s crossfade.</summary>
    public const double ReducedSeconds = 0.15;

    /// <summary>A count-up's frame rates: orbs at 30 fps, boards in the roll class at 60 fps.</summary>
    public const double OrbFps = 30;

    public const double BoardFps = 60;

    /// <summary><c>min(0.8, 0.3 + 0.012 Δ)</c> seconds: a test pass of +40 lands in 0.78 s.</summary>
    public static double CountUpDuration(int rise) => Math.Min(CountUpMax, CountUpBase + CountUpPerPoint * Math.Max(0, rise));

    public static TaskScoreChange Plan(int? from, int? to, TaskScoreSurface surface, bool reduceMotion)
    {
        if (from == to)
        {
            return TaskScoreChange.Still(to);
        }

        if (reduceMotion || from is null || to is null)
        {
            return new TaskScoreChange(TaskScoreChangeKind.Crossfade, from, to, ReducedSeconds);
        }

        var delta = to.Value - from.Value;
        if (delta < 0)
        {
            return new TaskScoreChange(TaskScoreChangeKind.Crossfade, from, to, FallSeconds);
        }

        if (delta < ClimbThreshold)
        {
            return new TaskScoreChange(TaskScoreChangeKind.Replace, from, to, 0);
        }

        return surface switch
        {
            TaskScoreSurface.Orb or TaskScoreSurface.BoardHero or TaskScoreSurface.Card =>
                new TaskScoreChange(TaskScoreChangeKind.CountUp, from, to, CountUpDuration(delta)),
            TaskScoreSurface.DashboardRow => new TaskScoreChange(TaskScoreChangeKind.Roll, from, to, RollSeconds),
            _ => new TaskScoreChange(TaskScoreChangeKind.Replace, from, to, 0),
        };
    }
}

/// <summary>
/// One surface's shown score over time: plans each change and retargets a running count-up from the integer on display,
/// so a value that arrives mid-climb never jumps back. Frames are needed only while <see cref="IsAnimating"/>.
/// </summary>
public sealed class TaskScoreTicker(TaskScoreSurface surface)
{
    private TaskScoreChange _change = TaskScoreChange.Still(null);
    private double _start = double.NegativeInfinity;
    private bool _initialised;

    public TaskScoreSurface Surface { get; } = surface;

    public TaskScoreChange Change => _change;

    public int? Target => _change.To;

    /// <summary>Takes a new shown value at <paramref name="now"/> seconds; the first value (or <paramref name="animate"/> false) shows at once.</summary>
    public void Set(int? value, double now, bool reduceMotion, bool animate = true)
    {
        if (_initialised && value == _change.To)
        {
            return;
        }

        if (!_initialised || !animate)
        {
            _initialised = true;
            _change = TaskScoreChange.Still(value);
            _start = now;
            return;
        }

        var from = Shown(now);
        _change = TaskScoreMotion.Plan(from, value, Surface, reduceMotion);
        _start = now;
    }

    /// <summary>The integer to draw at <paramref name="now"/>.</summary>
    public int? Shown(double now) => _change.ValueAt(now - _start);

    /// <summary>The crossfade's opacities at <paramref name="now"/> (outgoing 0 and incoming 1 outside a crossfade).</summary>
    public (double Outgoing, double Incoming) Fade(double now) => _change.Fade(now - _start);

    /// <summary>The value fading out during a crossfade, else null.</summary>
    public int? Outgoing(double now) =>
        _change.Kind == TaskScoreChangeKind.Crossfade && _change.IsRunning(now - _start) ? _change.From : null;

    public bool IsAnimating(double now) => _change.IsRunning(now - _start);

    /// <summary>Linear progress of the running change (1 when settled).</summary>
    public double Progress(double now) => _change.Progress(now - _start);
}
