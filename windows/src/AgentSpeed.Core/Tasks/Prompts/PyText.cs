namespace AgentSpeed.Core.Tasks;

/// <summary>
/// Whitespace as the reference's <c>str.isspace</c>, <c>strip</c> and <c>split</c> read it: <see cref="char.IsWhiteSpace(char)"/>
/// plus the information separators U+001C–U+001F.
/// </summary>
internal static class PyText
{
    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';

    public static bool IsBlank(string text)
    {
        foreach (var c in text)
        {
            if (!IsSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    public static ReadOnlySpan<char> TrimStart(ReadOnlySpan<char> text)
    {
        var start = 0;
        while (start < text.Length && IsSpace(text[start]))
        {
            start++;
        }

        return text[start..];
    }

    public static ReadOnlySpan<char> Trim(ReadOnlySpan<char> text)
    {
        text = TrimStart(text);
        var end = text.Length;
        while (end > 0 && IsSpace(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }
}
