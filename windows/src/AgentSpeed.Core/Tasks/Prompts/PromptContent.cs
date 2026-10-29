using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// The content of a user message as the prompt rules read it (METRICS 6.9 <i>Prompt lines</i>): its texts, whether it
/// has media, and its lead text. Mirrors <c>_content_parts</c> and <c>_prompt_text</c> of the reference.
/// </summary>
internal readonly struct PromptContent
{
    private PromptContent(JsonElement content, List<string> texts)
    {
        Content = content;
        Texts = texts;
    }

    public JsonElement Content { get; }

    public List<string> Texts { get; }

    /// <summary>The first text, or empty.</summary>
    public string Lead => Texts.Count > 0 ? Texts[0] : string.Empty;

    /// <summary>
    /// The texts of a string content or of its blocks whose <c>type</c> is one of <paramref name="textTypes"/>; null when
    /// the content is neither a string nor an array, or has no non-blank text and no block of a media type.
    /// </summary>
    public static PromptContent? Read(JsonElement content, string[] textTypes, string[] mediaTypes)
    {
        var texts = new List<string>();
        var media = false;
        if (content.ValueKind == JsonValueKind.String)
        {
            texts.Add(TaskJson.String(content) ?? string.Empty);
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = TaskJson.Get(block, "type");
                if (IsOneOf(type, textTypes) && TaskJson.String(TaskJson.Get(block, "text")) is { } text)
                {
                    texts.Add(text);
                }

                media |= IsOneOf(type, mediaTypes);
            }
        }
        else
        {
            return null;
        }

        if (!media && texts.TrueForAll(PyText.IsBlank))
        {
            return null;
        }

        return new PromptContent(content, texts);
    }

    /// <summary>
    /// The prompt's text for the continuation test: the string content, else the texts that do not start with
    /// <c>&lt;</c> after leading whitespace, joined with LF.
    /// </summary>
    public string PromptText()
    {
        if (Content.ValueKind == JsonValueKind.String)
        {
            return Texts[0];
        }

        var kept = new List<string>(Texts.Count);
        foreach (var text in Texts)
        {
            if (!PyText.TrimStart(text).StartsWith("<", StringComparison.Ordinal))
            {
                kept.Add(text);
            }
        }

        return string.Join('\n', kept);
    }

    /// <summary>Every text joined with LF.</summary>
    public string AllText() => string.Join('\n', Texts);

    /// <summary><c>== literal</c> for a JSON string (an unpaired surrogate never equals a literal).</summary>
    public static bool Is(JsonElement value, string literal)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            return value.ValueEquals(literal);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool HasBlockOfType(JsonElement content, string type)
    {
        if (content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object && Is(TaskJson.Get(block, "type"), type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>_starts_with_tag</c>: the head starts with <c>&lt;tag</c> followed by its end, <c>&gt;</c>, <c>/</c> or whitespace.
    /// </summary>
    public static bool StartsWithTag(ReadOnlySpan<char> head, string[] tags)
    {
        foreach (var tag in tags)
        {
            if (head.Length > tag.Length && head[0] == '<' && head[1..].StartsWith(tag, StringComparison.Ordinal))
            {
                var next = tag.Length + 1;
                if (head.Length == next || head[next] is '>' or '/' || PyText.IsSpace(head[next]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsOneOf(JsonElement value, string[] literals)
    {
        foreach (var literal in literals)
        {
            if (Is(value, literal))
            {
                return true;
            }
        }

        return false;
    }
}
