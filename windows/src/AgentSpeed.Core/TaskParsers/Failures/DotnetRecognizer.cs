using System.Buffers;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// .NET (TASK-PARSERS 5.3): MSBuild errors and totals, VSTest and Microsoft Testing Platform failed tests and summaries.
/// </summary>
internal sealed partial class DotnetRecognizer(FailureFound found) : FailureRecognizer(found)
{
    [GeneratedRegex("""\A(?:[ \t]*(.+?)\(([0-9]+)(?:,([0-9]+))?(?:,[0-9]+,[0-9]+)?\): error ([A-Za-z]+[0-9]+): (.*?)(?: \[([^\]]+)\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MsbError();

    [GeneratedRegex("""\A(?:[ \t]*([A-Za-z][A-Za-z0-9_.-]*) : error ([A-Za-z]+[0-9]+): (.*?)(?: \[([^\]]+)\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MsbToolError();

    [GeneratedRegex("""\A(?:[ \t]*.+?(?:\([0-9,]+\))? : warning [A-Za-z]+[0-9]+: .*|[ \t]*.+?\([0-9,]+\): warning [A-Za-z]+[0-9]+: .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MsbWarning();

    [GeneratedRegex("""\A(?:[ \t]*([0-9]+) Error\(s\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MsbCount();

    [GeneratedRegex("""\A(?:Build (?:failed|succeeded)(?: with ([0-9]+) error\(s\)(?: and [0-9]+ warning\(s\))?)?(?: and [0-9]+ warning\(s\))? in [0-9.]+s)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MsbTerminal();

    [GeneratedRegex("""\A(?:[ \t]*[0-9]+ Warning\(s\)|Build FAILED\.|Build succeeded\.|Time Elapsed .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MsbNoise();

    [GeneratedRegex("""\A(?:[ \t]*Failed ([^ ]+) \[[^\]]*\])\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VstestFailed();

    [GeneratedRegex("""\A(?:[ \t]*(?:Passed|Skipped) ([^ ]+) \[[^\]]*\])\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VstestPassed();

    [GeneratedRegex("""\A(?:(?:Failed|Passed)![ \t]*-[ \t]*Failed:[ \t]*([0-9]+),[ \t]*Passed:[ \t]*([0-9]+),[ \t]*Skipped:[ \t]*([0-9]+),[ \t]*Total:[ \t]*([0-9]+).*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VstestSummary();

    [GeneratedRegex("""\A(?:[ \t]*Test summary: total: ([0-9]+), failed: ([0-9]+), succeeded: ([0-9]+), skipped: ([0-9]+).*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MtpSummary();

    [GeneratedRegex("""\A(?:[ \t]*failed (.+?) \([0-9.]+m?s\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MtpFailed();

    // Every pattern below needs one of these (the patterns are anchored and case-sensitive).
    private static readonly SearchValues<string> Gate = Literals("): error ", " : error ", ": warning ", " Error(s)", "Build ", " Warning(s)", "Time Elapsed ", "Failed ", "Passed ", "Skipped ", "Failed:", "Test summary: ", "failed ");

    public override bool CannotClaim(string line) => Lacks(line, Gate);

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        var r = Has(line, "): error ") ? MsbError().Match(line) : Match.Empty;
        if (r.Success)
        {
            Found.Error("msbuild", G(r, 1), G(r, 4), G(r, 5));
            return 1;
        }

        r = MsbToolError().Match(line);
        if (r.Success)
        {
            Found.Error("msbuild", "", G(r, 2), G(r, 3));
            return 1;
        }

        if (Has(line, ": warning ") && MsbWarning().IsMatch(line))
        {
            return 1;
        }

        r = MsbCount().Match(line);
        if (r.Success)
        {
            Found.Summary("msbuild", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        r = MsbTerminal().Match(line);
        if (r.Success)
        {
            if (r.Groups[1].Success)
            {
                Found.Summary("msbuild", FailureFamily.Error, N(r, 1), null);
            }

            return 1;
        }

        if (MsbNoise().IsMatch(line))
        {
            return 1;
        }

        r = VstestFailed().Match(line);
        if (r.Success)
        {
            Found.Test("dotnet", G(r, 1));
            return 1;
        }

        if (VstestPassed().IsMatch(line))
        {
            return 1;
        }

        r = VstestSummary().Match(line);
        if (r.Success)
        {
            Found.Summary("dotnet", FailureFamily.Test, N(r, 1), N(r, 2), FailureMode.Sum);
            return 1;
        }

        r = MtpSummary().Match(line);
        if (r.Success)
        {
            Found.Summary("dotnet-mtp", FailureFamily.Test, N(r, 2), N(r, 3));
            return 1;
        }

        r = MtpFailed().Match(line);
        if (r.Success)
        {
            Found.Test("dotnet", G(r, 1));
            return 1;
        }

        return 0;
    }
}
