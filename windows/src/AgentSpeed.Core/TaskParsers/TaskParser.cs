using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The task-score parsers (spec/TASK-PARSERS.md): pure, deterministic functions that turn one tool call, its command
/// text, its result and its edit input into the small facts METRICS 6.9 keeps — kinds, classes, flags, suite names from
/// a fixed vocabulary, 16-hex digests, counts and exit codes.
/// <para>
/// Inputs are the values a log line stores, as <see cref="JsonElement"/>s (<c>default</c> = absent): the call's input
/// (an object, or a string for Codex), the result block or message, and the agent's working directory
/// (<c>cwdPath</c>) or null. Every function accepts any input and never throws. Inputs are read in memory while the
/// call runs; nothing of them is stored or logged (TASK-PARSERS 7).
/// </para>
/// </summary>
public static class TaskParser
{
    /// <summary>The fixed suite vocabulary (<c>SUITES</c>, 3.8): <see cref="AgentSpeed.Core.TaskParsers.CommandInfo.Suite"/> is null or one of these.</summary>
    public static IReadOnlyList<string> Suites => CommandSuites.All;

    /// <summary><c>action_kind</c> (2): what the call did; shell commands are refined by their text (3.9).</summary>
    public static ActionKind ActionKind(SourceKind source, string? tool, JsonElement input, string? cwd) =>
        ShellCommands.ActionKind(TaskFamilies.Of(source), tool, input, cwd);

    /// <summary><c>command_text</c> (3.1): the command line a shell tool runs, or null.</summary>
    public static string? CommandText(SourceKind source, string? tool, JsonElement input) =>
        ShellCommands.CommandText(TaskFamilies.Of(source), tool, input);

    /// <summary><c>command_info</c> (3): class, suite, targeted, key, and the masked / background / error-filter flags.</summary>
    public static CommandInfo CommandInfo(string text, string? cwd) => ShellCommands.Info(text, cwd);

    /// <summary><c>masked_status</c> (3.10): the status a masked command shows in its own output, or null.</summary>
    public static MaskedStatus? MaskedStatus(string text, string outputText, string? cwd) => ShellCommands.MaskedStatusOf(text, outputText, cwd);

    /// <summary>
    /// <c>outcome</c> (4.2): exit code, <c>failed</c> (null = unknown, never a failure) and <paramref name="t"/> (the
    /// result line's Unix ms) passed through. <paramref name="tool"/> and <paramref name="input"/> are the call's.
    /// </summary>
    public static TaskOutcome Outcome(SourceKind source, JsonElement result, long? t, string? tool, JsonElement input, string? cwd) =>
        ResultParser.Outcome(TaskFamilies.Of(source), result, t, tool, input, cwd);

    /// <summary><c>result_text</c> (4.1): the output text that <see cref="Failures"/> reads, LF-normalised.</summary>
    public static string ResultText(SourceKind source, JsonElement result) => ResultParser.ResultText(TaskFamilies.Of(source), result);

    /// <summary><c>continuation</c> (4.3): the session a call continues (<c>codex:N</c>, <c>claude:ID</c>), or null.</summary>
    public static string? Continuation(SourceKind source, string? tool, JsonElement input) =>
        ResultParser.Continuation(TaskFamilies.Of(source), tool, input);

    /// <summary><c>running_session</c> (4.3): the session a result leaves running, or null.</summary>
    public static string? RunningSession(SourceKind source, JsonElement result) => ResultParser.RunningSession(TaskFamilies.Of(source), result);

    /// <summary><c>notification</c> (4.3): a Claude background-task notice line's session and exit code, or null.</summary>
    public static TaskNotification? Notification(SourceKind source, JsonElement line) => ResultParser.Notification(TaskFamilies.Of(source), line);

    /// <summary><c>code_script</c> (4.4): a Codex code-mode script's tool calls and printing statements, or null.</summary>
    public static CodeScript? CodeScript(SourceKind source, string? tool, JsonElement input) =>
        CodeModeParser.Script(TaskFamilies.Of(source), tool, input);

    /// <summary><c>code_items</c> (4.4): a completed script's printed items (null for an item without text), or null.</summary>
    public static IReadOnlyList<string?>? CodeItems(SourceKind source, JsonElement result) => CodeModeParser.Items(TaskFamilies.Of(source), result);

    /// <summary>
    /// <c>failures</c> (5): fingerprints, failed and passed counts and framework of a command's output.
    /// <paramref name="info"/> is the command's <see cref="CommandInfo(string, string?)"/>, <paramref name="outputText"/> its
    /// <see cref="ResultText"/> and <paramref name="failed"/> its <see cref="Outcome"/>'s <c>Failed</c>.
    /// </summary>
    public static FailureReport Failures(AgentSpeed.Core.TaskParsers.CommandInfo? info, string outputText, string? cwd, bool? failed) =>
        FailureParser.Failures(info, outputText, cwd, failed);

    /// <summary><c>edit_hunks</c> (6): the file / old / new digests of what an edit call changed, in input order.</summary>
    public static IReadOnlyList<EditHunk> EditHunks(SourceKind source, string? tool, JsonElement input, string? cwd) =>
        EditParser.Hunks(TaskFamilies.Of(source), tool, input, cwd);
}
