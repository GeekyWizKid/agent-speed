using System.Buffers;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>rspec, phpunit and ctest (TASK-PARSERS 5.3): failed examples, tests and listings, and their totals.</summary>
internal sealed partial class MiscRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private string? phpSection;
    private bool? ctestFailedList;

    [GeneratedRegex("""\A(?:[ \t]+[0-9]+ - (.+?) \((.+)\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestListed();

    [GeneratedRegex("""\A(?:rspec (?:\./)?(.+?)(?::[0-9]+|\[[0-9:]+\]) # (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RspecFailed();

    [GeneratedRegex("""\A(?:([0-9]+) examples?, ([0-9]+) failures?(?:, ([0-9]+) pending)?(?:, ([0-9]+) errors? occurred outside of examples)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RspecSummary();

    [GeneratedRegex("""\A(?:An error occurred while loading (?:\./)?(.+?)\.?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RspecLoad();

    [GeneratedRegex("""\A(?:There (?:was|were) [0-9]+ ([a-z][a-z ]*?):)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitSection();

    [GeneratedRegex("""\A(?:[0-9]+\) ([A-Za-z0-9_\\]+)::([A-Za-z0-9_]+)(?: with data set (#[0-9]+|\"[^\"]*\"))?(?: \(.*\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitItem();

    [GeneratedRegex("""\A(?:Tests: ([0-9]+), Assertions: [0-9]+((?:, [A-Za-z ]+: [0-9]+)*)\.)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitTests();

    [GeneratedRegex("""\A(?:OK \(([0-9]+) tests?, [0-9]+ assertions?\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitOk();

    [GeneratedRegex("""\A(?:FAILURES!|ERRORS!|OK, but (?:there were issues|incomplete, skipped, or risky tests)!)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitNoise();

    [GeneratedRegex("""\A(?: *[0-9]+/[0-9]+ Test +#[0-9]+: (.+?) \.*(?: +|\*\*\*)(Passed|Failed|Exception|Timeout|Not Run|Skipped)(?:[: ].*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestLine();

    [GeneratedRegex("""\A(?:The following tests (?:FAILED|did not run):)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestListHeader();

    [GeneratedRegex("""\A(?:([0-9]+)% tests passed, ([0-9]+) tests? failed out of ([0-9]+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestSummary();

    [GeneratedRegex("""\A(?: *Start +[0-9]+: .*|Test project .*|Errors while running CTest|Total Test time .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CtestNoise();

    [GeneratedRegex("""\A(?:([A-Za-z ]+): ([0-9]+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PhpunitPair();

    // Every pattern below needs one of these (the patterns are anchored and case-sensitive).
    private static readonly SearchValues<string> Gate = Literals("rspec ", " example", "An error occurred while loading ", "There ", "Tests: ", "OK (", "FAILURES!", "ERRORS!", "OK, but ", " Test ", "The following tests ", "% tests passed, ", "Start ", "Test project ", "Errors while running CTest", "Total Test time ");

    // Besides: phpunit items, "N) Class::method".
    public override bool CannotClaim(string line) =>
        ctestFailedList is null && Lacks(line, Gate) && !(line.Length > 0 && char.IsAsciiDigit(line[0]) && line.Contains("::", StringComparison.Ordinal));

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
