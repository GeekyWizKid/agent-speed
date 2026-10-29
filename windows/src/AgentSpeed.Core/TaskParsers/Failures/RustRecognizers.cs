using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// cargo test (libtest) and nextest (TASK-PARSERS 5.3): failed test lines, captured-output headers, the
/// <c>failures:</c> listing and the per-binary results.
/// </summary>
internal sealed partial class CargoRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private bool listing;

    [GeneratedRegex("""\A(?:    ([^ ].*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LibtestListedShared();


    private static Regex LibtestListed() => ThreadRegex.Of(LibtestListedShared());

    [GeneratedRegex("""\A(?:test (.+) \.\.\. (FAILED|ok|ignored(?:, .*)?|bench: .*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LibtestLineShared();


    private static Regex LibtestLine() => ThreadRegex.Of(LibtestLineShared());

    [GeneratedRegex("""\A(?:---- (.+) (?:stdout|stderr) ----)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LibtestStdoutShared();


    private static Regex LibtestStdout() => ThreadRegex.Of(LibtestStdoutShared());

    [GeneratedRegex("""\A(?:test result: (?:ok|FAILED)\. ([0-9]+) passed; ([0-9]+) failed; ([0-9]+) ignored; ([0-9]+) measured; ([0-9]+) filtered out(?:; finished in .*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LibtestResultShared();


    private static Regex LibtestResult() => ThreadRegex.Of(LibtestResultShared());

    [GeneratedRegex("""\A(?:failures:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LibtestFailuresShared();


    private static Regex LibtestFailures() => ThreadRegex.Of(LibtestFailuresShared());

    [GeneratedRegex("""\A(?: *(?:FAIL|SIGSEGV|SIGABRT|SIGKILL|SIGBUS|TIMEOUT|LEAK-FAIL|ABORT) \[ *[0-9.]+m?s\] (?:\( *[0-9]+/[0-9]+\) )?([^ ]+) (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex NextestFailShared();


    private static Regex NextestFail() => ThreadRegex.Of(NextestFailShared());

    [GeneratedRegex("""\A(?: *(?:PASS|SKIP|LEAK|SLOW|FLAKY|START|RETRY|TRY [0-9]+ [A-Z-]+) \[.*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex NextestOtherShared();


    private static Regex NextestOther() => ThreadRegex.Of(NextestOtherShared());

    [GeneratedRegex("""\A(?: *Summary \[[^\]]*\] +([0-9]+) tests? run: (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex NextestSummaryShared();


    private static Regex NextestSummary() => ThreadRegex.Of(NextestSummaryShared());

    [GeneratedRegex("""\(line [0-9]+\)""", RegexOptions.CultureInvariant)]
    private static partial Regex DoctestLineShared();


    private static Regex DoctestLine() => ThreadRegex.Of(DoctestLineShared());

    [GeneratedRegex(""" \([^)]*\)""", RegexOptions.CultureInvariant)]
    private static partial Regex NextestNoteShared();


    private static Regex NextestNote() => ThreadRegex.Of(NextestNoteShared());

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        Match r;
        if (listing)
        {
            r = LibtestListed().Match(line);
            if (r.Success && IsListedName(G(r, 1)))
            {
                Found.Test("cargo", LibtestName(G(r, 1)));
                return 1;
            }

            listing = false;
        }

        r = LibtestLine().Match(line);
        if (r.Success)
        {
            if (G(r, 2) == "FAILED")
            {
                Found.Test("cargo", LibtestName(G(r, 1)));
            }

            return 1;
        }

        r = LibtestStdout().Match(line);
        if (r.Success)
        {
            Found.Test("cargo", LibtestName(G(r, 1)));
            return 1;
        }

        r = LibtestResult().Match(line);
        if (r.Success)
        {
            Found.Summary("cargo", FailureFamily.Test, N(r, 2), N(r, 1), FailureMode.Sum);
            return 1;
        }

        if (LibtestFailures().IsMatch(line) && i + 1 < lines.Length && LibtestListed().IsMatch(lines[i + 1]))
        {
            listing = true;
            return 1;
        }

        r = NextestFail().Match(line);
        if (r.Success)
        {
            Found.Test("cargo", LibtestName(G(r, 2)));
            return 1;
        }

        if (NextestOther().IsMatch(line))
        {
            return 1;
        }

        r = NextestSummary().Match(line);
        if (r.Success)
        {
            var c = FailureCounts.Parse(NextestNote().Replace(G(r, 2), ""));
            var failed = FailureCounts.Add(
                FailureCounts.Add(c.CountOf("failed"), c.CountOf("timed out")),
                FailureCounts.Add(c.CountOf("exec failed"), c.CountOf("leaky failed")));
            Found.Summary("nextest", FailureFamily.Test, failed, c.CountOf("passed"));
            return 1;
        }

        return 0;
    }

    /// <summary>A listed name is one word before any <c> - </c>; anything else ends the listing.</summary>
    private static bool IsListedName(string text)
    {
        var dash = text.IndexOf(" - ", StringComparison.Ordinal);
        return !(dash < 0 ? text : text[..dash]).Contains(' ', StringComparison.Ordinal);
    }

    private static string LibtestName(string name) => DoctestLine().Replace(name, "(line N)");
}

/// <summary>rustc through cargo build (TASK-PARSERS 5.3): an error header with its <c>--&gt;</c> location, and the error totals.</summary>
internal sealed partial class RustcRecognizer(FailureFound found) : FailureRecognizer(found)
{
    [GeneratedRegex("""\A(?:error: could not compile `[^`]+`(?: \([^)]*\))?(?:;.*)?(?: due to ([0-9]+) previous errors?| due to previous error)?(?:;.*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RustCouldNotShared();

    private static Regex RustCouldNot() => ThreadRegex.Of(RustCouldNotShared());

    [GeneratedRegex("""\A(?:error: aborting due to (?:([0-9]+) )?previous errors?(?:;.*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RustAbortingShared();


    private static Regex RustAborting() => ThreadRegex.Of(RustAbortingShared());

    [GeneratedRegex("""\A(?:error: (?:test failed|build failed|failed to run|[0-9]+ targets? failed|process didn't exit successfully|Recipe ).*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RustStatusShared();


    private static Regex RustStatus() => ThreadRegex.Of(RustStatusShared());

    [GeneratedRegex("""\A(?:error(?:\[(E[0-9]{4})\])?: (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RustHeaderShared();


    private static Regex RustHeader() => ThreadRegex.Of(RustHeaderShared());

    [GeneratedRegex("""\A(?: *--> (.+?):([0-9]+):([0-9]+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RustLocShared();


    private static Regex RustLoc() => ThreadRegex.Of(RustLocShared());

    [GeneratedRegex("""\A(?:warning(?:\[[A-Za-z0-9_:]+\])?: .+)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RustWarnShared();


    private static Regex RustWarn() => ThreadRegex.Of(RustWarnShared());

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        var r = RustCouldNot().Match(line);
        if (r.Success)
        {
            Found.Summary("rustc", FailureFamily.Error, N(r, 1, 1), null, FailureMode.Sum);
            return 1;
        }

        r = RustAborting().Match(line);
        if (r.Success)
        {
            Found.Summary("rustc", FailureFamily.Error, N(r, 1, 1), null, FailureMode.Sum);
            return 1;
        }

        if (RustStatus().IsMatch(line))
        {
            return 1;
        }

        r = RustHeader().Match(line);
        if (r.Success)
        {
            var k = Location(lines, i);
            if (k < 0)
            {
                return 0;
            }

            Found.Error("rustc", G(RustLoc().Match(lines[k]), 1), G(r, 1), G(r, 2));
            return k - i + 1;
        }

        if (RustWarn().IsMatch(line))
        {
            var k = Location(lines, i);
            return k < 0 ? 0 : k - i + 1;
        }

        return 0;
    }

    /// <summary>The index of the <c>--&gt;</c> line within the 3 lines after a header when no other header comes first, else -1.</summary>
    private static int Location(string[] lines, int i)
    {
        var end = Math.Min(i + 4, lines.Length);
        for (var k = i + 1; k < end; k++)
        {
            if (RustLoc().IsMatch(lines[k]))
            {
                return k;
            }

            if (RustHeader().IsMatch(lines[k]) || RustWarn().IsMatch(lines[k]))
            {
                return -1;
            }
        }

        return -1;
    }
}
