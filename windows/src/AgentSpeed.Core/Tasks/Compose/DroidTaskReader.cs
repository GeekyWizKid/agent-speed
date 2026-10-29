using System.Text.Json;
using AgentSpeed.Core.Droid;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// Factory Droid lines (METRICS 6.9 <i>Composing the stream</i>): the first <c>session_start</c> gives the cwd; lines
/// without a time add nothing; prompts are user messages, calls the <c>tool_use</c> blocks of an assistant message line
/// with a string <c>id</c> (key <c>droid:&lt;id&gt;</c> for duplicates), results a user line's <c>tool_result</c> blocks.
/// </summary>
internal sealed class DroidTaskReader() : TaskLineReader(SourceKind.Droid)
{
    private bool _headerSeen;

    public override void Reset()
    {
        base.Reset();
        _headerSeen = false;
    }

    public void Read(ReadOnlySpan<byte> raw, ref DroidLine line)
    {
        if (line.Status != LineStatus.Valid)
        {
            return;
        }

        if (line.Kind == DroidLineKind.SessionStart)
        {
            if (_headerSeen)
            {
                return;
            }

            _headerSeen = true;
            if (NonBlank(line.Cwd))
            {
                Cwd = line.Cwd;
            }

            line.TaskTracked = line.TimestampMs is not null;
            return;
        }

        if (line.TimestampMs is not { } t)
        {
            return;
        }

        line.TaskTracked = true;
        if (line.Kind != DroidLineKind.Message)
        {
            return;
        }

        if (line.Role == DroidRole.User)
        {
            line.Task = ReadUser(raw, t);
        }
        else if (line.Role == DroidRole.Assistant && !string.IsNullOrEmpty(line.Id))
        {
            var task = new TaskLine { Assistant = true, DedupeKey = "droid:" + line.Id };
            if (line.HasToolUse)
            {
                ReadCalls(raw, t, task);
            }

            line.Task = task;
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
        foreach (var block in TaskInputs.Blocks(TaskInputs.Get(json.Root, "message"), "content"))
        {
            if (TaskInputs.IsType(block, "tool_result") && Result(TaskInputs.GetString(block, "tool_use_id"), block, t) is { } result)
            {
                (task.Results ??= []).Add(result);
            }
        }

        return task.IsEmpty ? null : task;
    }

    private void ReadCalls(ReadOnlySpan<byte> raw, long t, TaskLine task)
    {
        using var json = TaskJsonLine.Parse(raw);
        var message = json.IsObject ? TaskInputs.Get(json.Root, "message") : default;
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
    }
}
