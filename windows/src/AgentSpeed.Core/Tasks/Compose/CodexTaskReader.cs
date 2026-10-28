using System.Text.Json;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.TaskParsers;
using AgentSpeed.Core.Work;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// Codex rollout lines (METRICS 6.9 <i>Composing the stream</i>): the first <c>session_meta</c> sets the cwd and the
/// copied-history bounds of 3.5 (copied lines add nothing), <c>turn_context</c> updates the cwd, prompts are
/// <c>response_item</c> user messages, calls are <c>function_call</c> / <c>custom_tool_call</c> items (a code-mode script
/// gives its tool calls instead) and results their <c>…_output</c> items.
/// </summary>
internal sealed class CodexTaskReader() : TaskLineReader(SourceKind.Codex)
{
    private const long CopiedHistoryToleranceMs = 2000;

    private bool _headerSeen;
    private long? _createdMs;
    private long? _startOrdinal;

    public override void Reset()
    {
        base.Reset();
        _headerSeen = false;
        _createdMs = null;
        _startOrdinal = null;
    }

    public void Read(ReadOnlySpan<byte> raw, ref CodexLine line)
    {
        if (line.Status != LineStatus.Valid || !line.HasTimestamp)
        {
            return;
        }

        var t = line.TimestampMs;
        if (line.Kind == CodexLineKind.SessionMeta)
        {
            if (_headerSeen)
            {
                return;
            }

            _headerSeen = true;
            _createdMs = t;
            _startOrdinal = line.SubagentStartOrdinal;
            line.TaskTracked = true;
            if (NonBlank(line.Cwd))
            {
                Cwd = line.Cwd;
            }

            return;
        }

        if ((_createdMs is { } created && t < created - CopiedHistoryToleranceMs)
            || (_startOrdinal is { } start && line.Ordinal is { } ordinal && ordinal < start))
        {
            return;
        }

        line.TaskTracked = true;
        if (line.Kind == CodexLineKind.TurnContext)
        {
            if (NonBlank(line.Cwd))
            {
                Cwd = line.Cwd;
            }
        }
        else if (line.Kind == CodexLineKind.ResponseItem
                 && ((line.PayloadKind == CodexPayloadKind.Message && line.Role == CodexRole.User) || CodexTools.MayHaveTools(raw)))
        {
            line.Task = ReadItem(raw, t);
        }
    }

    private TaskLine? ReadItem(ReadOnlySpan<byte> raw, long t)
    {
        using var json = TaskJsonLine.Parse(raw);
        if (!json.IsObject)
        {
            return null;
        }

        if (PromptLines.Classify(Source, json.Root) is { } prompt)
        {
            return new TaskLine { Prompt = prompt };
        }

        var payload = TaskInputs.Get(json.Root, "payload");
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = TaskInputs.GetString(payload, "call_id");
        switch (TaskInputs.GetString(payload, "type"))
        {
            case "function_call":
            {
                var input = TaskInputs.Get(payload, "arguments");
                return Line(Call(id, TaskInputs.GetString(payload, "name"), input, TaskInputs.Digest(input), t));
            }

            case "custom_tool_call":
            {
                var input = TaskInputs.Get(payload, "input");
                var name = TaskInputs.GetString(payload, "name");
                var call = TaskParser.CodeScript(Source, name, input) is { } script
                    ? Script(id, script, t)
                    : Call(id, name, input, TaskInputs.Digest(input), t);
                return Line(call);
            }

            case "function_call_output" or "custom_tool_call_output":
                return Result(id, payload, t) is { } result ? new TaskLine { Results = [result] } : null;
            default:
                return null;
        }
    }

    private static TaskLine? Line(TaskCall? call) => call is null ? null : new TaskLine { Calls = [call] };
}
