using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// <c>openFailure</c> (METRICS 6.9 <i>Task facts</i>): the newest failing run that is still the last element of its check's
/// sequence — its suite (null when unknown), class and key; <see cref="Errors"/>, its <c>F</c>; <see cref="Peak"/>, its
/// check's <c>F_peak</c>; <see cref="Counted"/>, whether <c>F</c> comes from fingerprints or a <c>failedCount</c> above 0
/// rather than the minimum of 1; <see cref="Edited"/>, whether an effective edit followed it.
/// </summary>
public sealed record TaskOpenFailure(string? Suite, CommandClass Class, string Key, long Errors, long Peak, bool Counted, bool Edited);

/// <summary>
/// <c>lastPass</c> (METRICS 6.9 <i>Task facts</i>): the last run of the current block (<c>passing</c>) or of the latest
/// non-empty block (<c>pending</c>); <see cref="BuildOnly"/> when that block has no passing test run.
/// </summary>
public sealed record TaskLastPass(string? Suite, CommandClass Class, string Key, bool BuildOnly);

/// <summary>The counts and facts of a task (the reference's <c>counts</c>, <c>first_pass_ms</c> and <c>*_facts</c>).</summary>
public sealed partial class TaskEvaluation
{
    private TaskCounts CountsOf()
    {
        var recovered = 0;
        var recoverAttempts = 0;
        foreach (var episode in _episodes)
        {
            if (episode.Recovered)
            {
                recovered++;
                recoverAttempts += episode.Fails.Count;
            }
        }

        var fixAttempts = 0;
        var fixFailures = 0;
        for (var r = 0; r < _runCount; r++)
        {
            var run = Run(r);
            if (run.Result == RunResult.Fail)
            {
                fixAttempts++;
                fixFailures += FixLabel(run);
            }
        }

        return new TaskCounts(
            Actions: _length,
            Counted: _countedCount,
            Edits: Edits,
            Oscillations: Oscillations,
            LoopActions: LoopActions,
            Runs: _runCount,
            FailingRuns: fixAttempts,
            Episodes: _episodes.Count,
            Recovered: recovered,
            RecoverAttempts: recoverAttempts,
            FixAttempts: fixAttempts,
            FixFailures: fixFailures);
    }

    private long? FirstPass()
    {
        long? first = null;
        for (var r = 0; r < _runCount; r++)
        {
            if (Run(r) is { Result: RunResult.Pass, Outcome.T: { } t } && (first is null || t < first))
            {
                first = t;
            }
        }

        return first;
    }

    private double? HeldV() => State == TaskState.Pending ? (AnyPassingTest(_latestStart) ? VTest : VBuild) : null;

    private TaskOpenFailure? OpenFailureFacts()
    {
        if (State != TaskState.Failing || OpenFailureRun() is not { } run)
        {
            return null;
        }

        var counted = run.Fingerprints is not null || run.FailedCount > 0;
        return new TaskOpenFailure(run.Suite, run.RunClass, run.Command!.Key, run.Errors, Peak(_seq[run.CheckNo]), counted, run.Block < Edits);
    }

    private TaskLastPass? LastPassFacts()
    {
        if (State is not (TaskState.Passing or TaskState.Pending))
        {
            return null;
        }

        var start = State == TaskState.Passing ? _currentStart : _latestStart;
        var run = Run(_runCount - 1);
        return new TaskLastPass(run.Suite, run.RunClass, run.Command!.Key, !AnyPassingTest(start));
    }
}
