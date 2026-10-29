using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Reporting;

/// <summary>What one completed task's 6.11 record gives the task ledger (METRICS 11.10, Counting).</summary>
/// <param name="Actions">The task's actions; a task without any has no record and adds nothing.</param>
/// <param name="Score">The task's score, unrounded; null without one.</param>
public readonly record struct ReportTaskRecord(
    int Actions, double? Score, bool Success, bool Stalled, int FixAttempts, int FixFailures, int Recovered, int RecoverAttempts)
{
    /// <summary>The record of a completed task's evaluation: its counts, score, success and stall.</summary>
    public static ReportTaskRecord Of(TaskEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var counts = evaluation.Counts;
        return new ReportTaskRecord(counts.Actions, evaluation.Score, evaluation.State == TaskState.Passing, evaluation.StallEnds.Count > 0,
            counts.FixAttempts, counts.FixFailures, counts.Recovered, counts.RecoverAttempts);
    }
}

/// <summary>
/// One <c>taskStats</c> row (METRICS 11.10, Task statistics): the counted tasks of one model and source, summed. Rows add up
/// field by field; within a watch generation they only grow, so a difference of two is a window's rows.
/// </summary>
public sealed class ReportTaskRow : IEquatable<ReportTaskRow>
{
    public const int Bins = 10;

    /// <summary>The schema's bound on the fix and recovery sums.</summary>
    public const long MaxSum = 1_000_000;

    private readonly long[] _bins;

    public ReportTaskRow(long completed, long scored, long scoreSum, IReadOnlyList<long> scoreBins, long successes, long stalls, long fixAttempts,
        long fixFailures, long recovered, long recoverAttempts)
    {
        ArgumentNullException.ThrowIfNull(scoreBins);
        if (scoreBins.Count != Bins)
        {
            throw new ArgumentException("ten bins", nameof(scoreBins));
        }

        Completed = completed;
        Scored = scored;
        ScoreSum = scoreSum;
        _bins = [.. scoreBins];
        Successes = successes;
        Stalls = stalls;
        FixAttempts = fixAttempts;
        FixFailures = fixFailures;
        Recovered = recovered;
        RecoverAttempts = recoverAttempts;
    }

    public static ReportTaskRow Zero { get; } = new(0, 0, 0, new long[Bins], 0, 0, 0, 0, 0, 0);

    public long Completed { get; }

    public long Scored { get; }

    /// <summary>Σ of the scores rounded half away from zero, as shown.</summary>
    public long ScoreSum { get; }

    /// <summary>Counts of the rounded scores in <c>[0, 10)</c> … <c>[80, 90)</c> and <c>[90, 100]</c>.</summary>
    public IReadOnlyList<long> ScoreBins => _bins;

    public long Successes { get; }

    public long Stalls { get; }

    public long FixAttempts { get; }

    public long FixFailures { get; }

    public long Recovered { get; }

    public long RecoverAttempts { get; }

    /// <summary>What one completed task adds; null for a task without actions (no 6.11 record).</summary>
    public static ReportTaskRow? Of(ReportTaskRecord record)
    {
        if (record.Actions < 1)
        {
            return null;
        }

        var bins = new long[Bins];
        long scored = 0, sum = 0, successes = 0;
        if (record.Score is { } score)
        {
            var shown = Math.Clamp(TaskRounding.HalfAwayFromZero(score), 0, 100);
            scored = 1;
            sum = shown;
            bins[Math.Min(Bins - 1, shown / 10)] = 1;
            successes = record.Success ? 1 : 0;
        }

        return new ReportTaskRow(1, scored, sum, bins, successes, record.Stalled ? 1 : 0, record.FixAttempts, record.FixFailures, record.Recovered,
            record.RecoverAttempts);
    }

    public static ReportTaskRow operator +(ReportTaskRow a, ReportTaskRow b) => Combine(a, b, 1);

    /// <summary>Field-wise difference, never negative.</summary>
    public static ReportTaskRow operator -(ReportTaskRow a, ReportTaskRow b) => Combine(a, b, -1);

    public ReportJson Json(SourceKind source) => ReportJson.Object(
        ("source", ReportJson.Of(ReportBody.SourceId(source))),
        ("completed", ReportJson.Of(Completed)),
        ("scored", ReportJson.Of(Scored)),
        ("scoreSum", ReportJson.Of(ScoreSum)),
        ("scoreBins", ReportJson.Array(_bins.Select(ReportJson.Of))),
        ("successes", ReportJson.Of(Successes)),
        ("stalls", ReportJson.Of(Stalls)),
        ("fixAttempts", ReportJson.Of(Math.Min(FixAttempts, MaxSum))),
        ("fixFailures", ReportJson.Of(Math.Min(FixFailures, MaxSum))),
        ("recovered", ReportJson.Of(Math.Min(Recovered, MaxSum))),
        ("recoverAttempts", ReportJson.Of(Math.Min(RecoverAttempts, MaxSum))));

    public bool Equals(ReportTaskRow? other) =>
        other is not null && Completed == other.Completed && Scored == other.Scored && ScoreSum == other.ScoreSum && _bins.SequenceEqual(other._bins)
        && Successes == other.Successes && Stalls == other.Stalls && FixAttempts == other.FixAttempts && FixFailures == other.FixFailures
        && Recovered == other.Recovered && RecoverAttempts == other.RecoverAttempts;

    public override bool Equals(object? obj) => Equals(obj as ReportTaskRow);

    public override int GetHashCode() => HashCode.Combine(Completed, Scored, ScoreSum, Successes, Stalls, FixAttempts, Recovered);

    private static ReportTaskRow Combine(ReportTaskRow a, ReportTaskRow b, int sign)
    {
        static long F(long x, long y, int s) => Math.Max(0, x + s * y);
        var bins = new long[Bins];
        for (var i = 0; i < Bins; i++)
        {
            bins[i] = F(a._bins[i], b._bins[i], sign);
        }

        return new ReportTaskRow(F(a.Completed, b.Completed, sign), F(a.Scored, b.Scored, sign), F(a.ScoreSum, b.ScoreSum, sign), bins,
            F(a.Successes, b.Successes, sign), F(a.Stalls, b.Stalls, sign), F(a.FixAttempts, b.FixAttempts, sign),
            F(a.FixFailures, b.FixFailures, sign), F(a.Recovered, b.Recovered, sign), F(a.RecoverAttempts, b.RecoverAttempts, sign));
    }
}

/// <summary>
/// A <c>taskScore</c> object (METRICS 11.10, Body): a set's highest task score with its form (6.9 <i>Values</i>) and its live
/// thinking speed, unrounded until written.
/// </summary>
public readonly record struct ReportTaskValue(TaskValueForm Form, double? Score, double Thinking)
{
    /// <summary>The set rule: the highest live score, else the highest resting score, else none.</summary>
    public static ReportTaskValue Of(IEnumerable<ReportTaskAgent> agents, double thinking)
    {
        ArgumentNullException.ThrowIfNull(agents);
        double? live = null, resting = null;
        foreach (var agent in agents)
        {
            switch (agent.Form)
            {
                case TaskValueForm.Live:
                    live = Math.Max(live ?? double.MinValue, agent.Score);
                    break;
                case TaskValueForm.Resting:
                    resting = Math.Max(resting ?? double.MinValue, agent.Score);
                    break;
            }
        }

        return live is { } l ? new(TaskValueForm.Live, l, thinking)
            : resting is { } r ? new(TaskValueForm.Resting, r, thinking)
            : new(TaskValueForm.None, null, thinking);
    }

    /// <summary>The score as sent: 0–100, rounded half away from zero; null for none.</summary>
    public int? Shown => Form == TaskValueForm.None || Score is not { } score ? null : Math.Clamp(TaskRounding.HalfAwayFromZero(score), 0, 100);

    /// <summary><c>thinkingTokS</c> as sent (METRICS 11.7).</summary>
    public double ThinkingTokS => ReportJson.Round(Math.Clamp(double.IsFinite(Thinking) ? Thinking : 0, 0, ReportBody.MaxTokS), 1);

    public string FormName => Form switch
    {
        TaskValueForm.Live => "live",
        TaskValueForm.Resting => "resting",
        _ => "none",
    };

    public ReportJson Json => ReportJson.Object(
        ("score", ReportJson.OfOptional(Shown)),
        ("form", ReportJson.Of(Shown is null ? "none" : FormName)),
        ("thinkingTokS", ReportJson.Decimal(ThinkingTokS, 1)));

    /// <summary>The value rounded as sent, for the WebSocket changed rule.</summary>
    public (int? Score, string Form, double Thinking) Sent => (Shown, Shown is null ? "none" : FormName, ThinkingTokS);
}

/// <summary>An agent of the all-sources set with a score (METRICS 11.10): its source, current model's report id, form and score.</summary>
public readonly record struct ReportTaskAgent(SourceKind Source, string ModelId, TaskValueForm Form, double Score);

/// <summary>
/// The task score's live facts of a tick (METRICS 11.10): the agents of the all-sources set that have a score, the set's
/// thinking speed and every report id's thinking speed (its raw models' model sums added).
/// </summary>
public sealed record ReportTaskFacts(IReadOnlyList<ReportTaskAgent> Agents, double Thinking, IReadOnlyDictionary<string, double> ModelThinking)
{
    public static ReportTaskFacts Empty { get; } = new([], 0, new Dictionary<string, double>(StringComparer.Ordinal));

    /// <summary>The top-level value: the highest of every agent, with the all-sources thinking.</summary>
    public ReportTaskValue Top => ReportTaskValue.Of(Agents, Thinking);
}
