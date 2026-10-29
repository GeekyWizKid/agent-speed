using System.Globalization;
using System.Text.Json;
using AgentSpeed.Core.History;

namespace AgentSpeed.Core.Tasks;

/// <summary>What the omp and Pi line structs share for the composer.</summary>
internal enum PiEntryRole : byte
{
    Other,
    User,
    Assistant,
    ToolResult,
}

/// <summary>One omp, Pi or OpenClaw entry as the composer sees it, from the source's line struct.</summary>
internal readonly record struct PiEntryFacts(
    bool Valid,
    bool IsSession,
    bool IsMessage,
    string? Id,
    long? TimestampMs,
    string? Cwd,
    PiEntryRole Role,
    bool Guarded,
    long? StartedMs,
    bool HasToolCalls);

/// <summary>
/// omp, Pi and OpenClaw entries (METRICS 6.9 <i>Composing the stream</i>): the first <c>session</c> header gives the cwd;
/// entries before it, without a string id or time, or older than the header by more than 2 s add nothing; prompts are
/// user messages, calls the <c>toolCall</c> blocks of an assistant message that passes 4.7 step 1, results the
/// <c>toolResult</c> messages.
/// </summary>
internal sealed class PiFamilyTaskReader(SourceKind source) : TaskLineReader(source)
{
    private const long CopiedHistoryMs = 2000;

    private readonly string _keyPrefix = SourceNames.Of(source) + ":";
    private bool _headerSeen;
    private long? _headerMs;

    public override void Reset()
    {
        base.Reset();
        _headerSeen = false;
        _headerMs = null;
    }

    /// <summary>Reads one entry; <paramref name="tracked"/> tells whether it is a line the composer counts.</summary>
    public TaskLine? Read(ReadOnlySpan<byte> raw, in PiEntryFacts entry, out bool tracked)
    {
        tracked = false;
        if (!entry.Valid)
        {
            return null;
        }

        if (entry.IsSession)
        {
            if (_headerSeen)
            {
                return null;
            }

            _headerSeen = true;
            _headerMs = entry.TimestampMs;
            if (NonBlank(entry.Cwd))
            {
                Cwd = entry.Cwd;
            }

            tracked = entry.TimestampMs is not null;
            return null;
        }

        if (!_headerSeen || entry.Id is null || entry.TimestampMs is not { } t
            || (_headerMs is { } header && t < header - CopiedHistoryMs))
        {
            return null;
        }

        tracked = true;
        if (!entry.IsMessage)
        {
            return null;
        }

        switch (entry.Role)
        {
            case PiEntryRole.User:
                return ReadUser(raw);
            case PiEntryRole.Assistant when entry.Guarded:
                var key = string.Create(CultureInfo.InvariantCulture, $"{_keyPrefix}{entry.Id}:{entry.StartedMs ?? t}");
                var task = new TaskLine { Assistant = true, DedupeKey = key };
                if (entry.HasToolCalls)
                {
                    ReadCalls(raw, t, task);
                }

                return task;
            case PiEntryRole.ToolResult:
                return ReadResult(raw, t);
            default:
                return null;
        }
    }

    private TaskLine? ReadUser(ReadOnlySpan<byte> raw)
    {
        using var json = TaskJsonLine.Parse(raw);
        return json.IsObject && PromptLines.Classify(Source, json.Root) is { } prompt ? new TaskLine { Prompt = prompt } : null;
    }

    private void ReadCalls(ReadOnlySpan<byte> raw, long t, TaskLine task)
    {
        using var json = TaskJsonLine.Parse(raw);
        var message = json.IsObject ? TaskInputs.Get(json.Root, "message") : default;
        foreach (var block in TaskInputs.Blocks(message, "content"))
        {
            if (!TaskInputs.IsType(block, "toolCall"))
            {
                continue;
            }

            var input = TaskInputs.Get(block, "arguments");
            if (Call(TaskInputs.GetString(block, "id"), TaskInputs.GetString(block, "name"), input, TaskInputs.Digest(input), t) is { } call)
            {
                (task.Calls ??= []).Add(call);
            }
        }
    }

    private TaskLine? ReadResult(ReadOnlySpan<byte> raw, long t)
    {
        using var json = TaskJsonLine.Parse(raw);
        var message = json.IsObject ? TaskInputs.Get(json.Root, "message") : default;
        if (message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Result(TaskInputs.GetString(message, "toolCallId"), message, t) is { } result ? new TaskLine { Results = [result] } : null;
    }
}
