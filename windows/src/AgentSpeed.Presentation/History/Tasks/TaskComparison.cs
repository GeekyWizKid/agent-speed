using AgentSpeed.Core;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>
/// The comparison values of a group of task records (METRICS 6.11, <i>Comparison</i>) with the counts behind each rate,
/// which the rates' tooltips name. Rates are null when their denominator is 0.
/// </summary>
/// <param name="Tasks">Records.</param>
/// <param name="ScoredTasks">Records with a score.</param>
/// <param name="MedianScore"><c>q(0.5)</c> of the unrounded scores; null without a scored record.</param>
/// <param name="Histogram">Ten counts: bins <c>[0, 10)</c> … <c>[80, 90)</c> and <c>[90, 100]</c>.</param>
/// <param name="Successes">Scored records that ended passing.</param>
/// <param name="Stalls">Records that stalled.</param>
/// <param name="FixFailures">Σ fix failures.</param>
/// <param name="FixAttempts">Σ fix attempts (failing runs).</param>
/// <param name="RecoverAttempts">The mean attempts of a recovered episode; null without one.</param>
/// <param name="TimeToFirstPassMs"><c>q(0.5)</c> of the time from a task's start to its first pass; null when none passed.</param>
public sealed record TaskStats(
    int Tasks,
    int ScoredTasks,
    double? MedianScore,
    double? LowerQuartile,
    double? UpperQuartile,
    IReadOnlyList<int> Histogram,
    int Successes,
    int Stalls,
    int FixFailures,
    int FixAttempts,
    double? RecoverAttempts,
    double? TimeToFirstPassMs)
{
    public const int Bins = 10;

    public static TaskStats Empty { get; } = new(0, 0, null, null, null, new int[Bins], 0, 0, 0, 0, null, null);

    public bool HasRecords => Tasks > 0;

    public double? SuccessRate => ScoredTasks > 0 ? (double)Successes / ScoredTasks : null;

    public double? StallRate => Tasks > 0 ? (double)Stalls / Tasks : null;

    public double? FixFailureRate => FixAttempts > 0 ? (double)FixFailures / FixAttempts : null;
}

/// <summary>A source's records in the range.</summary>
public sealed record TaskSourceStats(SourceKind Source, TaskStats Stats);

/// <summary>A model id's records across sources; <see cref="Model"/> is empty for an unknown model.</summary>
/// <param name="Sources">The sources with records of the model, in source order.</param>
public sealed record TaskModelStats(string Model, IReadOnlyList<SourceKind> Sources, TaskStats Stats);

/// <summary>The window range's comparison: per source in source order, per model ordered by tasks, then id.</summary>
public sealed record TaskComparison(IReadOnlyList<TaskSourceStats> BySource, IReadOnlyList<TaskModelStats> ByModel)
{
    public static TaskComparison Empty { get; } = new([], []);

    public TaskStats Of(SourceKind source) => BySource.FirstOrDefault(s => s.Source == source)?.Stats ?? TaskStats.Empty;
}
