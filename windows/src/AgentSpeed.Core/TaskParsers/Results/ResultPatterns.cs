using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The result patterns of TASK-PARSERS 4 (Appendix A.4), verbatim. Full-line patterns are wrapped as
/// <c>\A(?:P)\z</c>, prefix matches as <c>\A(?:P)</c>; <i>search</i> patterns are left unanchored.
/// </summary>
internal static partial class ResultPatterns
{
    /// <summary>The prefixes of a Claude result the user interrupted or rejected (A.2 <c>CLAUDE_ABORT</c>).</summary>
    public static readonly string[] ClaudeAbort =
    [
        "[Request interrupted by user",
        "The user doesn't want to proceed with this tool use",
        "The user doesn't want to take this action",
        "Permission to use ",
        "Claude requested permissions to use",
        "User rejected",
        "The tool use was rejected",
    ];

    [GeneratedRegex(@"\A(?:Exit code (-?[0-9]+))\z", RegexOptions.CultureInvariant)]
    public static partial Regex ClaudeExit();

    [GeneratedRegex(@"\A(?:Command timed out after .*)\z", RegexOptions.CultureInvariant)]
    public static partial Regex ClaudeTimeout();

    [GeneratedRegex(@"\A(?:Command running in background with ID: ?([A-Za-z0-9_:-]+(?:\.[A-Za-z0-9_:-]+)*).*)\z", RegexOptions.CultureInvariant)]
    public static partial Regex ClaudeBackground();

    [GeneratedRegex(@"\A(?:Exit code: (-?[0-9]+))\z", RegexOptions.CultureInvariant)]
    public static partial Regex CodexExit();

    [GeneratedRegex(@"\A(?:Process exited with code (-?[0-9]+))\z", RegexOptions.CultureInvariant)]
    public static partial Regex CodexExited();

    [GeneratedRegex(@"\A(?:Process running with session ID ([0-9]+))\z", RegexOptions.CultureInvariant)]
    public static partial Regex CodexRunning();

    /// <summary>A prefix match.</summary>
    [GeneratedRegex(@"\A(?:command timed out after [0-9]+ milliseconds)", RegexOptions.CultureInvariant)]
    public static partial Regex CodexTimeout();

    [GeneratedRegex(@"\A(?:(Chunk ID|Wall time|Exit code|Process exited with code|Process running with session ID|Original token count|Total output lines)[: ].*)\z", RegexOptions.CultureInvariant)]
    public static partial Regex CodexHeader();

    [GeneratedRegex(@"\A(?:\(?Command exited with code (-?[0-9]+)\)?)\z", RegexOptions.CultureInvariant)]
    public static partial Regex PiExit();

    [GeneratedRegex(@"\A(?:\[?Command timed out.*)\z", RegexOptions.CultureInvariant)]
    public static partial Regex PiTimeout();

    [GeneratedRegex(@"\A(?:(Command aborted.*|Command was terminated.*|Operation aborted.*|Command cancelled.*))\z", RegexOptions.CultureInvariant)]
    public static partial Regex PiAbort();

    /// <summary>A prefix match.</summary>
    [GeneratedRegex(@"\A(?:Command still running \(session .*)", RegexOptions.CultureInvariant)]
    public static partial Regex PiRunning();

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<status>([a-z_]+)</status>", RegexOptions.CultureInvariant)]
    public static partial Regex BashOutputStatus();

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<exit_code>(-?[0-9]+)</exit_code>", RegexOptions.CultureInvariant)]
    public static partial Regex BashOutputExit();

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<task-id>([^<]+)</task-id>", RegexOptions.CultureInvariant)]
    public static partial Regex NotifyTask();

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<summary>([^\n]*?)</summary>", RegexOptions.CultureInvariant)]
    public static partial Regex NotifySummary();

    [GeneratedRegex(@"\A(?:Background command [^\n]*(?:\(exit code (-?[0-9]+)\)|with exit code (-?[0-9]+)))\z", RegexOptions.CultureInvariant)]
    public static partial Regex NotifyExit();
}
