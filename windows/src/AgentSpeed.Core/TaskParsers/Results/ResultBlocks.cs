using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// <i>Blocks text</i> of a content value (TASK-PARSERS 4.1): a string as is; an array gives the <c>text</c> of its items
/// of type <c>text</c>, <c>input_text</c> or <c>output_text</c> that have a string <c>text</c>, joined by LF; anything
/// else the empty text.
/// </summary>
internal static class ResultBlocks
{
    public static string Text(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return TaskJson.String(content) ?? "";
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        string? single = null;
        StringBuilder? builder = null;
        foreach (var item in content.EnumerateArray())
        {
            if (BlockText(item) is not { } text)
            {
                continue;
            }

            if (single is null)
            {
                single = text;
                continue;
            }

            builder ??= new StringBuilder(single.Length + text.Length + 1).Append(single);
            builder.Append('\n').Append(text);
        }

        return builder?.ToString() ?? single ?? "";
    }

    /// <summary>
    /// The first line of the LF-normalised blocks text, without building the rest: the joining LF ends the first block,
    /// and any CR or LF inside it ends the line.
    /// </summary>
    public static string FirstLine(JsonElement content)
    {
        var value = default(JsonElement);
        if (content.ValueKind == JsonValueKind.String)
        {
            value = content;
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (BlockTextValue(item) is { } block)
                {
                    value = block;
                    break;
                }
            }
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        if (FirstLineOfRaw(value) is { } line)
        {
            return line;
        }

        var text = TaskJson.String(value) ?? "";
        var cut = text.AsSpan().IndexOfAny('\r', '\n');
        return cut < 0 ? text : text[..cut];
    }

    /// <summary>
    /// The first line of a string value decoded from its raw text up to the first escaped CR or LF (JSON strings hold no
    /// raw line breaks), so a long text is never built; null (decode it whole) for an escape it does not handle simply.
    /// </summary>
    private static string? FirstLineOfRaw(JsonElement value)
    {
        var raw = JsonMarshal.GetRawUtf8Value(value);
        if (raw.Length < 2 || raw[0] != (byte)'"')
        {
            return null;
        }

        var body = raw[1..^1];
        var end = body.Length;
        var escaped = false;
        var i = body.IndexOf((byte)'\\');
        while (i >= 0)
        {
            if (i + 1 >= body.Length)
            {
                return null;
            }

            var e = body[i + 1];
            if (e is (byte)'n' or (byte)'r')
            {
                end = i;
                break;
            }

            if (e == (byte)'u')
            {
                // \u escapes (line breaks, surrogates): the whole decode keeps its exact rules.
                return null;
            }

            escaped = true;
            var next = body[(i + 2)..].IndexOf((byte)'\\');
            i = next < 0 ? -1 : i + 2 + next;
        }

        var prefix = body[..end];
        if (!escaped)
        {
            return System.Text.Unicode.Utf8.IsValid(prefix) ? Encoding.UTF8.GetString(prefix) : null;
        }

        var buffer = new byte[prefix.Length + 2];
        buffer[0] = buffer[^1] = (byte)'"';
        prefix.CopyTo(buffer.AsSpan(1));
        var reader = new Utf8JsonReader(buffer);
        return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
    }

    private static string? BlockText(JsonElement item) => BlockTextValue(item) is { } value ? TaskJson.String(value) : null;

    /// <summary>The <c>text</c> value of a text item, when it is a string.</summary>
    private static JsonElement? BlockTextValue(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && (TaskJson.GetString(item, "type") is "text" or "input_text" or "output_text")
            && TaskJson.Get(item, "text") is { ValueKind: JsonValueKind.String } text
            ? text
            : null;
}
