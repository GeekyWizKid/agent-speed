namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// <c>failures(info, outputText, cwd, failed)</c> (TASK-PARSERS 5): the fingerprints of the failing tests and errors in
/// a command's output, its failed and passed counts and the framework that decided them. Only the suite and the error
/// filter flag of the command's <c>command_info</c> are read; the output is analysed in memory and never kept.
/// </summary>
internal static class FailureParser
{
    public const int MaxFingerprints = 256;

    private static readonly (string By, string Dropped)[] Supersedes = [("nextest", "cargo")];

    /// <summary>failures(info, outputText, cwd, failed); <paramref name="info"/> is null for a call without command text.</summary>
    public static FailureReport Failures(CommandInfo? info, string outputText, string? cwd, bool? failed) =>
        Detail(info, outputText, cwd, failed).Report;

    /// <summary>The same plus the canonical strings (sorted with <see cref="TaskText.ScalarOrder"/>), for tests only.</summary>
    internal static (FailureReport Report, IReadOnlyList<string> Canonical) Detail(CommandInfo? info, string outputText, string? cwd, bool? failed)
    {
        var suite = info?.Suite;
        var found = new FailureFound(cwd);
        var segments = new List<(string[] Lines, bool[] Claimed)>(2);
        foreach (var segment in FailureBudget.Segments(TaskText.Scalars(outputText ?? "")))
        {
            var lines = FailureLines.Clean(segment);
            segments.Add((lines, Recognize(found, lines)));
        }

        if (found.Canonical.Count == 0 && failed == true)
        {
            foreach (var (lines, claimed) in segments)
            {
                FailureFallback.Generic(found, lines, claimed);
            }
        }

        var canonical = found.Canonical.Distinct(StringComparer.Ordinal).ToList();
        canonical.Sort(TaskText.ScalarOrder);
        var fingerprints = canonical.Select(TaskText.Sha16).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxFingerprints).ToArray();

        var (order, totals) = Combine(found, suite ?? "");
        var tests = order.Where(fw => totals[fw].Family == FailureFamily.Test).ToList();
        var chosen = tests.Count > 0 ? tests : order.Where(fw => totals[fw].Family == FailureFamily.Error).ToList();
        long? failedCount;
        long? passedCount = null;
        string? framework;
        if (chosen.Count > 0)
        {
            failedCount = 0;
            long? passes = null;
            foreach (var fw in chosen)
            {
                failedCount = FailureCounts.Add(failedCount.Value, totals[fw].Failed);
                if (totals[fw].Passed is { } p)
                {
                    passes = FailureCounts.Add(passes ?? 0, p);
                }
            }

            passedCount = tests.Count > 0 ? passes : null;
            framework = chosen.FirstOrDefault(fw => totals[fw].Failed != 0)
                ?? chosen.FirstOrDefault(fw => totals[fw].Passed is not (null or 0))
                ?? chosen[0];
        }
        else if (canonical.Count > 0)
        {
            failedCount = canonical.Count;
            var first = found.Canonical[0];
            framework = first[..first.IndexOf('|', StringComparison.Ordinal)];
        }
        else
        {
            framework = FailureFallback.Completion(segments.Select(s => s.Lines));
            failedCount = framework is null ? null : 0;

            // A silent error filter found no error line (3.10, 5.4).
            if (failedCount is null && failed == false && info is { ErrorFilter: true } && MaskedRuns.Silent(outputText ?? ""))
            {
                failedCount = 0;
            }
        }

        return (new FailureReport(fingerprints, failedCount, passedCount, framework), canonical);
    }

    /// <summary>Reads one segment with a fresh set of recognisers; returns which lines were claimed.</summary>
    private static bool[] Recognize(FailureFound found, string[] lines)
    {
        var claimed = new bool[lines.Length];
        var recognizers = FailureRecognizer.CreateAll(found);
        var i = 0;
        while (i < lines.Length)
        {
            var used = 0;
            foreach (var recognizer in recognizers)
            {
                used = recognizer.Step(lines, i);
                if (used > 0)
                {
                    break;
                }
            }

            if (used > 0)
            {
                var end = Math.Min(i + used, lines.Length);
                claimed.AsSpan(i, end - i).Fill(true);
                i = end;
            }
            else
            {
                i++;
            }
        }

        foreach (var recognizer in recognizers)
        {
            recognizer.Finish();
        }

        return claimed;
    }

    /// <summary>
    /// The summaries per framework (TASK-PARSERS 5.4) in order of first appearance: <c>found-errors</c> assigned to tsc
    /// or ruff, superseded frameworks dropped, then each combined by mode.
    /// </summary>
    private static (List<string> Order, Dictionary<string, FailureTotal> Totals) Combine(FailureFound found, string suite)
    {
        var hasTsc = found.Canonical.Exists(fp => fp.StartsWith("tsc|", StringComparison.Ordinal));
        var summaries = new List<FailureSummary>(found.Summaries.Count);
        foreach (var s in found.Summaries)
        {
            summaries.Add(s.Framework == "found-errors" ? s with { Framework = hasTsc || suite is "tsc" or "vue-tsc" ? "tsc" : "ruff" } : s);
        }

        foreach (var (by, dropped) in Supersedes)
        {
            if (summaries.Exists(s => s.Framework == by))
            {
                summaries.RemoveAll(s => s.Framework == dropped);
            }
        }

        var order = new List<string>();
        var totals = new Dictionary<string, FailureTotal>(StringComparer.Ordinal);
        foreach (var s in summaries)
        {
            if (!totals.TryGetValue(s.Framework, out var total))
            {
                order.Add(s.Framework);
                totals[s.Framework] = new FailureTotal(s.Family, s.Failed, s.Passed);
            }
            else if (s.Mode == FailureMode.Sum)
            {
                total.Failed = FailureCounts.Add(total.Failed, s.Failed);
                total.Passed = total.Passed is null && s.Passed is null ? null : FailureCounts.Add(total.Passed ?? 0, s.Passed ?? 0);
            }
            else
            {
                totals[s.Framework] = new FailureTotal(s.Family, s.Failed, s.Passed);
            }
        }

        return (order, totals);
    }

    private sealed class FailureTotal(FailureFamily family, long failed, long? passed)
    {
        public FailureFamily Family { get; } = family;

        public long Failed { get; set; } = failed;

        public long? Passed { get; set; } = passed;
    }
}
