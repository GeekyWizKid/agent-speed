using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>The five dimensions (METRICS 6.9 <i>Dimensions</i>, the reference's <c>_dimensions</c>).</summary>
public sealed partial class TaskEvaluation
{
    internal const double VTest = 40.0;
    internal const double VBuild = 20.0;
    internal const double CMax = 25.0;
    private const double RMax = 15.0;
    private const double EMax = 10.0;
    private const double KBuild = 4.0;
    private const double KTest = 4.0;
    private const double KFull = 2.0;
    private const int Ramp = 8;

    private TaskDims Dimensions()
    {
        var start = _currentStart;
        var hasPassingTest = AnyPassingTest(start);
        var v = State == TaskState.Passing ? (hasPassingTest ? VTest : VBuild) : 0.0;
        var c = Convergence();

        double r;
        if (_episodes.Count > 0)
        {
            var sum = 0.0;
            foreach (var episode in _episodes)
            {
                sum += episode.Recovered ? 2.0 / (episode.Fails.Count + 1) : 0.0;
            }

            r = RMax * sum / _episodes.Count;
        }
        else
        {
            r = AnyPass() ? RMax : 0.0;
        }

        var n = _countedCount;
        var e = n > 0 ? EMax * Math.Min(1.0, (double)n / Ramp) * Ig * (1 - Loop) * (1 - Osc) : 0.0;

        var anyBuildOrCheck = false;
        var anyTest = false;
        var lastFailingTest = -1;
        for (var i = start; i < _runCount; i++)
        {
            var run = Run(i);
            anyBuildOrCheck |= run.RunClass is CommandClass.Build or CommandClass.Check;
            if (run.RunClass == CommandClass.Test)
            {
                anyTest = true;
                if (run.Result == RunResult.Fail)
                {
                    lastFailingTest = run.Index;
                }
            }
        }

        var k = 0.0;
        if (anyBuildOrCheck || hasPassingTest)
        {
            k += KBuild;
        }

        if (anyTest)
        {
            k += KTest;
        }

        for (var i = start; i < _runCount; i++)
        {
            var run = Run(i);
            if (IsPassingTest(run) && !run.Targeted && run.Index > lastFailingTest)
            {
                k += KFull;
                break;
            }
        }

        return new TaskDims(v, c, r, e, k);
    }

    private bool AnyPass()
    {
        for (var i = 0; i < _runCount; i++)
        {
            if (Run(i).Result == RunResult.Pass)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// C: over every check whose peak <c>F_peak</c> is above 0, <c>c_j = max(0, 1 − (F_last + N_new) / F_peak)</c>,
    /// weighed by the peak; 25 when runs exist and no check ever failed, 0 without runs.
    /// </summary>
    private double Convergence()
    {
        if (_runCount == 0)
        {
            return 0.0;
        }

        var weighted = 0.0;
        var total = 0.0;
        foreach (var seq in _seq)
        {
            var peak = Peak(seq);
            if (peak == 0)
            {
                continue;
            }

            var last = seq[^1];
            var fresh = 0;
            if (last.Result == RunResult.Fail && last.Fingerprints is { } latest)
            {
                var firstPeak = seq.FindIndex(run => run.Errors == peak);
                var known = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i <= firstPeak; i++)
                {
                    if (seq[i].Fingerprints is { } fingerprints)
                    {
                        known.UnionWith(fingerprints);
                    }
                }

                foreach (var fingerprint in latest)
                {
                    if (!known.Contains(fingerprint))
                    {
                        fresh++;
                    }
                }
            }

            weighted += peak * Math.Max(0.0, 1 - Ratio((Int128)last.Errors + fresh, peak));
            total += peak;
        }

        return total == 0 ? CMax : CMax * weighted / total;
    }

    private static long Peak(List<TaskAct> seq)
    {
        var peak = 0L;
        foreach (var run in seq)
        {
            peak = Math.Max(peak, run.Errors);
        }

        return peak;
    }

    /// <summary>An integer quotient as a double (the reference's integers do not overflow).</summary>
    internal static double Ratio(Int128 numerator, long denominator) => (double)numerator / denominator;
}
