namespace AgentSpeed.Core.Tasks;

/// <summary>
/// Everything METRICS 6.9 derives from one task's events at one instant (the reference's <c>Evaluation</c> and
/// <c>task_result</c>): the state, the score and its five dimensions, <c>IG</c>, <c>L</c>, <c>O</c>, the counts of 6.11
/// and the facts of the score card. Immutable once made.
/// </summary>
public sealed partial class TaskEvaluation
{
    private readonly TaskActs _source;

    /// <summary>The number of leading acts of <see cref="_source"/> this evaluation reads.</summary>
    private readonly int _length;

    private readonly int _countedCount;

    private TaskEvaluation(TaskActs source, int length)
    {
        _source = source;
        _length = length;

        var effective = 0;
        var oscillations = 0;
        var counted = 0;
        var weights = 0L;
        var gains = 0L;
        for (var i = 0; i < length; i++)
        {
            var act = source.Acts[i];
            if (act.Effective)
            {
                effective++;
                if (act.Oscillating)
                {
                    oscillations++;
                }
            }

            if (act.Weight > 0)
            {
                counted++;
                weights += act.Weight;
                gains += act.Weight * act.Gain;
            }
        }

        _countedCount = counted;
        Edits = effective;
        Oscillations = oscillations;
        Osc = effective > 0 ? (double)oscillations / effective : 0.0;
        Ig = weights > 0 ? (double)gains / weights : 0.0;
        LoopActions = TaskLoops.MarkedCount(source.CountedKeys, counted);
        Loop = counted > 0 ? (double)LoopActions / counted : 0.0;
        StallEnds = PrefixStallEnds(source.StallEnds, counted);

        Analyze();
        Dims = Dimensions();
        Score = State == TaskState.None ? null : Dims.Sum;
        Shown = Score is { } score ? TaskRounding.HalfAwayFromZero(score) : null;
        Counts = CountsOf();
        FirstPassMs = FirstPass();
        VHeld = HeldV();
        OpenFailure = OpenFailureFacts();
        LastPass = LastPassFacts();
    }

    /// <summary>
    /// <c>evaluate</c>: the task made of <paramref name="events"/> (prompts are skipped) at <paramref name="nowMs"/>: actions
    /// with <c>t</c> after it are left out and outcomes read after it are not read yet. Null evaluates everything.
    /// </summary>
    public static TaskEvaluation Of(IReadOnlyList<TaskEvent> events, long? nowMs = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var acts = TaskActs.Of(events, 0, events.Count, nowMs);
        return new TaskEvaluation(acts, acts.Acts.Count);
    }

    internal static TaskEvaluation Of(TaskActs acts) => new(acts, acts.Acts.Count);

    public TaskState State { get; private set; }

    /// <summary>V + C + R + E + K, unrounded; null when the state is <see cref="TaskState.None"/>.</summary>
    public double? Score { get; }

    /// <summary>The score rounded half away from zero.</summary>
    public int? Shown { get; }

    public TaskDims Dims { get; }

    /// <summary>Information gain <c>IG = Σ w·ΔI / Σ w</c>.</summary>
    public double Ig { get; }

    /// <summary>Loop share <c>L</c>.</summary>
    public double Loop { get; }

    /// <summary>Oscillation share <c>O</c>.</summary>
    public double Osc { get; }

    public TaskCounts Counts { get; }

    /// <summary>The earliest outcome time of a passing run.</summary>
    public long? FirstPassMs { get; }

    /// <summary>1-based counted indices that end a stall window (6.11 <c>stalled</c>).</summary>
    public IReadOnlyList<int> StallEnds { get; }

    /// <summary><c>pending</c> only: the V a passing re-run restores.</summary>
    public double? VHeld { get; }

    /// <summary><c>failing</c> only.</summary>
    public TaskOpenFailure? OpenFailure { get; }

    /// <summary><c>passing</c> and <c>pending</c> only.</summary>
    public TaskLastPass? LastPass { get; }

    /// <summary><c>n</c>, the counted actions.</summary>
    public int CountedActions => _countedCount;

    /// <summary>Effective edits.</summary>
    public int Edits { get; }

    /// <summary>Oscillating effective edits.</summary>
    public int Oscillations { get; }

    /// <summary>Counted actions marked as loops.</summary>
    public int LoopActions { get; }

    /// <summary>The 6.11 features of this evaluation (a snapshot, or the live current task).</summary>
    public TaskFeatures Features => TaskFeatures.Of(this);

    internal TaskActs Source => _source;

    /// <summary>The evaluation of the first <paramref name="length"/> acts of the same task at the same instant.</summary>
    internal TaskEvaluation Prefix(int length) => length == _length ? this : new TaskEvaluation(_source, length);

    private static IReadOnlyList<int> PrefixStallEnds(List<int> all, int counted)
    {
        var count = 0;
        while (count < all.Count && all[count] <= counted)
        {
            count++;
        }

        return count == all.Count ? all.AsReadOnly() : all.GetRange(0, count).AsReadOnly();
    }
}
