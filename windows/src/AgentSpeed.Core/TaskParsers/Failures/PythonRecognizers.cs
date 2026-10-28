using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>pytest (TASK-PARSERS 5.3): <c>FAILED</c> / <c>ERROR</c> node ids and the session summary.</summary>
internal sealed partial class PytestRecognizer(FailureFound found) : FailureRecognizer(found)
{
    [GeneratedRegex("""\A(?:(?:\[gw[0-9]+\] \[ *[0-9]+%\] )?(FAILED|ERROR) (?!\()([^ ]+?)(?: - (.*))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PytestFail();

    [GeneratedRegex("""\A(?:([^ ]+::.+?) (FAILED|ERROR)(?: +\[ *[0-9]+%\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PytestVerbose();

    [GeneratedRegex("""\A(?:(?:=+ )?((?:[0-9]+ (?:failed|passed|errors?|skipped|xfailed|xpassed|deselected|warnings?|rerun|reruns|subtests passed|subtests failed)(?:, )?)+) in [0-9]+(?:\.[0-9]+)?s(?: \([0-9:.]+\))?(?: =+)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PytestSummary();

    [GeneratedRegex("""\A(?:(?:=+ )?no tests ran in [0-9]+(?:\.[0-9]+)?s(?: =+)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex PytestNone();

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        string? nodeId = null;
        var r = PytestFail().Match(line);
        if (r.Success)
        {
            nodeId = G(r, 2);
        }
        else
        {
            r = PytestVerbose().Match(line);
            if (r.Success)
            {
                nodeId = G(r, 1);
            }
        }

        if (nodeId is not null)
        {
            var sep = nodeId.IndexOf("::", StringComparison.Ordinal);
            Found.Test("pytest", sep < 0 ? "" : nodeId[(sep + 2)..], sep < 0 ? nodeId : nodeId[..sep]);
            return 1;
        }

        r = PytestSummary().Match(line);
        if (r.Success)
        {
            var c = FailureCounts.Parse(G(r, 1));
            var failed = FailureCounts.Add(FailureCounts.Add(c.CountOf("failed"), c.CountOf("error")), c.CountOf("errors"));
            Found.Summary("pytest", FailureFamily.Test, failed, c.CountOf("passed"));
            return 1;
        }

        if (PytestNone().IsMatch(line))
        {
            Found.Summary("pytest", FailureFamily.Test, 0, 0);
            return 1;
        }

        return 0;
    }
}

/// <summary>unittest and Django (TASK-PARSERS 5.3): <c>FAIL: method (dotted.path)</c> and the <c>Ran N tests</c> total.</summary>
internal sealed partial class UnittestRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private long? ran;

    [GeneratedRegex("""\A(?:(FAIL|ERROR|UNEXPECTED SUCCESS): ([A-Za-z_][A-Za-z0-9_]*) \(([A-Za-z0-9_.]+)\)(?: .*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex UnittestFail();

    [GeneratedRegex("""\A(?:Ran ([0-9]+) tests? in [0-9]+(?:\.[0-9]+)?s)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex UnittestRan();

    [GeneratedRegex("""\A(?:OK(?: \((.*)\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex UnittestOk();

    [GeneratedRegex("""\A(?:FAILED \((.*)\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex UnittestFailed();

    [GeneratedRegex("""\A(?:[ \t]*([a-z ]+)=([0-9]+)[ \t]*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex UnittestPair();

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        var r = UnittestFail().Match(line);
        if (r.Success)
        {
            var name = G(r, 2);
            var where = G(r, 3);
            Found.Test("unittest", where.EndsWith("." + name, StringComparison.Ordinal) ? where : where + "." + name);
            return 1;
        }

        r = UnittestRan().Match(line);
        if (r.Success)
        {
            ran = N(r, 1);
            return 1;
        }

        if (ran is { } total)
        {
            r = UnittestOk().Match(line);
            if (!r.Success)
            {
                r = UnittestFailed().Match(line);
            }

            if (r.Success)
            {
                var c = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var part in G(r, 1).Split(','))
                {
                    var pair = UnittestPair().Match(part);
                    if (pair.Success)
                    {
                        c[G(pair, 1)] = N(pair, 2);
                    }
                }

                var failed = c.CountOf("failures") + c.CountOf("errors") + c.CountOf("unexpected successes");
                var passed = Math.Max(0, total - failed - c.CountOf("skipped") - c.CountOf("expected failures"));
                Found.Summary("unittest", FailureFamily.Test, failed, passed);
                ran = null;
                return 1;
            }
        }

        return 0;
    }
}
