using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>rspec, phpunit and ctest (TASK-PARSERS 5.3): failed examples, tests and listings, and their totals.</summary>
internal sealed partial class MiscRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private string? phpSection;
    private bool? ctestFailedList;

    [GeneratedRegex("""\A(?:[ \t]+[0-9]+ - (.+?) \((.+)\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestListedShared();


    private static Regex CtestListed() => ThreadRegex.Of(CtestListedShared());

    [GeneratedRegex("""\A(?:rspec (?:\./)?(.+?)(?::[0-9]+|\[[0-9:]+\]) # (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RspecFailedShared();


    private static Regex RspecFailed() => ThreadRegex.Of(RspecFailedShared());

    [GeneratedRegex("""\A(?:([0-9]+) examples?, ([0-9]+) failures?(?:, ([0-9]+) pending)?(?:, ([0-9]+) errors? occurred outside of examples)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RspecSummaryShared();


    private static Regex RspecSummary() => ThreadRegex.Of(RspecSummaryShared());

    [GeneratedRegex("""\A(?:An error occurred while loading (?:\./)?(.+?)\.?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RspecLoadShared();


    private static Regex RspecLoad() => ThreadRegex.Of(RspecLoadShared());

    [GeneratedRegex("""\A(?:There (?:was|were) [0-9]+ ([a-z][a-z ]*?):)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitSectionShared();


    private static Regex PhpunitSection() => ThreadRegex.Of(PhpunitSectionShared());

    [GeneratedRegex("""\A(?:[0-9]+\) ([A-Za-z0-9_\\]+)::([A-Za-z0-9_]+)(?: with data set (#[0-9]+|\"[^\"]*\"))?(?: \(.*\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitItemShared();


    private static Regex PhpunitItem() => ThreadRegex.Of(PhpunitItemShared());

    [GeneratedRegex("""\A(?:Tests: ([0-9]+), Assertions: [0-9]+((?:, [A-Za-z ]+: [0-9]+)*)\.)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitTestsShared();


    private static Regex PhpunitTests() => ThreadRegex.Of(PhpunitTestsShared());

    [GeneratedRegex("""\A(?:OK \(([0-9]+) tests?, [0-9]+ assertions?\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitOkShared();


    private static Regex PhpunitOk() => ThreadRegex.Of(PhpunitOkShared());

    [GeneratedRegex("""\A(?:FAILURES!|ERRORS!|OK, but (?:there were issues|incomplete, skipped, or risky tests)!)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitNoiseShared();


    private static Regex PhpunitNoise() => ThreadRegex.Of(PhpunitNoiseShared());

    [GeneratedRegex("""\A(?: *[0-9]+/[0-9]+ Test +#[0-9]+: (.+?) \.*(?: +|\*\*\*)(Passed|Failed|Exception|Timeout|Not Run|Skipped)(?:[: ].*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestLineShared();


    private static Regex CtestLine() => ThreadRegex.Of(CtestLineShared());

    [GeneratedRegex("""\A(?:The following tests (?:FAILED|did not run):)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestListHeaderShared();


    private static Regex CtestListHeader() => ThreadRegex.Of(CtestListHeaderShared());

    [GeneratedRegex("""\A(?:([0-9]+)% tests passed, ([0-9]+) tests? failed out of ([0-9]+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestSummaryShared();


    private static Regex CtestSummary() => ThreadRegex.Of(CtestSummaryShared());

    [GeneratedRegex("""\A(?: *Start +[0-9]+: .*|Test project .*|Errors while running CTest|Total Test time .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestNoiseShared();


    private static Regex CtestNoise() => ThreadRegex.Of(CtestNoiseShared());

    [GeneratedRegex("""\A(?:([A-Za-z ]+): ([0-9]+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitPairShared();


    private static Regex PhpunitPair() => ThreadRegex.Of(PhpunitPairShared());

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        Match r;
        if (ctestFailedList is { } failedList)
        {
            r = CtestListed().Match(line);
            if (r.Success)
            {
                if (failedList)
                {
                    Found.Test("ctest", G(r, 1));
                }

                return 1;
            }

            ctestFailedList = null;
        }

        r = RspecFailed().Match(line);
        if (r.Success)
        {
            Found.Test("rspec", G(r, 2), G(r, 1));
            return 1;
        }

        r = RspecSummary().Match(line);
        if (r.Success)
        {
            var examples = N(r, 1);
            var failures = N(r, 2);
            Found.Summary("rspec", FailureFamily.Test, failures + N(r, 4), Math.Max(0, examples - failures - N(r, 3)));
            return 1;
        }

        r = RspecLoad().Match(line);
        if (r.Success)
        {
            Found.Error("rspec", G(r, 1), "load", "");
            return 1;
        }

        r = PhpunitSection().Match(line);
        if (r.Success)
        {
            phpSection = G(r, 1);
            return 1;
        }

        r = PhpunitItem().Match(line);
        if (r.Success)
        {
            if (phpSection is "failure" or "failures" or "error" or "errors")
            {
                var data = G(r, 3);
                Found.Test("phpunit", G(r, 1) + "::" + G(r, 2) + (data.Length > 0 ? " " + data : ""));
            }

            return 1;
        }

        r = PhpunitTests().Match(line);
        if (r.Success)
        {
            var c = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var part in G(r, 2).Split(", "))
            {
                var pair = PhpunitPair().Match(part);
                if (pair.Success)
                {
                    c[G(pair, 1)] = N(pair, 2);
                }
            }

            var failed = c.CountOf("Errors") + c.CountOf("Failures");
            Found.Summary("phpunit", FailureFamily.Test, failed, Math.Max(0, N(r, 1) - failed - c.CountOf("Skipped") - c.CountOf("Incomplete")));
            return 1;
        }

        r = PhpunitOk().Match(line);
        if (r.Success)
        {
            Found.Summary("phpunit", FailureFamily.Test, 0, N(r, 1));
            return 1;
        }

        if (PhpunitNoise().IsMatch(line))
        {
            return 1;
        }

        r = CtestLine().Match(line);
        if (r.Success)
        {
            if (G(r, 2) is "Failed" or "Exception" or "Timeout")
            {
                Found.Test("ctest", G(r, 1));
            }

            return 1;
        }

        if (CtestListHeader().IsMatch(line))
        {
            ctestFailedList = line.Contains("FAILED", StringComparison.Ordinal);
            return 1;
        }

        r = CtestSummary().Match(line);
        if (r.Success)
        {
            var failed = N(r, 2);
            Found.Summary("ctest", FailureFamily.Test, failed, Math.Max(0, N(r, 3) - failed));
            return 1;
        }

        return CtestNoise().IsMatch(line) ? 1 : 0;
    }
}
