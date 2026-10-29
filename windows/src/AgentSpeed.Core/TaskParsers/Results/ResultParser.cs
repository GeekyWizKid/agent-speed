using System.Globalization;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Tool results (TASK-PARSERS 4): the output text <c>failures</c> reads (4.1), the outcome (4.2) and the sessions that
/// outlive a call (4.3). Unknown families give the empty text, an unknown outcome and no session.
/// </summary>
/// <summary>A result read once (<see cref="ResultParser.Read"/>).</summary>
internal readonly record struct ResultRead(TaskOutcome Outcome, string? Session, string? Text);

internal static class ResultParser
{
    private static readonly string[] PollFields = ["bash_id", "shell_id", "task_id"];

    /// <summary><c>result_text</c> (4.1), LF-normalised.</summary>
    public static string ResultText(TaskFamily family, JsonElement result)
    {
        switch (family)
        {
            case TaskFamily.Claude:
                if (result.ValueKind != JsonValueKind.Object)
                {
                    return "";
                }

                var text = TaskText.Lf(ResultBlocks.Text(TaskJson.Get(result, "content")));
                var cut = text.IndexOf('\n', StringComparison.Ordinal);
                if (ResultPatterns.ClaudeExit().IsMatch(cut < 0 ? text : text.AsSpan(0, cut)))
                {
                    return cut < 0 ? "" : text[(cut + 1)..];
                }

                return text;
            case TaskFamily.Codex:
                return CodexStatus.Of(result).Body;
            case TaskFamily.Omp or TaskFamily.Pi or TaskFamily.OpenClaw:
                return result.ValueKind == JsonValueKind.Object ? PiText(TaskJson.Get(result, "content")) : "";
            case TaskFamily.Droid:
                return result.ValueKind == JsonValueKind.Object ? TaskText.Lf(ResultBlocks.Text(TaskJson.Get(result, "content"))) : "";
            default:
                return "";
        }
    }

    /// <summary>
    /// <c>outcome</c> (4.2): the exit code, <c>failed</c> (null = unknown) and <paramref name="t"/> passed through.
    /// <paramref name="tool"/> and <paramref name="input"/> are the call's, when known.
    /// </summary>
    public static TaskOutcome Outcome(TaskFamily family, JsonElement result, long? t, string? tool, JsonElement input, string? cwd) =>
        Read(family, result, t, tool, input, cwd, session: false, text: false).Outcome;

    /// <summary>
    /// One result read once: its <see cref="Outcome"/>, and when asked its <see cref="RunningSession"/> and
    /// <see cref="ResultText"/> — the same values the three give one by one, with a Codex result's status read only once.
    /// </summary>
    /// <param name="command">The call's command analysis when the caller has it (<c>ShellCommands.Classify</c>), else null.</param>
    internal static ResultRead Read(TaskFamily family, JsonElement result, long? t, string? tool, JsonElement input, string? cwd, bool session, bool text,
        ShellAnalysis? command = null)
    {
        var known = command;
        CodexStatus? codex = null;
        string? body = null;
        long? exit;
        bool? failed;
        ResultState state;
        switch (family)
        {
            case TaskFamily.Claude when result.ValueKind == JsonValueKind.Object:
                ClaudeOutcome(result, tool, out exit, out failed, out state);
                break;
            case TaskFamily.Codex:
                var status = CodexStatus.Of(result);
                codex = status;
                exit = status.Exit;
                state = status.State;
                failed = exit is { } code ? code != 0 : status.Patch;
                break;
            case TaskFamily.Omp or TaskFamily.Pi or TaskFamily.OpenClaw when result.ValueKind == JsonValueKind.Object:
                PiOutcome(result, out exit, out failed, out state);
                break;
            case TaskFamily.Droid when result.ValueKind == JsonValueKind.Object:
                exit = null;
                state = ResultState.Done;
                failed = TaskJson.IsTrue(TaskJson.Get(result, "is_error"));
                break;
            default:
                return new ResultRead(new TaskOutcome(null, null, t), null, text ? "" : null);
        }

        if (tool is not null && state == ResultState.Done)
        {
            var (background, shell) = ShellCallState(family, tool, input, cwd, known);
            if (background)
            {
                state = ResultState.Running;
            }
            else if (shell is { Info.Masked: true } masked)
            {
                // A masked command's status is the pipeline's unless its own output shows the chosen segment's (3.10).
                if (MaskedRuns.Status(masked, body ??= codex?.Body ?? ResultText(family, result)) is { } shown)
                {
                    failed = shown.Failed;
                    if (shown.ExitCode is not null || shown.Failed)
                    {
                        exit = shown.ExitCode;
                    }
                }
                else
                {
                    failed = null;
                }
            }
        }

        if (state != ResultState.Done)
        {
            failed = null;
            if (state is ResultState.Running or ResultState.Aborted)
            {
                exit = null;
            }
        }

        // A result that leaves a session running has neither an exit code nor a verdict (4.2, 4.3).
        var running = session && exit is null && failed is null ? codex is { } c ? c.Session : RunningSession(family, result) : null;
        return new ResultRead(new TaskOutcome(exit, failed, t), running, text ? body ?? codex?.Body ?? ResultText(family, result) : null);
    }

    /// <summary>
    /// <c>continuation</c> (4.3): the session a Codex <c>write_stdin</c> or a Claude <c>BashOutput</c> /
    /// <c>TaskOutput</c> call continues, or null.
    /// </summary>
    public static string? Continuation(TaskFamily family, string? tool, JsonElement input)
    {
        if (family == TaskFamily.Codex && tool == "write_stdin")
        {
            return TaskJson.IsInt(TaskJson.Get(TaskJson.AsObject(input), "session_id"), out var id)
                ? "codex:" + id.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        if (family == TaskFamily.Claude && (tool is "BashOutput" or "TaskOutput"))
        {
            var args = TaskJson.AsObject(input);
            foreach (var field in PollFields)
            {
                if (TaskJson.GetString(args, field) is { Length: > 0 } value)
                {
                    return "claude:" + value;
                }
            }
        }

        return null;
    }

    /// <summary><c>running_session</c> (4.3): the session a result leaves running, or null.</summary>
    public static string? RunningSession(TaskFamily family, JsonElement result)
    {
        if (family == TaskFamily.Codex)
        {
            return CodexStatus.Of(result).Session;
        }

        if (family == TaskFamily.Claude && result.ValueKind == JsonValueKind.Object)
        {
            var m = ResultPatterns.ClaudeBackground().Match(ResultBlocks.FirstLine(TaskJson.Get(result, "content")));
            return m.Success ? "claude:" + m.Groups[1].Value : null;
        }

        return null;
    }

    /// <summary>
    /// <c>notification</c> (4.3): the session and exit code of a Claude Code background-task notice (a line of type
    /// <c>attachment</c>), or null.
    /// </summary>
    public static TaskNotification? Notification(TaskFamily family, JsonElement line)
    {
        if (family != TaskFamily.Claude || line.ValueKind != JsonValueKind.Object || TaskJson.GetString(line, "type") != "attachment")
        {
            return null;
        }

        var attachment = TaskJson.Get(line, "attachment");
        if (attachment.ValueKind != JsonValueKind.Object
            || TaskJson.GetString(attachment, "type") != "queued_command"
            || TaskJson.GetString(attachment, "commandMode") != "task-notification"
            || TaskJson.GetString(attachment, "prompt") is not { } prompt)
        {
            return null;
        }

        var text = TaskText.Lf(prompt);
        var task = ResultPatterns.NotifyTask().Match(text);
        var summary = ResultPatterns.NotifySummary().Match(text);
        if (!task.Success || !summary.Success)
        {
            return null;
        }

        var m = ResultPatterns.NotifyExit().Match(TaskText.Trim(summary.Groups[1].Value));
        if (!m.Success)
        {
            return null;
        }

        var digits = m.Groups[1].Success ? m.Groups[1] : m.Groups[2];
        return new TaskNotification("claude:" + task.Groups[1].Value, TaskText.Num(digits.ValueSpan));
    }

    private static void ClaudeOutcome(JsonElement result, string? tool, out long? exit, out bool? failed, out ResultState state)
    {
        var content = TaskJson.Get(result, "content");
        var polling = tool is "BashOutput" or "TaskOutput";

        // The abort prefixes hold no line break, so the first line decides them as the whole text would.
        string? text = null;
        string first;
        if (polling)
        {
            text = TaskText.Lf(ResultBlocks.Text(content));
            var cut = text.IndexOf('\n', StringComparison.Ordinal);
            first = cut < 0 ? text : text[..cut];
        }
        else
        {
            first = ResultBlocks.FirstLine(content);
        }

        exit = null;
        state = ResultState.Done;
        var m = ResultPatterns.ClaudeExit().Match(first);
        if (m.Success)
        {
            exit = TaskText.Num(m.Groups[1].ValueSpan);
        }

        if (StartsWithAny(first, ResultPatterns.ClaudeAbort))
        {
            state = ResultState.Aborted;
        }
        else if (ResultPatterns.ClaudeTimeout().IsMatch(first))
        {
            state = ResultState.Timeout;
        }
        else if (ResultPatterns.ClaudeBackground().IsMatch(first))
        {
            state = ResultState.Running;
        }
        else if (text is not null)
        {
            var status = ResultPatterns.BashOutputStatus().Match(text);
            if (status.Success && status.Groups[1].ValueSpan.SequenceEqual("running"))
            {
                state = ResultState.Running;
            }

            var statusExit = ResultPatterns.BashOutputExit().Match(text);
            if (statusExit.Success && exit is null && status.Success)
            {
                exit = TaskText.Num(statusExit.Groups[1].ValueSpan);
            }
        }

        failed = exit is { } code ? code != 0 : TaskJson.IsTrue(TaskJson.Get(result, "is_error"));
    }

    private static void PiOutcome(JsonElement result, out long? exit, out bool? failed, out ResultState state)
    {
        var text = TaskText.Lf(ResultBlocks.Text(TaskJson.Get(result, "content")));
        var lines = PiResultLines.Of(text);
        var details = TaskJson.Get(result, "details");

        exit = null;
        if (TaskJson.IsInt(TaskJson.Get(details, "exitCode"), out var code) || TaskJson.IsInt(TaskJson.Get(details, "exit_code"), out code))
        {
            exit = code;
        }
        else if (lines.Count > 0 && ResultPatterns.PiExit().Match(lines.Last(text)) is { Success: true } m)
        {
            exit = TaskText.Num(m.Groups[1].ValueSpan);
        }

        state = ResultState.Done;
        if (TaskJson.GetString(details, "status") == "running" || (lines.Count > 0 && ResultPatterns.PiRunning().IsMatch(lines.FirstSpan(text))))
        {
            state = ResultState.Running;
        }
        else if (TaskJson.IsTrue(TaskJson.Get(details, "timedOut"))
                 || (lines.Count > 0 && ResultPatterns.PiTimeout().IsMatch(lines.LastSpan(text)))
                 || (lines.Count > 1 && ResultPatterns.PiTimeout().IsMatch(lines.SecondLastSpan(text))))
        {
            state = ResultState.Timeout;
        }
        else if (lines.Count > 0 && (ResultPatterns.PiAbort().IsMatch(lines.LastSpan(text)) || ResultPatterns.PiAbort().IsMatch(lines.FirstSpan(text))))
        {
            state = ResultState.Aborted;
        }

        failed = exit is { } value ? value != 0 : TaskJson.IsTrue(TaskJson.Get(result, "isError"));
    }

    /// <summary>
    /// The omp, Pi and OpenClaw text (4.1): trailing lines that are empty after trimming dropped, then a last line that,
    /// trimmed, matches <c>PI_EXIT</c>.
    /// </summary>
    private static string PiText(JsonElement content)
    {
        var text = TaskText.Lf(ResultBlocks.Text(content));
        var end = text.Length;
        int start;
        while (true)
        {
            start = LineStart(text, end);
            if (!text.AsSpan(start, end - start).Trim(" \t").IsEmpty)
            {
                break;
            }

            if (start == 0)
            {
                return "";
            }

            end = start - 1;
        }

        if (ResultPatterns.PiExit().IsMatch(text.AsSpan(start, end - start).Trim(" \t")))
        {
            if (start == 0)
            {
                return "";
            }

            end = start - 1;
        }

        return end == text.Length ? text : text[..end];
    }

    /// <summary>The start of the line that ends at <paramref name="end"/> (a line break or the text's end).</summary>
    internal static int LineStart(string text, int end) => end == 0 ? 0 : text.LastIndexOf('\n', end - 1) + 1;

    private static bool StartsWithAny(string text, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The call's part of 4.2: whether the input or the command backgrounds it, and the command's analysis (for
    /// <c>masked</c> and 3.10). A call without command text is neither.
    /// </summary>
    private static (bool Background, ShellAnalysis? Command) ShellCallState(TaskFamily family, string tool, JsonElement input, string? cwd,
        ShellAnalysis? known = null)
    {
        if (ShellCommands.CommandText(family, tool, input) is not { } text)
        {
            return (false, null);
        }

        var flag = family switch
        {
            TaskFamily.Claude => TaskJson.IsTrue(TaskJson.Get(TaskJson.AsObject(input), "run_in_background")),
            TaskFamily.OpenClaw => TaskJson.IsTrue(TaskJson.Get(TaskJson.AsObject(input), "background")),
            _ => false,
        };
        if (flag)
        {
            return (true, null);
        }

        var command = known ?? ShellCommands.Analyze(text, cwd);
        return (command.Info.Background, command);
    }
}
