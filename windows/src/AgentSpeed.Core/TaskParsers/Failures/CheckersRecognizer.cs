using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// tsc, eslint, ruff, black, mypy and pyright (TASK-PARSERS 5.3): each checker's error lines and its error total.
/// </summary>
internal sealed partial class CheckersRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private string? eslintFile;
    private bool tscTable;

    [GeneratedRegex("""\A(?:[ \t]+[0-9]+[ \t]+.+:[0-9]+)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex TscRowShared();


    private static Regex TscRow() => ThreadRegex.Of(TscRowShared());

    [GeneratedRegex("""\A(?:(.+?)\(([0-9]+),([0-9]+)\): error (TS[0-9]+): (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex TscParenShared();


    private static Regex TscParen() => ThreadRegex.Of(TscParenShared());

    [GeneratedRegex("""\A(?:(.+?):([0-9]+):([0-9]+) - error (TS[0-9]+): (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex TscPrettyShared();


    private static Regex TscPretty() => ThreadRegex.Of(TscPrettyShared());

    [GeneratedRegex("""\A(?:Found ([0-9]+) errors? in [0-9]+ files? \((?:checked [0-9]+ source files?|errors prevented further checking)\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MypyFoundShared();


    private static Regex MypyFound() => ThreadRegex.Of(MypyFoundShared());

    [GeneratedRegex("""\A(?:Found ([0-9]+) errors? in .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex TscFoundShared();


    private static Regex TscFound() => ThreadRegex.Of(TscFoundShared());

    [GeneratedRegex("""\A(?:Found ([0-9]+) errors?(?: \(([0-9]+) fixed, ([0-9]+) remaining\))?\.)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex FoundErrorsShared();


    private static Regex FoundErrors() => ThreadRegex.Of(FoundErrorsShared());

    [GeneratedRegex("""\A(?:Errors +Files)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex TscTableShared();


    private static Regex TscTable() => ThreadRegex.Of(TscTableShared());

    [GeneratedRegex("""\A(?:(.+?):([0-9]+):([0-9]+): (.+) \[(Error|Warning)/(.+)\])\z""", RegexOptions.CultureInvariant)]
    private static partial Regex EslintUnixShared();


    private static Regex EslintUnix() => ThreadRegex.Of(EslintUnixShared());

    [GeneratedRegex("""\A(?:(/[^:]*|[A-Za-z]:[\\/][^:]*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex EslintFileShared();


    private static Regex EslintFile() => ThreadRegex.Of(EslintFileShared());

    [GeneratedRegex("""\A(?:[ \t]+([0-9]+):([0-9]+)[ \t]+(error|warning)[ \t]+(.+?)(?:[ \t]{2,}([^ \t]+))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex EslintMsgShared();


    private static Regex EslintMsg() => ThreadRegex.Of(EslintMsgShared());

    [GeneratedRegex("""\A(?:✖ ([0-9]+) problems? \(([0-9]+) errors?, ([0-9]+) warnings?\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex EslintSummaryShared();


    private static Regex EslintSummary() => ThreadRegex.Of(EslintSummaryShared());

    [GeneratedRegex("""\A(?:[ \t]+[0-9]+ errors? and [0-9]+ warnings? potentially fixable.*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex EslintFixableShared();


    private static Regex EslintFixable() => ThreadRegex.Of(EslintFixableShared());

    [GeneratedRegex("""\A(?:(.+?\.(?:py|pyi|ipynb)):([0-9]+):([0-9]+): ([A-Z]+[0-9]+|SyntaxError|invalid-syntax):? (?:\[\*\] )?(.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RuffConciseShared();


    private static Regex RuffConcise() => ThreadRegex.Of(RuffConciseShared());

    [GeneratedRegex("""\A(?:([A-Z]{1,4}[0-9]{1,4}|invalid-syntax|SyntaxError):? (?:\[\*\] )?(.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RuffHeaderShared();


    private static Regex RuffHeader() => ThreadRegex.Of(RuffHeaderShared());

    [GeneratedRegex("""\A(?: *--> (.+?):([0-9]+):([0-9]+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RuffLocShared();


    private static Regex RuffLoc() => ThreadRegex.Of(RuffLocShared());

    [GeneratedRegex("""\A(?:All checks passed!)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RuffPassedShared();


    private static Regex RuffPassed() => ThreadRegex.Of(RuffPassedShared());

    [GeneratedRegex("""\A(?:Would reformat: (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex RuffWouldShared();


    private static Regex RuffWould() => ThreadRegex.Of(RuffWouldShared());

    [GeneratedRegex("""\A(?:would reformat (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex BlackWouldShared();


    private static Regex BlackWould() => ThreadRegex.Of(BlackWouldShared());

    [GeneratedRegex("""\A(?:(?:Oh no! .*)?([0-9]+) files? would be reformatted(?:, [0-9]+ files? (?:already formatted|would be left unchanged))?\.?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex FormatSummaryShared();


    private static Regex FormatSummary() => ThreadRegex.Of(FormatSummaryShared());

    [GeneratedRegex("""\A(?:(.+?\.pyi?):([0-9]+)(?::([0-9]+))?: error: (.+?)(?:  \[([a-z0-9-]+)\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MypyErrorShared();


    private static Regex MypyError() => ThreadRegex.Of(MypyErrorShared());

    [GeneratedRegex("""\A(?:(.+?\.pyi?):([0-9]+)(?::([0-9]+))?: note: .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MypyNoteShared();


    private static Regex MypyNote() => ThreadRegex.Of(MypyNoteShared());

    [GeneratedRegex("""\A(?:Success: no issues found in [0-9]+ source files?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MypySuccessShared();


    private static Regex MypySuccess() => ThreadRegex.Of(MypySuccessShared());

    [GeneratedRegex("""\A(?:[ \t]*(.+?):([0-9]+):([0-9]+) - error: (.+?)(?: \((report[A-Za-z]+)\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PyrightErrorShared();


    private static Regex PyrightError() => ThreadRegex.Of(PyrightErrorShared());

    [GeneratedRegex("""\A(?:[ \t]*(.+?):([0-9]+):([0-9]+) - (?:warning|information|info|note): .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PyrightOtherShared();


    private static Regex PyrightOther() => ThreadRegex.Of(PyrightOtherShared());

    [GeneratedRegex(""" \((report[A-Za-z]+)\)$""", RegexOptions.CultureInvariant)]
    private static partial Regex PyrightRuleShared();


    private static Regex PyrightRule() => ThreadRegex.Of(PyrightRuleShared());

    [GeneratedRegex("""\A(?:([0-9]+) errors?, ([0-9]+) warnings?, ([0-9]+) (?:informations?|infos?|notes?) ?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PyrightSummaryShared();


    private static Regex PyrightSummary() => ThreadRegex.Of(PyrightSummaryShared());

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        if (tscTable)
        {
            if (TscRow().IsMatch(line))
            {
                return 1;
            }

            tscTable = false;
        }

        var r = TscParen().Match(line);
        if (!r.Success)
        {
            r = TscPretty().Match(line);
        }

        if (r.Success)
        {
            Found.Error("tsc", G(r, 1), G(r, 4), G(r, 5));
            return 1;
        }

        r = MypyFound().Match(line);
        if (r.Success)
        {
            Found.Summary("mypy", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        r = TscFound().Match(line);
        if (r.Success)
        {
            Found.Summary("tsc", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        r = FoundErrors().Match(line);
        if (r.Success)
        {
            Found.Summary("found-errors", FailureFamily.Error, r.Groups[3].Success ? N(r, 3) : N(r, 1), null);
            return 1;
        }

        if (TscTable().IsMatch(line))
        {
            tscTable = true;
            return 1;
        }

        r = EslintUnix().Match(line);
        if (r.Success)
        {
            if (G(r, 5) == "Error")
            {
                Found.Error("eslint", G(r, 1), G(r, 6), G(r, 4));
            }

            return 1;
        }

        if (EslintFile().IsMatch(line) && i + 1 < lines.Length && EslintMsg().IsMatch(lines[i + 1]))
        {
            eslintFile = line;
            return 1;
        }

        if (eslintFile is not null)
        {
            r = EslintMsg().Match(line);
            if (r.Success)
            {
                if (G(r, 3) == "error")
                {
                    Found.Error("eslint", eslintFile, G(r, 5), G(r, 4));
                }

                return 1;
            }
        }

        r = EslintSummary().Match(line);
        if (r.Success)
        {
            Found.Summary("eslint", FailureFamily.Error, N(r, 2), null);
            return 1;
        }

        if (EslintFixable().IsMatch(line))
        {
            return 1;
        }

        r = RuffConcise().Match(line);
        if (r.Success)
        {
            Found.Error("ruff", G(r, 1), G(r, 4), G(r, 5));
            return 1;
        }

        r = RuffHeader().Match(line);
        if (r.Success)
        {
            var end = Math.Min(i + 4, lines.Length);
            for (var k = i + 1; k < end; k++)
            {
                var loc = RuffLoc().Match(lines[k]);
                if (loc.Success)
                {
                    Found.Error("ruff", G(loc, 1), G(r, 1), G(r, 2));
                    return k - i + 1;
                }
            }
        }

        if (RuffPassed().IsMatch(line))
        {
            Found.Summary("ruff", FailureFamily.Error, 0, null);
            return 1;
        }

        r = RuffWould().Match(line);
        if (r.Success)
        {
            Found.Error("ruff", G(r, 1), "format", "");
            return 1;
        }

        r = BlackWould().Match(line);
        if (r.Success)
        {
            Found.Error("black", G(r, 1), "format", "");
            return 1;
        }

        r = FormatSummary().Match(line);
        if (r.Success)
        {
            Found.Summary("format", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        r = MypyError().Match(line);
        if (r.Success)
        {
            Found.Error("mypy", G(r, 1), G(r, 5), G(r, 4));
            return 1;
        }

        if (MypyNote().IsMatch(line))
        {
            return 1;
        }

        if (MypySuccess().IsMatch(line))
        {
            Found.Summary("mypy", FailureFamily.Error, 0, null);
            return 1;
        }

        r = Has(line, " - error: ") ? PyrightError().Match(line) : Match.Empty;
        if (r.Success)
        {
            return Pyright(lines, i, r);
        }

        if (IsPyrightOther(line))
        {
            return 1;
        }

        r = PyrightSummary().Match(line);
        if (r.Success)
        {
            Found.Summary("pyright", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        return 0;
    }

    /// <summary>A pyright error with its more-indented continuation lines; the rule may end the last of them.</summary>
    private int Pyright(string[] lines, int i, Match r)
    {
        var indent = LeadingWs(lines[i]);
        var k = i;
        var code = G(r, 5);
        while (k + 1 < lines.Length
               && LeadingWs(lines[k + 1]) > indent
               && !(Has(lines[k + 1], " - error: ") && PyrightError().IsMatch(lines[k + 1]))
               && !IsPyrightOther(lines[k + 1]))
        {
            k++;
        }

        if (k > i && code.Length == 0)
        {
            code = G(PyrightRule().Match(lines[k]), 1);
        }

        Found.Error("pyright", G(r, 1), code, PyrightRule().Replace(G(r, 4), ""));
        return k - i + 1;
    }

    private static bool IsPyrightOther(string line) =>
        (Has(line, " - warning: ") || Has(line, " - info") || Has(line, " - note: ")) && PyrightOther().IsMatch(line);

    private static int LeadingWs(string line)
    {
        var n = 0;
        while (n < line.Length && TaskText.IsWs(line[n]))
        {
            n++;
        }

        return n;
    }
}
