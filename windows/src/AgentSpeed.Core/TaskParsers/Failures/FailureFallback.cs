using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// What <c>failures</c> reads when the recognisers find nothing: the generic error lines of a failed run (TASK-PARSERS
/// 5.6) and the completion markers of a clean build (5.4).
/// </summary>
internal static partial class FailureFallback
{
    private static readonly string[] CompletionFrameworks = ["cargo", "swiftpm", "xcodebuild", "gradle", "maven", "prettier", "vite"];

    [GeneratedRegex("""\A(?:(.+?\.[A-Za-z0-9]+):([0-9]+)(?::([0-9]+))?:? (?:fatal )?error(?:\[([^\]]+)\])?:? (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GenericFileShared();


    private static Regex GenericFile() => ThreadRegex.Of(GenericFileShared());

    [GeneratedRegex("""\A(?:(?:error|ERROR|Error|fatal|FATAL)(?:\[([^\]]+)\])?: (.+))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GenericPrefixShared();


    private static Regex GenericPrefix() => ThreadRegex.Of(GenericPrefixShared());

    [GeneratedRegex("""\A(?:(?:Uncaught )?([A-Za-z_][A-Za-z0-9_.]*(?:Error|Exception)): (.*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex GenericExceptionShared();


    private static Regex GenericException() => ThreadRegex.Of(GenericExceptionShared());

    [GeneratedRegex("""\A(?:Finished .* target\(s\) in .+)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteCargoShared();


    private static Regex CompleteCargo() => ThreadRegex.Of(CompleteCargoShared());

    [GeneratedRegex("""\A(?:Build complete!(?: \(.*\))?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteSwiftpmShared();


    private static Regex CompleteSwiftpm() => ThreadRegex.Of(CompleteSwiftpmShared());

    [GeneratedRegex("""\A(?:\*\* [A-Z]+(?: [A-Z]+)* SUCCEEDED \*\*(?: \[.*\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteXcodebuildShared();


    private static Regex CompleteXcodebuild() => ThreadRegex.Of(CompleteXcodebuildShared());

    [GeneratedRegex("""\A(?:BUILD SUCCESSFUL(?: in .+)?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteGradleShared();


    private static Regex CompleteGradle() => ThreadRegex.Of(CompleteGradleShared());

    [GeneratedRegex("""\A(?:\[INFO\] BUILD SUCCESS)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteMavenShared();


    private static Regex CompleteMaven() => ThreadRegex.Of(CompleteMavenShared());

    [GeneratedRegex("""\A(?:All matched files use Prettier code style!)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompletePrettierShared();


    private static Regex CompletePrettier() => ThreadRegex.Of(CompletePrettierShared());

    [GeneratedRegex("""\A(?:\u2713 built in [0-9]+(?:\.[0-9]+)?(?:ms|s))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteViteShared();


    private static Regex CompleteVite() => ThreadRegex.Of(CompleteViteShared());

    /// <summary>Every unclaimed line as a <c>generic</c> error: <c>file:line: error</c>, an <c>error:</c> prefix or an exception.</summary>
    public static void Generic(FailureFound found, string[] lines, bool[] claimed)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (claimed[i])
            {
                continue;
            }

            var line = lines[i];
            var r = GenericFile().Match(line);
            if (r.Success)
            {
                found.Error("generic", r.Groups[1].Value, r.Groups[4].Value, r.Groups[5].Value);
                continue;
            }

            r = GenericPrefix().Match(line);
            if (r.Success)
            {
                found.Error("generic", "", r.Groups[1].Value, r.Groups[2].Value);
                continue;
            }

            r = GenericException().Match(line);
            if (r.Success)
            {
                found.Error("generic", "", r.Groups[1].Value, r.Groups[2].Value);
            }
        }
    }

    /// <summary>The framework of the first line that, trimmed, says a tool finished cleanly, or null.</summary>
    public static string? Completion(IEnumerable<string[]> segments)
    {
        foreach (var lines in segments)
        {
            foreach (var line in lines)
            {
                var k = CompletionIndex(line.AsSpan().Trim(" \t"));
                if (k >= 0)
                {
                    return CompletionFrameworks[k];
                }
            }
        }

        return null;
    }

    private static int CompletionIndex(ReadOnlySpan<char> line) =>
        CompleteCargo().IsMatch(line) ? 0
        : CompleteSwiftpm().IsMatch(line) ? 1
        : CompleteXcodebuild().IsMatch(line) ? 2
        : CompleteGradle().IsMatch(line) ? 3
        : CompleteMaven().IsMatch(line) ? 4
        : CompletePrettier().IsMatch(line) ? 5
        : CompleteVite().IsMatch(line) ? 6
        : -1;
}
