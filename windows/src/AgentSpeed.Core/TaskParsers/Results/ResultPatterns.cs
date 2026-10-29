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
    private static partial Regex ClaudeExitShared();


    public static Regex ClaudeExit() => ThreadRegex.Of(ClaudeExitShared());

    [GeneratedRegex(@"\A(?:Command timed out after .*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeTimeoutShared();


    public static Regex ClaudeTimeout() => ThreadRegex.Of(ClaudeTimeoutShared());

    [GeneratedRegex(@"\A(?:Command running in background with ID: ?([A-Za-z0-9_:-]+(?:\.[A-Za-z0-9_:-]+)*).*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeBackgroundShared();


    public static Regex ClaudeBackground() => ThreadRegex.Of(ClaudeBackgroundShared());

    [GeneratedRegex(@"\A(?:Exit code: (-?[0-9]+))\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodexExitShared();


    public static Regex CodexExit() => ThreadRegex.Of(CodexExitShared());

    [GeneratedRegex(@"\A(?:Process exited with code (-?[0-9]+))\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodexExitedShared();


    public static Regex CodexExited() => ThreadRegex.Of(CodexExitedShared());

    [GeneratedRegex(@"\A(?:Process running with session ID ([0-9]+))\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodexRunningShared();


    public static Regex CodexRunning() => ThreadRegex.Of(CodexRunningShared());

    /// <summary>A prefix match.</summary>
    [GeneratedRegex(@"\A(?:command timed out after [0-9]+ milliseconds)", RegexOptions.CultureInvariant)]
    private static partial Regex CodexTimeoutShared();

    public static Regex CodexTimeout() => ThreadRegex.Of(CodexTimeoutShared());

    [GeneratedRegex(@"\A(?:(Chunk ID|Wall time|Exit code|Process exited with code|Process running with session ID|Original token count|Total output lines)[: ].*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodexHeaderShared();


    public static Regex CodexHeader() => ThreadRegex.Of(CodexHeaderShared());

    [GeneratedRegex(@"\A(?:\(?Command exited with code (-?[0-9]+)\)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex PiExitShared();


    public static Regex PiExit() => ThreadRegex.Of(PiExitShared());

    [GeneratedRegex(@"\A(?:\[?Command timed out.*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex PiTimeoutShared();


    public static Regex PiTimeout() => ThreadRegex.Of(PiTimeoutShared());

    [GeneratedRegex(@"\A(?:(Command aborted.*|Command was terminated.*|Operation aborted.*|Command cancelled.*))\z", RegexOptions.CultureInvariant)]
    private static partial Regex PiAbortShared();


    public static Regex PiAbort() => ThreadRegex.Of(PiAbortShared());

    /// <summary>A prefix match.</summary>
    [GeneratedRegex(@"\A(?:Command still running \(session .*)", RegexOptions.CultureInvariant)]
    private static partial Regex PiRunningShared();

    public static Regex PiRunning() => ThreadRegex.Of(PiRunningShared());

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<status>([a-z_]+)</status>", RegexOptions.CultureInvariant)]
    private static partial Regex BashOutputStatusShared();

    public static Regex BashOutputStatus() => ThreadRegex.Of(BashOutputStatusShared());

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<exit_code>(-?[0-9]+)</exit_code>", RegexOptions.CultureInvariant)]
    private static partial Regex BashOutputExitShared();

    public static Regex BashOutputExit() => ThreadRegex.Of(BashOutputExitShared());

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<task-id>([^<]+)</task-id>", RegexOptions.CultureInvariant)]
    private static partial Regex NotifyTaskShared();

    public static Regex NotifyTask() => ThreadRegex.Of(NotifyTaskShared());

    /// <summary>A search.</summary>
    [GeneratedRegex(@"<summary>([^\n]*?)</summary>", RegexOptions.CultureInvariant)]
    private static partial Regex NotifySummaryShared();

    public static Regex NotifySummary() => ThreadRegex.Of(NotifySummaryShared());

    [GeneratedRegex(@"\A(?:Background command [^\n]*(?:\(exit code (-?[0-9]+)\)|with exit code (-?[0-9]+)))\z", RegexOptions.CultureInvariant)]
    private static partial Regex NotifyExitShared();


    public static Regex NotifyExit() => ThreadRegex.Of(NotifyExitShared());
}
