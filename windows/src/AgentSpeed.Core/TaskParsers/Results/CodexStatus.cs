using System.Globalization;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>The state of a tool result (TASK-PARSERS 4.2).</summary>
internal enum ResultState
{
    Done,

    /// <summary>Backgrounded or still running.</summary>
    Running,

    /// <summary>Interrupted, cancelled or rejected by the user.</summary>
    Aborted,

    Timeout,
}

/// <summary>
/// The <b>Codex status</b> of a <c>…_output</c> payload or its <c>output</c> value (TASK-PARSERS 4.1): the body that
/// <c>failures</c> reads, the exit code, the state, the session a unified-exec command left running and the patch
/// verdict (true = failed, false = applied, null = none).
/// </summary>
/// <remarks>The body is a slice of the text (or the JSON output) taken only when <see cref="Body"/> is read.</remarks>
internal readonly record struct CodexStatus(string BodySource, int BodyStart, long? Exit, ResultState State, string? Session, bool? Patch)
{
    private const int HeaderLines = 8;

    /// <summary>The body that <c>failures</c> reads.</summary>
    public string Body => BodyStart == 0 ? BodySource : BodyStart >= BodySource.Length ? "" : BodySource[BodyStart..];

    public static CodexStatus Of(JsonElement result)
    {
        var value = OutputValue(result, out var decoded);
        var text = TaskText.Lf(decoded ?? ResultBlocks.Text(value));
        var body = text;
        var bodyStart = 0;
        long? exit = null;
        var state = ResultState.Done;
        string? session = null;

        if (TaskJson.LeadingObject(text) is { } head && TaskJson.GetString(head, "output") is { } output)
        {
            body = TaskText.Lf(output);
            if (TaskJson.IsInt(TaskJson.Get(head, "exit_code"), out var code)
                || TaskJson.IsInt(TaskJson.Get(TaskJson.Get(head, "metadata"), "exit_code"), out code))
            {
                exit = code;
            }
            else if (TaskJson.IsInt(TaskJson.Get(head, "session_id"), out var id))
            {
                state = ResultState.Running;
                session = "codex:" + id.ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            ReadHeader(text, ref bodyStart, ref exit, ref state, ref session);
        }

        var bodySpan = body.AsSpan(Math.Min(bodyStart, body.Length));
        var bodyCut = bodySpan.IndexOf('\n');
        var bodyFirst = bodyCut < 0 ? bodySpan : bodySpan[..bodyCut];
        var textCut = text.IndexOf('\n', StringComparison.Ordinal);
        var first = (textCut < 0 ? text.AsSpan() : text.AsSpan(0, textCut)).Trim(" \t");
        if (first.SequenceEqual("aborted") || first.StartsWith("aborted by user", StringComparison.Ordinal)
            || first.Contains("rejected by user", StringComparison.Ordinal))
        {
            state = ResultState.Aborted;
        }
        else if (ResultPatterns.CodexTimeout().IsMatch(bodyFirst))
        {
            state = ResultState.Timeout;
        }

        bool? patch = null;
        if (exit is null && state == ResultState.Done)
        {
            if (bodyFirst.StartsWith("Success. Updated the following files", StringComparison.Ordinal))
            {
                patch = false;
            }
            else if (bodyFirst.StartsWith("apply_patch verification failed", StringComparison.Ordinal)
                     || bodyFirst.StartsWith("Failed to apply patch", StringComparison.Ordinal))
            {
                patch = true;
            }
        }

        return new CodexStatus(body, bodyStart, exit, state, session, patch);
    }

    /// <summary>
    /// The <i>output value</i> (4.1 step 1): the result's <c>output</c> when the result is an object with both
    /// <c>type</c> and <c>output</c>, else the result; an object's <c>content</c> array or string; a string holding a
    /// non-empty JSON array of objects with a string <c>type</c> read as that array.
    /// </summary>
    public static JsonElement OutputValue(JsonElement result) => OutputValue(result, out _);

    /// <summary>
    /// The output value, and the string it holds when it is a string kept as is (decoded once: its blocks text), else
    /// null.
    /// </summary>
    private static JsonElement OutputValue(JsonElement result, out string? decoded)
    {
        decoded = null;
        var output = result.ValueKind == JsonValueKind.Object && TaskJson.Has(result, "type") && TaskJson.Has(result, "output")
            ? TaskJson.Get(result, "output")
            : result;
        if (output.ValueKind == JsonValueKind.Object
            && TaskJson.Get(output, "content") is { ValueKind: JsonValueKind.Array or JsonValueKind.String } content)
        {
            output = content;
        }

        if (output.ValueKind == JsonValueKind.String && TaskJson.String(output) is { } text)
        {
            if (TaskJson.FirstNonWs(text) == '['
                && TaskJson.Parse(text) is { ValueKind: JsonValueKind.Array } array
                && IsTypedItems(array))
            {
                return array;
            }

            decoded = text;
        }

        return output;
    }

    private static bool IsTypedItems(JsonElement array)
    {
        if (array.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || TaskJson.GetString(item, "type") is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>4.1 step 4: a header of known lines ended by <c>Output:</c> within the first 8 lines.</summary>
    private static void ReadHeader(string text, ref int body, ref long? exit, ref ResultState state, ref string? session)
    {
        Span<int> starts = stackalloc int[HeaderLines];
        Span<int> ends = stackalloc int[HeaderLines];
        var start = 0;
        var output = -1;
        var bodyStart = -1;
        for (var line = 0; line < HeaderLines; line++)
        {
            var lf = text.IndexOf('\n', start);
            var end = lf < 0 ? text.Length : lf;
            if (text.AsSpan(start, end - start).SequenceEqual("Output:"))
            {
                output = line;
                bodyStart = lf < 0 ? text.Length : lf + 1;
                break;
            }

            starts[line] = start;
            ends[line] = end;
            if (lf < 0)
            {
                break;
            }

            start = lf + 1;
        }

        if (output < 0)
        {
            return;
        }

        for (var k = 0; k < output; k++)
        {
            if (!ResultPatterns.CodexHeader().IsMatch(text.AsSpan(starts[k], ends[k] - starts[k])))
            {
                return;
            }
        }

        body = bodyStart;
        for (var k = 0; k < output; k++)
        {
            var line = text[starts[k]..ends[k]];
            var m = ResultPatterns.CodexExit().Match(line);
            if (!m.Success)
            {
                m = ResultPatterns.CodexExited().Match(line);
            }

            if (m.Success)
            {
                exit = TaskText.Num(m.Groups[1].ValueSpan);
            }

            m = ResultPatterns.CodexRunning().Match(line);
            if (m.Success)
            {
                state = ResultState.Running;
                session = "codex:" + m.Groups[1].Value;
            }
        }
    }
}
