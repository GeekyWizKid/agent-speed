using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// swift-testing, XCTest, xcodebuild and swiftc (TASK-PARSERS 5.3): failed tests and their run totals, the
/// <c>Failing tests:</c> listing, the failed build command count and compiler errors.
/// </summary>
internal sealed partial class SwiftRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private readonly HashSet<string> stFailed = new(StringComparer.Ordinal);
    private readonly HashSet<string> xcFailed = new(StringComparer.Ordinal);
    private bool listing;
    private bool commands;

    [GeneratedRegex("""\A(?:[ \t]+(.+?)(?: \(.*\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XbListedShared();


    private static Regex XbListed() => ThreadRegex.Of(XbListedShared());

    [GeneratedRegex("""\A(?:\(([0-9]+) failures?\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XbCountShared();


    private static Regex XbCount() => ThreadRegex.Of(XbCountShared());

    [GeneratedRegex("""\A(?:[^ \tA-Za-z0-9]+ Test (.+?) failed after [0-9]+(?:\.[0-9]+)? seconds(?: with [0-9]+ issues?)?(?: \(including [0-9]+ known issues?\))?\.)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex StFailedShared();


    private static Regex StFailed() => ThreadRegex.Of(StFailedShared());

    [GeneratedRegex(""" with [0-9]+ test cases?$""", RegexOptions.CultureInvariant)]
    private static partial Regex StCasesShared();


    private static Regex StCases() => ThreadRegex.Of(StCasesShared());

    [GeneratedRegex("""\A(?:[^ \tA-Za-z0-9]+ Test run with ([0-9]+) tests?(?: in [0-9]+ suites?)? (passed|failed) after [0-9]+(?:\.[0-9]+)? seconds(?: with ([0-9]+) issues?)?(?: \(including [0-9]+ known issues?\))?\.)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex StRunShared();


    private static Regex StRun() => ThreadRegex.Of(StRunShared());

    [GeneratedRegex("""\A(?:[^ \tA-Za-z0-9]+ Test (.+?) recorded an issue.*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex StIssueShared();


    private static Regex StIssue() => ThreadRegex.Of(StIssueShared());

    [GeneratedRegex("""\A(?:[^ \tA-Za-z0-9]+ (?:Test .+? (?:passed after .*|started\.|skipped.*)|Suite .+? (?:passed|failed|started).*|Test run started\.|Testing Library Version: .*|Target Platform: .*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex StOtherShared();


    private static Regex StOther() => ThreadRegex.Of(StOtherShared());

    [GeneratedRegex("""\A(?:Test [Cc]ase '(-\[[^\]]+\]|[^']+)' (failed|passed|started|skipped)(?: on '[^']*')?(?: \([0-9.]+ seconds\))?\.?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XcCaseShared();


    private static Regex XcCase() => ThreadRegex.Of(XcCaseShared());

    [GeneratedRegex("""\A(?:(.+?):([0-9]+): error: (-\[[^\]]+\]|[A-Za-z0-9_.()/]+) : .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XcErrorShared();


    private static Regex XcError() => ThreadRegex.Of(XcErrorShared());

    [GeneratedRegex("""\A(?:Test [Ss]uite '.+' (?:started|passed|failed).*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XcSuiteShared();


    private static Regex XcSuite() => ThreadRegex.Of(XcSuiteShared());

    [GeneratedRegex("""\A(?:[ \t]*Executed ([0-9]+) tests?, with (?:([0-9]+) tests? skipped and )?([0-9]+) failures? \(([0-9]+) unexpected\) in [0-9.]+ \([0-9.]+\) seconds)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XcExecutedShared();


    private static Regex XcExecuted() => ThreadRegex.Of(XcExecutedShared());

    [GeneratedRegex("""\A(?:Failing tests:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XbFailingShared();


    private static Regex XbFailing() => ThreadRegex.Of(XbFailingShared());

    [GeneratedRegex("""\A(?:\*\* (?:TEST|BUILD|ANALYZE|ARCHIVE|CLEAN|TEST BUILD|INSTALL) (?:FAILED|SUCCEEDED|INTERRUPTED) \*\*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XbResultShared();


    private static Regex XbResult() => ThreadRegex.Of(XbResultShared());

    [GeneratedRegex("""\A(?:The following build commands failed:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex XbCommandsShared();


    private static Regex XbCommands() => ThreadRegex.Of(XbCommandsShared());

    [GeneratedRegex("""\A(?:(.+\.swift):([0-9]+):([0-9]+): error: (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SwiftcErrorShared();


    private static Regex SwiftcError() => ThreadRegex.Of(SwiftcErrorShared());

    [GeneratedRegex("""\A(?:(.+\.swift):([0-9]+):([0-9]+): (?:warning|note): .+)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SwiftcWarnShared();


    private static Regex SwiftcWarn() => ThreadRegex.Of(SwiftcWarnShared());

    [GeneratedRegex("""\A(?:error: (?:emit-module command failed|fatalError|compile command failed|link command failed).*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SwiftNoiseShared();


    private static Regex SwiftNoise() => ThreadRegex.Of(SwiftNoiseShared());

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        Match r;
        if (listing)
        {
            r = XbListed().Match(line);
            if (r.Success)
            {
                var listed = G(r, 1);
                if (!listed.EndsWith(':'))
                {
                    XcTest(XctestId(listed));
                }

                return 1;
            }

            listing = false;
        }

        if (commands)
        {
            r = XbCount().Match(line);
            if (r.Success)
            {
                Found.Summary("xcodebuild", FailureFamily.Error, N(r, 1), null);
                commands = false;
                return 1;
            }

            if (StartsIndented(line))
            {
                return 1;
            }

            commands = false;
        }

        r = StFailed().Match(line);
        if (r.Success && !G(r, 1).StartsWith("run with ", StringComparison.Ordinal))
        {
            var name = StCases().Replace(G(r, 1), "");
            stFailed.Add(name);
            Found.Test("swift-testing", name);
            return 1;
        }

        r = StRun().Match(line);
        if (r.Success)
        {
            var total = N(r, 1);
            long failed = G(r, 2) == "passed" ? 0 : stFailed.Count > 0 ? stFailed.Count : N(r, 3, 1);
            Found.Summary("swift-testing", FailureFamily.Test, failed, Math.Max(0, total - failed));
            return 1;
        }

        if (StIssue().IsMatch(line) || StOther().IsMatch(line))
        {
            return 1;
        }

        r = XcCase().Match(line);
        if (r.Success)
        {
            if (G(r, 2) == "failed")
            {
                XcTest(XctestId(G(r, 1)));
            }

            return 1;
        }

        r = Has(line, ": error: ") ? XcError().Match(line) : Match.Empty;
        if (r.Success)
        {
            XcTest(XctestId(G(r, 3)));
            return 1;
        }

        if (XcSuite().IsMatch(line))
        {
            return 1;
        }

        r = XcExecuted().Match(line);
        if (r.Success)
        {
            var total = N(r, 1);
            var skipped = N(r, 2);
            long failed = xcFailed.Count > 0 ? xcFailed.Count : N(r, 3);
            Found.Summary("xctest", FailureFamily.Test, failed, Math.Max(0, total - failed - skipped));
            return 1;
        }

        if (XbFailing().IsMatch(line))
        {
            listing = true;
            return 1;
        }

        if (XbResult().IsMatch(line))
        {
            return 1;
        }

        if (XbCommands().IsMatch(line))
        {
            commands = true;
            return 1;
        }

        r = SwiftcError().Match(line);
        if (r.Success)
        {
            Found.Error("swiftc", G(r, 1), "", G(r, 4));
            return 1;
        }

        return SwiftcWarn().IsMatch(line) || SwiftNoise().IsMatch(line) ? 1 : 0;
    }

    private void XcTest(string test)
    {
        xcFailed.Add(test);
        Found.Test("xctest", test);
    }

    /// <summary><c>xctest_id</c>: <c>-[Class method]</c>, <c>Module.Class/method()</c> and the like as <c>Class.method</c>.</summary>
    private static string XctestId(string name)
    {
        var s = name;
        if (s.StartsWith("-[", StringComparison.Ordinal) && s.EndsWith(']'))
        {
            s = s[2..^1];
        }

        s = s.Replace(' ', '.').Replace('/', '.').Replace("()", "", StringComparison.Ordinal);
        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.', parts, Math.Max(0, parts.Length - 2), Math.Min(2, parts.Length));
    }
}
