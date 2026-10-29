using System.Buffers;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// jest, vitest, mocha, bun and deno (TASK-PARSERS 5.3): failed tests under their file headers, mocha's numbered
/// failure blocks and each runner's totals.
/// </summary>
internal sealed partial class JsRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private readonly Dictionary<string, long> bun = new(StringComparer.Ordinal);
    private string jestFile = "";
    private Dictionary<string, long>? jestTests;
    private Dictionary<string, long>? jestSuites;
    private string vitestFile = "";
    private Dictionary<string, long>? vitestTests;
    private Dictionary<string, long>? vitestFiles;
    private long? mochaPassing;
    private long? mochaFailing;
    private bool mochaDetails;

    [GeneratedRegex("""\A(?:([ \t]*)([0-9]+)\) (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MochaItem();

    [GeneratedRegex("""\A(?: *FAIL +([^ ]+) \[ (.+) \])\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VitestLoad();

    [GeneratedRegex("""\A(?: *FAIL +(.+?) > (.+?)(?: \[[^\]]*\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VitestFail();

    [GeneratedRegex("""\A(?: ?(FAIL|PASS) +([^ ]+)(?: \([0-9.]+ ?m?s\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JestFile();

    [GeneratedRegex("""\A(?:  ● (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JestBullet();

    [GeneratedRegex("""\A(?:Test Suites:[ \t]+(.*[0-9]+ total))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JestSuites();

    [GeneratedRegex("""\A(?:Tests:[ \t]+(.*[0-9]+ total))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JestTests();

    [GeneratedRegex("""\A(?:(?:Snapshots:|Time:|Ran all test suites).*|[ \t]+[✓✕○✎] .*|Summary of all failing tests)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JestNoise();

    [GeneratedRegex("""\A(?: *[❯>✓] ([^ ]+) \([0-9]+ tests?(?: \|.*)?\)(?: [0-9.]+m?s)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VitestFile();

    [GeneratedRegex("""\A(?: +[×✗] (.+?)(?: [0-9.]+m?s)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VitestX();

    [GeneratedRegex("""\A(?: *Test Files +(.+?) \(([0-9]+)\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VitestFiles();

    [GeneratedRegex("""\A(?: *Tests +(.+?) \(([0-9]+)\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex VitestTests();

    [GeneratedRegex("""\A(?:[ \t]*([0-9]+) passing \([0-9a-z.]+\))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MochaPassing();

    [GeneratedRegex("""\A(?:[ \t]*([0-9]+) failing)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MochaFailing();

    [GeneratedRegex("""\A(?:[ \t]*([0-9]+) pending)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MochaPending();

    [GeneratedRegex("""\A(?:\(fail\) (.+?)(?: \[[0-9.]+m?s\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex BunFail();

    [GeneratedRegex("""\A(?: *([0-9]+) (pass|fail))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex BunCount();

    [GeneratedRegex("""\A(?:(?:ok|FAILED) \| ([0-9]+) passed(?: \([0-9]+ steps?\))? \| ([0-9]+) failed(?: \([0-9]+ steps?\))?.*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex DenoSummary();

    [GeneratedRegex("""\A(?:(.+?) => (.+?):[0-9]+:[0-9]+)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex DenoFailure();

    // Every pattern below needs one of these (the patterns are anchored and case-sensitive).
    private static readonly SearchValues<string> Gate = Literals("\u25cf", "Test Suites:", "Tests:", "Snapshots:", "Time:", "Ran all test suites", "\u2713", "\u2715", "\u25cb", "\u270e", "Summary of all failing tests", "\u276f ", "> ", "\u00d7", "\u2717", "Test Files ", "Tests ", " passing (", " failing", " pending", "(fail) ", " | ", " => ");

    // Besides: FAIL / PASS headers after leading spaces (vitest, jest) and bun's "N pass" / "N fail" counts.
    public override bool CannotClaim(string line) =>
        !mochaDetails && Lacks(line, Gate) && !line.AsSpan().TrimStart(' ').StartsWith("FAIL ") && !line.AsSpan().TrimStart(' ').StartsWith("PASS ")
        && !line.EndsWith(" pass", StringComparison.Ordinal) && !line.EndsWith(" fail", StringComparison.Ordinal);

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        Match r;
        if (mochaDetails)
        {
            r = MochaItem().Match(line);
            if (r.Success)
            {
                var used = MochaBlock(lines, i, G(r, 1).Length, G(r, 3));
                if (used > 0)
                {
                    return used;
                }
            }
        }

        r = VitestLoad().Match(line);
        if (r.Success)
        {
            Found.Test("vitest", "", G(r, 1));
            return 1;
        }

        r = VitestFail().Match(line);
        if (r.Success)
        {
            Found.Test("vitest", G(r, 2), G(r, 1));
            return 1;
        }

        r = JestFile().Match(line);
        if (r.Success)
        {
            jestFile = G(r, 1) == "FAIL" ? G(r, 2) : "";
            return 1;
        }

        r = JestBullet().Match(line);
        if (r.Success)
        {
            if (G(r, 1) != "Console")
            {
                Found.Test("jest", G(r, 1), jestFile);
            }

            return 1;
        }

        r = JestSuites().Match(line);
        if (r.Success)
        {
            FlushJest();
            jestSuites = FailureCounts.Parse(G(r, 1));
            return 1;
        }

        r = JestTests().Match(line);
        if (r.Success)
        {
            jestTests = FailureCounts.Parse(G(r, 1));
            FlushJest();
            return 1;
        }

        if (JestNoise().IsMatch(line))
        {
            return 1;
        }

        r = VitestFile().Match(line);
        if (r.Success)
        {
            vitestFile = G(r, 1);
            return 1;
        }

        r = VitestX().Match(line);
        if (r.Success && vitestFile.Length > 0)
        {
            Found.Test("vitest", G(r, 1), vitestFile);
            return 1;
        }

        r = VitestFiles().Match(line);
        if (r.Success)
        {
            FlushVitest();
            vitestFiles = FailureCounts.Parse(G(r, 1), '|');
            return 1;
        }

        r = VitestTests().Match(line);
        if (r.Success)
        {
            vitestTests = FailureCounts.Parse(G(r, 1), '|');
            FlushVitest();
            return 1;
        }

        r = MochaPassing().Match(line);
        if (r.Success)
        {
            mochaPassing = N(r, 1);
            return 1;
        }

        r = MochaFailing().Match(line);
        if (r.Success)
        {
            mochaFailing = N(r, 1);
            mochaDetails = true;
            return 1;
        }

        if (MochaPending().IsMatch(line))
        {
            return 1;
        }

        r = BunFail().Match(line);
        if (r.Success)
        {
            Found.Test("bun", G(r, 1));
            return 1;
        }

        r = BunCount().Match(line);
        if (r.Success)
        {
            bun[G(r, 2)] = N(r, 1);
            return 1;
        }

        r = DenoSummary().Match(line);
        if (r.Success)
        {
            Found.Summary("deno", FailureFamily.Test, N(r, 2), N(r, 1));
            return 1;
        }

        r = Has(line, " => ") ? DenoFailure().Match(line) : Match.Empty;
        if (r.Success)
        {
            Found.Test("deno", G(r, 1), G(r, 2));
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// A numbered mocha block: the title with its more-indented continuation lines up to the part ending <c>:</c>.
    /// Returns the lines it uses, or 0 when the joined title does not end with <c>:</c>.
    /// </summary>
    private int MochaBlock(string[] lines, int i, int indent, string first)
    {
        var parts = new List<string> { first };
        var k = i;
        while (!parts[^1].EndsWith(':')
               && k + 1 < lines.Length
               && !lines[k + 1].AsSpan().Trim(" \t").IsEmpty
               && LeadingSpaces(lines[k + 1]) > indent)
        {
            k++;
            parts.Add(TaskText.Trim(lines[k]));
        }

        for (var p = 0; p < parts.Count; p++)
        {
            parts[p] = TaskText.Trim(parts[p]);
        }

        var title = string.Join(' ', parts);
        if (!title.EndsWith(':'))
        {
            return 0;
        }

        Found.Test("mocha", title[..^1]);
        return k - i + 1;
    }

    private static int LeadingSpaces(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ')
        {
            n++;
        }

        return n;
    }

    private void Flush(string framework, Dictionary<string, long>? tests, Dictionary<string, long>? files)
    {
        if (tests is null && files is null)
        {
            return;
        }

        var failed = tests.CountOf("failed");
        if (failed == 0 && files is { Count: > 0 })
        {
            failed = files.CountOf("failed");
        }

        Found.Summary(framework, FailureFamily.Test, failed, tests.CountOf("passed"), FailureMode.Sum);
    }

    private void FlushJest()
    {
        Flush("jest", jestTests, jestSuites);
        jestTests = jestSuites = null;
    }

    private void FlushVitest()
    {
        Flush("vitest", vitestTests, vitestFiles);
        vitestTests = vitestFiles = null;
    }

    public override void Finish()
    {
        FlushJest();
        FlushVitest();
        if (mochaPassing is not null || mochaFailing is not null)
        {
            Found.Summary("mocha", FailureFamily.Test, mochaFailing ?? 0, mochaPassing ?? 0);
        }

        if (bun.Count > 0)
        {
            Found.Summary("bun", FailureFamily.Test, bun.CountOf("fail"), bun.CountOf("pass"));
        }
    }
}
