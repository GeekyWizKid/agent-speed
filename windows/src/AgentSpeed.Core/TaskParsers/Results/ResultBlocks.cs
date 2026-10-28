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
        string? text = null;
        if (content.ValueKind == JsonValueKind.String)
        {
            text = TaskJson.String(content);
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (BlockText(item) is { } block)
                {
                    text = block;
                    break;
                }
            }
        }

        if (text is null)
        {
            return "";
        }

        var cut = text.AsSpan().IndexOfAny('\r', '\n');
        return cut < 0 ? text : text[..cut];
    }

    private static string? BlockText(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && (TaskJson.GetString(item, "type") is "text" or "input_text" or "output_text")
            ? TaskJson.GetString(item, "text")
            : null;
}
