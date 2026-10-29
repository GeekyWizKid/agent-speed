using System.Globalization;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.Orb.Lines;

/// <summary>
/// The intelligence line's 智力 as a sample states it (DESIGN 12, <i>Values and forms</i>): the shown integer (null:
/// <c>–</c>), whether it takes the pale unit-token color (resting and none) and whether the proof mark goes under it.
/// </summary>
public readonly record struct OrbLineScore(int? Shown, bool Pale, bool Unproven)
{
    public static OrbLineScore None { get; } = new(null, true, false);

    public static OrbLineScore Of(TaskSetReading score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return new OrbLineScore(score.Shown, score.Form != TaskForm.Live, score.IsUnproven);
    }
}

/// <summary>One frame of the intelligence line's number.</summary>
/// <param name="Shown">The integer as drawn (mid count-up its current value); null: <c>–</c>.</param>
/// <param name="Alpha">Its opacity: the incoming side of a crossfade, else 1.</param>
/// <param name="Outgoing">The value a crossfade fades out (null: <c>–</c>), drawn while <paramref name="OutgoingAlpha"/> is above 0.</param>
/// <param name="Marked">The value the proof mark lies under: the number, or the one leaving while <c>–</c> comes in.</param>
/// <param name="Pale">0 in the number color, 1 in the unit token's color.</param>
/// <param name="Proof">The proof mark's opacity.</param>
public readonly record struct OrbScoreFrame(int? Shown, double Alpha, int? Outgoing, double OutgoingAlpha, int? Marked, double Pale, double Proof);

/// <summary>
/// How 智力 moves on the orb readout (DESIGN 12, <i>Motion</i>), kept as moments so any frame can be drawn at its time and a
/// still orb renders nothing: a rise of 3 or more counts up with easeOutCubic timing (<see cref="TaskScoreMotion"/>), a rise of
/// 1 or 2 replaces the digits, a fall and <c>–</c> ↔ a number crossfade; live ↔ resting crossfades the color in 0.2 s (kept
/// with Reduce Motion); the proof mark fades with the state in 0.25 s (Reduce Motion: at once). Under Reduce Motion every
/// number change is a 0.15 s crossfade from the value shown before. Times are seconds on the shared orb clock.
/// </summary>
public readonly record struct OrbScoreMotion
{
    private static readonly string[] Texts = BuildTexts();

    public int? Shown { get; private init; }

    public bool Pale { get; private init; }

    public bool Unproven { get; private init; }

    /// <summary>The last number change as drawn with motion, retargeted from the integer it had reached.</summary>
    private TaskScoreChange Change { get; init; }

    /// <summary>The same change under Reduce Motion: a crossfade from the value shown before.</summary>
    private TaskScoreChange Reduced { get; init; }

    /// <summary>When the number, the color and the mark last began to change; null: never, nothing moves.</summary>
    private double? ChangedAt { get; init; }

    private double? PaleFrom { get; init; }

    private double? ProofFrom { get; init; }

    /// <summary>The score as it first shows: no motion.</summary>
    public static OrbScoreMotion Start(OrbLineScore score) => new()
    {
        Shown = score.Shown,
        Pale = score.Pale,
        Unproven = score.Unproven,
        Change = TaskScoreChange.Still(score.Shown),
        Reduced = TaskScoreChange.Still(score.Shown),
    };

    /// <summary>The integer or <c>–</c> as text, without allocating for 0–100.</summary>
    public static string Text(int? value) =>
        value is not { } number ? TaskScoreText.Dash
        : number >= 0 && number < Texts.Length ? Texts[number]
        : number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Takes a sample's score at <paramref name="time"/>. A change during a count-up starts from the integer it has reached; a
    /// color or mark reversal part-way starts from where it stands.
    /// </summary>
    public OrbScoreMotion Next(OrbLineScore score, double time)
    {
        var next = this;
        if (score.Shown != Shown)
        {
            var drawn = ChangedAt is { } at ? Change.ValueAt(time - at) : Shown;
            next = next with
            {
                Shown = score.Shown,
                Change = TaskScoreMotion.Plan(drawn, score.Shown, TaskScoreSurface.Orb, reduceMotion: false),
                Reduced = TaskScoreMotion.Plan(Shown, score.Shown, TaskScoreSurface.Orb, reduceMotion: true),
                ChangedAt = time,
            };
        }

        if (score.Pale != Pale)
        {
            next = next with { Pale = score.Pale, PaleFrom = Reversal(time, PaleFrom, TaskScoreMotion.FormSeconds) };
        }

        if (score.Unproven != Unproven)
        {
            next = next with { Unproven = score.Unproven, ProofFrom = Reversal(time, ProofFrom, TaskProofMark.FadeSeconds) };
        }

        return next;
    }

    public OrbScoreFrame Frame(double time, bool reduceMotion)
    {
        var change = reduceMotion ? Reduced : Change;
        var elapsed = ChangedAt is { } at ? time - at : 1e9;
        var (outgoing, incoming) = change.Fade(elapsed);
        var shown = change.ValueAt(elapsed);
        var pale = Ramp(time, PaleFrom, TaskScoreMotion.FormSeconds);
        var proof = Ramp(time, ProofFrom, reduceMotion ? 0 : TaskProofMark.FadeSeconds);
        return new OrbScoreFrame(shown, incoming, change.From, outgoing, shown ?? change.From, Pale ? pale : 1 - pale,
            Unproven ? proof : 1 - proof);
    }

    /// <summary>
    /// A count-up, a crossfade, the color or the mark moves at <paramref name="time"/> (frames at the orb's 30 fps). Replacing
    /// digits and Reduce Motion's mark render the one frame of the sample.
    /// </summary>
    public bool IsMoving(double time, bool reduceMotion) =>
        (ChangedAt is { } at && (reduceMotion ? Reduced : Change).IsRunning(time - at))
        || Within(time, PaleFrom, TaskScoreMotion.FormSeconds)
        || (!reduceMotion && Within(time, ProofFrom, TaskProofMark.FadeSeconds));

    private static double Ramp(double time, double? start, double duration) =>
        start is not { } at ? 1 : duration > 0 ? Math.Clamp((time - at) / duration, 0, 1) : time >= at ? 1 : 0;

    private static bool Within(double time, double? start, double duration) => start is { } at && time >= at && time < at + duration;

    private static double Reversal(double time, double? start, double duration) => time - (1 - Ramp(time, start, duration)) * duration;

    private static string[] BuildTexts()
    {
        var texts = new string[TaskScoreText.MaxScore + 1];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        return texts;
    }
}
