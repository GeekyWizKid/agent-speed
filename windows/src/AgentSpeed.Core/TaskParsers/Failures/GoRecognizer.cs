using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// go test, build and vet (TASK-PARSERS 5.3): <c>--- FAIL</c> tests and compiler errors; the summary is made at the
/// segment's end from the tests and package lines seen.
/// </summary>
internal sealed partial class GoRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private readonly HashSet<string> failed = new(StringComparer.Ordinal);
    private readonly HashSet<string> passed = new(StringComparer.Ordinal);
    private int ok;
    private int packages;
    private int broken;

    [GeneratedRegex("""\A(?:[ \t]*--- FAIL: ([^ ]+) \([0-9.]+s\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoFailShared();


    private static Regex GoFail() => ThreadRegex.Of(GoFailShared());

    [GeneratedRegex("""\A(?:[ \t]*--- (PASS|SKIP): ([^ ]+) \([0-9.]+s\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoPassShared();


    private static Regex GoPass() => ThreadRegex.Of(GoPassShared());

    [GeneratedRegex("""\A(?:=== (?:RUN|PAUSE|CONT|NAME) .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoRunShared();


    private static Regex GoRun() => ThreadRegex.Of(GoRunShared());

    [GeneratedRegex("""\A(?:(?:FAIL|PASS|\?[ \t]+[^ \t]+[ \t]+\[no test files\]))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoBareShared();


    private static Regex GoBare() => ThreadRegex.Of(GoBareShared());

    [GeneratedRegex("""\A(?:# [^ ]+(?: \[[^\]]+\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoHeaderShared();


    private static Regex GoHeader() => ThreadRegex.Of(GoHeaderShared());

    [GeneratedRegex("""\A(?:(FAIL|ok)[ \t]+([^ \t]+)[ \t]+(?:[0-9.]+s|\(cached\)|\[([^\]]+)\])(?:[ \t].*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoPkgShared();


    private static Regex GoPkg() => ThreadRegex.Of(GoPkgShared());

    [GeneratedRegex("""\A(?:([^ \t][^ \t:]*\.go):([0-9]+)(?::([0-9]+))?: (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GoErrorShared();


    private static Regex GoError() => ThreadRegex.Of(GoErrorShared());

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        var r = GoFail().Match(line);
        if (r.Success)
        {
            failed.Add(G(r, 1));
            Found.Test("go", G(r, 1));
            return 1;
        }

        r = GoPass().Match(line);
        if (r.Success)
        {
            if (G(r, 1) == "PASS")
            {
                passed.Add(G(r, 2));
            }

            return 1;
        }

        if (GoRun().IsMatch(line) || GoBare().IsMatch(line) || GoHeader().IsMatch(line))
        {
            return 1;
        }

        r = GoPkg().Match(line);
        if (r.Success)
        {
            packages++;
            if (G(r, 1) == "ok")
            {
                ok++;
            }
            else if (G(r, 3) is "build failed" or "setup failed")
            {
                broken++;
            }

            return 1;
        }

        r = GoError().Match(line);
        if (r.Success)
        {
            Found.Error("go", G(r, 1), "", G(r, 4));
            return 1;
        }

        return 0;
    }

    public override void Finish()
    {
        if (packages > 0 || failed.Count > 0 || passed.Count > 0)
        {
            long? passes = passed.Count > 0 ? passed.Count : ok > 0 ? ok : null;
            Found.Summary("go", FailureFamily.Test, failed.Count + broken, passes);
        }
    }
}
