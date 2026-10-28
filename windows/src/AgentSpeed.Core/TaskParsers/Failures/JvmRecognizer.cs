using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The JVM (TASK-PARSERS 5.3): JUnit through Gradle and Surefire (with the <c>[ERROR] Failures:</c> listing), javac,
/// kotlinc and Maven's compiler totals.
/// </summary>
internal sealed partial class JvmRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private bool listing;

    [GeneratedRegex("""\A(?:\[(?:ERROR|WARNING|INFO)\] Tests run: ([0-9]+), Failures: ([0-9]+), Errors: ([0-9]+), Skipped: ([0-9]+)(?:, Flakes: [0-9]+)?(.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SurefireRun();

    [GeneratedRegex("""\A(?:\[ERROR\][ \t]+([A-Za-z0-9_$]+)\.([A-Za-z0-9_$\[\]]+)(?::[0-9]+)?(?:[ \t].*)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SurefireListed();

    [GeneratedRegex("""\A(?:([^ ]+) > (.+?) (FAILED|PASSED|SKIPPED))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GradleTest();

    [GeneratedRegex("""\A(?:([0-9]+) tests? completed, ([0-9]+) failed(?:, ([0-9]+) skipped)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GradleSummary();

    [GeneratedRegex("""\A(?:> Task [^ ]+(?: [A-Z-]+)?|FAILURE: Build failed with an exception\.|BUILD (?:SUCCESSFUL|FAILED) in .*|\* What went wrong:|\* Try:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GradleNoise();

    [GeneratedRegex("""\A(?:\[ERROR\] ([A-Za-z0-9_.$]+)\.([A-Za-z0-9_$\[\]]+)(?:\(.*\))? -- Time elapsed: .* <<< (?:FAILURE|ERROR)!)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SurefireTest();

    [GeneratedRegex("""\A(?:(?:\[ERROR\] )?([A-Za-z0-9_$]+)\(([A-Za-z0-9_.$]+)\)[ \t]+Time elapsed: .* <<< (?:FAILURE|ERROR)!)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SurefireOld();

    [GeneratedRegex("""\A(?:\[ERROR\] (?:Failures|Errors): ?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex SurefireHeader();

    [GeneratedRegex("""\A(?:\[ERROR\] (.+?\.(?:java|kt|scala)):\[([0-9]+),([0-9]+)\] (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MavenJavac();

    [GeneratedRegex("""\A(?:\[INFO\] ([0-9]+) errors?[ \t]*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MavenErrors();

    [GeneratedRegex("""\A(?:\[ERROR\] COMPILATION ERROR :?|\[INFO\] BUILD (?:FAILURE|SUCCESS))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MavenNoise();

    [GeneratedRegex("""\A(?:(.+?\.java):([0-9]+): error: (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JavacError();

    [GeneratedRegex("""\A(?:(.+?\.java):([0-9]+): warning: .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JavacWarning();

    [GeneratedRegex("""\A(?:([0-9]+) errors?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex JavacCount();

    [GeneratedRegex("""\A(?:e: (?:file://)?(.+?\.kts?):([0-9]+):([0-9]+) (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex KotlinError();

    [GeneratedRegex("""\A(?:e: (.+?\.kts?): \(([0-9]+), ([0-9]+)\): (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex KotlinErrorOld();

    [GeneratedRegex("""\A(?:w: .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex KotlinWarning();

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        Match r;
        if (listing)
        {
            if (SurefireRun().IsMatch(line) || line.StartsWith("[INFO]", StringComparison.Ordinal) || line is "[ERROR] " or "[ERROR]")
            {
                listing = false;
            }
            else
            {
                r = SurefireListed().Match(line);
                if (r.Success && !line.StartsWith("[ERROR]   Run ", StringComparison.Ordinal))
                {
                    Found.Test("junit", JunitId(G(r, 1), G(r, 2)));
                    return 1;
                }

                if (line.StartsWith("[ERROR]", StringComparison.Ordinal) || line.StartsWith("[WARNING]", StringComparison.Ordinal))
                {
                    return 1;
                }

                listing = false;
            }
        }

        r = GradleTest().Match(line);
        if (r.Success)
        {
            if (G(r, 3) == "FAILED")
            {
                Found.Test("junit", JunitId(G(r, 1), G(r, 2)));
            }

            return 1;
        }

        r = GradleSummary().Match(line);
        if (r.Success)
        {
            var total = N(r, 1);
            var failed = N(r, 2);
            Found.Summary("gradle", FailureFamily.Test, failed, Math.Max(0, total - failed - N(r, 3)), FailureMode.Sum);
            return 1;
        }

        if (GradleNoise().IsMatch(line))
        {
            return 1;
        }

        r = SurefireTest().Match(line);
        if (r.Success)
        {
            Found.Test("junit", JunitId(G(r, 1), G(r, 2)));
            return 1;
        }

        r = SurefireOld().Match(line);
        if (r.Success)
        {
            Found.Test("junit", JunitId(G(r, 2), G(r, 1)));
            return 1;
        }

        if (SurefireHeader().IsMatch(line))
        {
            listing = true;
            return 1;
        }

        r = SurefireRun().Match(line);
        if (r.Success)
        {
            if (TaskText.Trim(G(r, 5)).Length == 0)
            {
                var run = N(r, 1);
                var failures = N(r, 2);
                var errors = N(r, 3);
                Found.Summary("maven", FailureFamily.Test, failures + errors, Math.Max(0, run - failures - errors - N(r, 4)));
            }

            return 1;
        }

        r = MavenJavac().Match(line);
        if (r.Success)
        {
            Found.Error("javac", G(r, 1), "", G(r, 4));
            return 1;
        }

        r = MavenErrors().Match(line);
        if (r.Success)
        {
            Found.Summary("javac", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        if (MavenNoise().IsMatch(line))
        {
            return 1;
        }

        r = JavacError().Match(line);
        if (r.Success)
        {
            Found.Error("javac", G(r, 1), "", G(r, 3));
            return 1;
        }

        if (JavacWarning().IsMatch(line))
        {
            return 1;
        }

        r = JavacCount().Match(line);
        if (r.Success)
        {
            Found.Summary("javac", FailureFamily.Error, N(r, 1), null);
            return 1;
        }

        r = KotlinError().Match(line);
        if (!r.Success)
        {
            r = KotlinErrorOld().Match(line);
        }

        if (r.Success)
        {
            Found.Error("kotlinc", G(r, 1), "", G(r, 4));
            return 1;
        }

        return KotlinWarning().IsMatch(line) ? 1 : 0;
    }

    /// <summary><c>junit_id</c>: the class's last dot component, <c>.</c>, the method without a trailing <c>()</c>.</summary>
    private static string JunitId(string cls, string method)
    {
        var simple = cls[(cls.LastIndexOf('.') + 1)..];
        return simple + "." + (method.EndsWith("()", StringComparison.Ordinal) ? method[..^2] : method);
    }
}
