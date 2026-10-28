using System.Text.Json;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// Claude Code and Claude Desktop lines (METRICS 6.9 <i>Composing the stream</i>): every line with a time counts; the cwd
/// is the latest non-blank one; prompts are user lines and queued messages; calls are an assistant line's
/// <c>tool_use</c> blocks, results a user line's <c>tool_result</c> blocks; task notices end background sessions.
/// </summary>
internal sealed class ClaudeTaskReader() : TaskLineReader(SourceKind.ClaudeCode)
{
    private const string Synthetic = "<synthetic>";

    public void Read(ReadOnlySpan<byte> raw, ref ClaudeLine line)
    {
        if (line.Status != LineStatus.Valid || !line.HasTimestamp)
        {
            return;
        }

        line.TaskTracked = true;
        if (NonBlank(line.Cwd))
        {
            Cwd = line.Cwd;
        }

        var t = line.TimestampMs;
        switch (line.Kind)
        {
            case ClaudeLineKind.User:
                line.Task = ReadUser(raw, t);
                break;
            case ClaudeLineKind.Attachment when raw.IndexOf("\"queued_command\""u8) >= 0:
                line.Task = ReadAttachment(raw, t);
                break;
            case ClaudeLineKind.Assistant when !string.Equals(line.Model, Synthetic, StringComparison.Ordinal):
                line.Task = ReadAssistant(raw, in line, t);
                break;
        }
    }

    private TaskLine? ReadUser(ReadOnlySpan<byte> raw, long t)
    {
        using var json = TaskJsonLine.Parse(raw);
        if (!json.IsObject)
        {
            return null;
        }

        var task = new TaskLine { Prompt = PromptLines.Classify(Source, json.Root) };
        var message = TaskInputs.Get(json.Root, "message");
        if (message.ValueKind == JsonValueKind.Object)
        {
            foreach (var block in TaskInputs.Blocks(message, "content"))
            {
                if (TaskInputs.IsType(block, "tool_result") && Result(TaskInputs.GetString(block, "tool_use_id"), block, t) is { } result)
                {
                    (task.Results ??= []).Add(result);
                }
            }
        }

        return task.IsEmpty ? null : task;
    }

    private TaskLine? ReadAttachment(ReadOnlySpan<byte> raw, long t)
    {
        using var json = TaskJsonLine.Parse(raw);
        if (!json.IsObject)
        {
            return null;
        }

        var task = new TaskLine { Prompt = PromptLines.Classify(Source, json.Root) };
        if (TaskParser.Notification(Source, json.Root) is { } notice)
        {
            var queued = QueuedTime(TaskInputs.Get(json.Root, "attachment"));
            task.Notice = new TaskNotice(notice.Session, notice.ExitCode, queued ?? t);
        }

        return task.IsEmpty ? null : task;
    }

    /// <summary>
    /// An assistant line with <c>tool_use</c> blocks: its calls (the composer takes the withdrawal decision of every
    /// assistant line from the line itself, <see cref="IsDecidingAssistant"/>).
    /// </summary>
    private TaskLine? ReadAssistant(ReadOnlySpan<byte> raw, in ClaudeLine line, long t)
    {
        if (!line.HasToolUse)
        {
            return null;
        }

        var task = new TaskLine();

        using var json = TaskJsonLine.Parse(raw);
        var message = json.IsObject ? TaskInputs.Get(json.Root, "message") : default;
        if (message.ValueKind != JsonValueKind.Object)
        {
            return task;
        }

        foreach (var block in TaskInputs.Blocks(message, "content"))
        {
            if (!TaskInputs.IsType(block, "tool_use"))
            {
                continue;
            }

            var input = TaskInputs.Get(block, "input");
            if (Call(TaskInputs.GetString(block, "id"), TaskInputs.GetString(block, "name"), input, TaskInputs.Digest(input), t) is { } call)
            {
                (task.Calls ??= []).Add(call);
            }
        }

        return task.Calls is null ? null : task;
    }

    /// <summary>An <c>assistant</c> line that is not <c>&lt;synthetic&gt;</c>: it decides a pending withdrawal (6.9).</summary>
    public static bool IsDecidingAssistant(in ClaudeLine line) =>
        line.Kind == ClaudeLineKind.Assistant && !string.Equals(line.Model, Synthetic, StringComparison.Ordinal);

    /// <summary>The line's dedupe key (4.1 step 2), null when blank.</summary>
    public static string? DedupeKey(in ClaudeLine line) => string.IsNullOrEmpty(line.Key) ? null : line.Key;

    /// <summary><c>attachment.timestamp</c> when it is a timestamp (METRICS 3.1).</summary>
    private static long? QueuedTime(JsonElement attachment)
    {
        var stamp = TaskInputs.Get(attachment, "timestamp");
        if (stamp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = System.Text.Encoding.UTF8.GetBytes(TaskJson.String(stamp) ?? string.Empty);
        return Rfc3339.TryParseUnixMs(text, out var ms) ? ms : null;
    }
}
