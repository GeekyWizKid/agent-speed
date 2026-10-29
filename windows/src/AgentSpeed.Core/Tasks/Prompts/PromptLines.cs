using System.Text;
using System.Text.Json;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>The prompt kind of a line (METRICS 6.9 <i>Prompt lines</i>).</summary>
public enum PromptLineKind
{
    /// <summary>A user request; it starts a task unless it is a continuation.</summary>
    Prompt,

    /// <summary>A Claude slash command or skill the user typed; never a continuation.</summary>
    Command,

    /// <summary>A Claude local command's output: it withdraws the command before it (<see cref="Segmentation"/>).</summary>
    CommandOutput,
}

/// <summary>
/// One classified line. <see cref="T"/> is set for a queued Claude message whose own timestamp parses (METRICS 3.1): the
/// prompt takes that time instead of the line's.
/// </summary>
public readonly record struct PromptLine(PromptLineKind Kind, bool Continuation, long? T);

/// <summary>
/// <c>prompt_line</c> (METRICS 6.9 <i>Prompt lines</i>): the prompt kind of one line that passed its source's
/// copied-history rules, for the scored sources. Prompt texts are compared with the continuation list in memory and
/// never kept.
/// </summary>
public static class PromptLines
{
    private static readonly string[] ClaudeTextTypes = ["text"];
    private static readonly string[] ClaudeMediaTypes = ["image", "document"];
    private static readonly string[] CodexTextTypes = ["input_text", "text"];
    private static readonly string[] CodexMediaTypes = ["input_image"];
    private static readonly string[] PiMediaTypes = ["image"];
    private static readonly string[] ClaudeOutputTags = ["local-command-stdout", "local-command-stderr"];

    private static readonly string[] ClaudeIgnoredTags =
        ["local-command-caveat", "bash-input", "bash-stdout", "bash-stderr", "user-memory-input", "system-reminder", "task-notification"];

    private const string CodexAgents = "# AGENTS.md instructions";

    /// <summary>The line's prompt kind, or null (anything else, or a source without tasks).</summary>
    public static PromptLine? Classify(SourceKind source, JsonElement line)
    {
        if (line.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return source switch
        {
            SourceKind.ClaudeCode or SourceKind.ClaudeDesktop => Claude(line),
            SourceKind.Codex => Codex(line),
            SourceKind.Omp or SourceKind.Pi or SourceKind.OpenClaw => PiFamily(line),
            SourceKind.Droid => Droid(line),
            _ => null,
        };
    }

    private static PromptLine Prompt(bool continuation, long? t = null) => new(PromptLineKind.Prompt, continuation, t);

    private static PromptLine? Claude(JsonElement line)
    {
        var type = TaskJson.Get(line, "type");
        if (PromptContent.Is(type, "attachment"))
        {
            return ClaudeQueued(line);
        }

        if (!PromptContent.Is(type, "user") || TaskJson.IsTrue(TaskJson.Get(line, "isMeta"))
            || TaskJson.IsTrue(TaskJson.Get(line, "isCompactSummary")) || TaskJson.IsTrue(TaskJson.Get(line, "isVisibleInTranscriptOnly")))
        {
            return null;
        }

        var content = TaskJson.Get(TaskJson.Get(line, "message"), "content");
        if (PromptContent.HasBlockOfType(content, "tool_result")
            || PromptContent.Read(content, ClaudeTextTypes, ClaudeMediaTypes) is not { } parts)
        {
            return null;
        }

        var head = PyText.TrimStart(parts.Lead);
        if (head.StartsWith("[Request interrupted by user", StringComparison.Ordinal))
        {
            return null;
        }

        if (PromptContent.StartsWithTag(head, ClaudeOutputTags))
        {
            return new PromptLine(PromptLineKind.CommandOutput, false, null);
        }

        if (PromptContent.StartsWithTag(head, ClaudeIgnoredTags))
        {
            return null;
        }

        if (head.StartsWith("<command-name>", StringComparison.Ordinal) || head.StartsWith("<command-message>", StringComparison.Ordinal))
        {
            return new PromptLine(PromptLineKind.Command, false, null);
        }

        return Prompt(Continuations.IsContinuation(parts.PromptText()));
    }

    /// <summary><c>claude_queued_prompt</c>: a <c>queued_command</c> attachment in prompt mode, from a human.</summary>
    private static PromptLine? ClaudeQueued(JsonElement line)
    {
        var attachment = TaskJson.Get(line, "attachment");
        if (attachment.ValueKind != JsonValueKind.Object || !PromptContent.Is(TaskJson.Get(attachment, "type"), "queued_command")
            || !PromptContent.Is(TaskJson.Get(attachment, "commandMode"), "prompt") || !TaskJson.IsTrue(TaskJson.Get(attachment, "humanTurn"))
            || TaskJson.IsTrue(TaskJson.Get(line, "isMeta")) || TaskJson.IsTrue(TaskJson.Get(attachment, "isMeta")))
        {
            return null;
        }

        if (PromptContent.Read(TaskJson.Get(attachment, "prompt"), ClaudeTextTypes, ClaudeMediaTypes) is not { } parts)
        {
            return null;
        }

        return Prompt(Continuations.IsContinuation(parts.PromptText()), Timestamp(TaskJson.Get(attachment, "timestamp")));
    }

    /// <summary><c>codex_prompt</c>: a <c>response_item</c> user message the user wrote, not injected context.</summary>
    private static PromptLine? Codex(JsonElement line)
    {
        var payload = TaskJson.Get(line, "payload");
        if (!PromptContent.Is(TaskJson.Get(line, "type"), "response_item") || payload.ValueKind != JsonValueKind.Object
            || !PromptContent.Is(TaskJson.Get(payload, "type"), "message") || !PromptContent.Is(TaskJson.Get(payload, "role"), "user"))
        {
            return null;
        }

        if (PromptContent.Read(TaskJson.Get(payload, "content"), CodexTextTypes, CodexMediaTypes) is not { } parts)
        {
            return null;
        }

        var lead = PyText.TrimStart(parts.Lead);
        if ((lead.Length > 1 && lead[0] == '<' && char.IsAsciiLetter(lead[1])) || lead.StartsWith(CodexAgents, StringComparison.Ordinal))
        {
            return null;
        }

        return Prompt(Continuations.IsContinuation(parts.PromptText()));
    }

    /// <summary><c>pi_family_prompt</c>: a <c>message</c> entry of role <c>user</c> (omp, Pi, OpenClaw).</summary>
    private static PromptLine? PiFamily(JsonElement line)
    {
        var message = TaskJson.Get(line, "message");
        if (!PromptContent.Is(TaskJson.Get(line, "type"), "message") || message.ValueKind != JsonValueKind.Object
            || !PromptContent.Is(TaskJson.Get(message, "role"), "user"))
        {
            return null;
        }

        return PromptContent.Read(TaskJson.Get(message, "content"), ClaudeTextTypes, PiMediaTypes) is { } parts
            ? Prompt(Continuations.IsContinuation(parts.AllText()))
            : null;
    }

    /// <summary><c>droid_prompt</c>: a user message without a non-null <c>visibility</c> that holds no tool result.</summary>
    private static PromptLine? Droid(JsonElement line)
    {
        var message = TaskJson.Get(line, "message");
        if (!PromptContent.Is(TaskJson.Get(line, "type"), "message") || message.ValueKind != JsonValueKind.Object
            || !PromptContent.Is(TaskJson.Get(message, "role"), "user")
            || (message.TryGetProperty("visibility", out var visibility) && visibility.ValueKind != JsonValueKind.Null))
        {
            return null;
        }

        var content = TaskJson.Get(message, "content");
        if (PromptContent.HasBlockOfType(content, "tool_result") || PromptContent.Read(content, ClaudeTextTypes, PiMediaTypes) is not { } parts)
        {
            return null;
        }

        return Prompt(Continuations.IsContinuation(parts.PromptText()));
    }

    /// <summary>A timestamp string of METRICS 3.1, or null.</summary>
    private static long? Timestamp(JsonElement value)
    {
        if (TaskJson.String(value) is not { } text || text.Length > Rfc3339.MaxLength)
        {
            return null;
        }

        Span<byte> utf8 = stackalloc byte[Rfc3339.MaxLength * 3];
        var length = Encoding.UTF8.GetBytes(text, utf8);
        return Rfc3339.TryParseUnixMs(utf8[..length], out var ms) ? ms : null;
    }
}
