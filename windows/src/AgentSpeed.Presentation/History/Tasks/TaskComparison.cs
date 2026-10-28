using AgentSpeed.Core;

namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>
/// The comparison values of a group of task records (METRICS 6.11, <i>Comparison</i>). Rates are null when their
/// denominator is 0; the numerators behind them (for the rates' tooltips, <c>96 / 128</c>) are null when the engine
/// does not provide them, and their tooltips are then left out.
/// </summary>
/// <param name="Tasks">Records.</param>
/// <param name="ScoredTasks">Records with a score.</param>
/// <param name="MedianScore"><c>q(0.5)</c> of the unrounded scores; null without a scored record.</param>
/// <param name="Histogram">Ten counts: bins <c>[0, 10)</c> … <c>[80, 90)</c> and <c>[90, 100]</c>.</param>
/// <param name="SuccessRate"><c>successes ÷ scoredTasks</c>.</param>
/// <param name="StallRate"><c>stalls ÷ tasks</c>.</param>
/// <param name="FixFailureRate"><c>fixFailures ÷ fixAttempts</c>.</param>
/// <param name="RecoverAttempts">The mean attempts of a recovered episode; null without one.</param>
/// <param name="TimeToFirstPassMs"><c>q(0.5)</c> of the time from a task's start to its first pass; null when none passed.</param>
/// <param name="Successes">Scored records that ended passing.</param>
/// <param name="Stalls">Records that stalled.</param>
/// <param name="FixFailures">Σ fix failures.</param>
/// <param name="FixAttempts">Σ fix attempts (failing runs).</param>
/// <param name="Recovered">Σ recovered episodes.</param>
public sealed record TaskStats(
    int Tasks,
    int ScoredTasks,
    double? MedianScore,
    double? LowerQuartile,
    double? UpperQuartile,
    IReadOnlyList<int> Histogram,
    double? SuccessRate,
    double? StallRate,
    double? FixFailureRate,
    double? RecoverAttempts,
    double? TimeToFirstPassMs,
    int? Successes = null,
    int? Stalls = null,
    int? FixFailures = null,
    int? FixAttempts = null,
    int? Recovered = null)
{
    public const int Bins = 10;

    public static TaskStats Empty { get; } = new(0, 0, null, null, null, new int[Bins], null, null, null, null, null);

    public bool HasRecords => Tasks > 0;

    /// <summary>Values from the numerators, with the rates they give (METRICS 6.11).</summary>
    public static TaskStats FromCounts(int tasks, int scoredTasks, double? medianScore, double? lowerQuartile, double? upperQuartile,
        IReadOnlyList<int> histogram, int successes, int stalls, int fixFailures, int fixAttempts, int recovered, int recoverAttempts,
        double? timeToFirstPassMs) =>
        new(tasks, scoredTasks, medianScore, lowerQuartile, upperQuartile, histogram,
            scoredTasks > 0 ? (double)successes / scoredTasks : null,
            tasks > 0 ? (double)stalls / tasks : null,
            fixAttempts > 0 ? (double)fixFailures / fixAttempts : null,
            recovered > 0 ? (double)recoverAttempts / recovered : null,
            timeToFirstPassMs, successes, stalls, fixFailures, fixAttempts, recovered);
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
