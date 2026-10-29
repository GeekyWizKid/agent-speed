namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Where a regular expression literal that starts at a <c>/</c> ends (TASK-PARSERS 4.4): at an unescaped <c>/</c>
/// outside <c>[…]</c>, then letters, digits, <c>_</c> and <c>$</c>; a line break first makes it no literal. A scan
/// that fails runs on to the line's end, where a later <c>/</c> would scan the same characters again, so every
/// (position, inside a class) state a failed scan passed is remembered: the scans of one script stay linear together.
/// </summary>
internal sealed class JsRegexScanner(string text)
{
    private const byte FailsOutside = 1;
    private const byte FailsInside = 2;

    private byte[]? fails;

    /// <summary>The index after the literal starting at <paramref name="slash"/>, or -1.</summary>
    public int End(int slash)
    {
        var end = Scan(slash, mark: false);
        if (end < 0)
        {
            Scan(slash, mark: true);
        }

        return end;
    }

    private int Scan(int slash, bool mark)
    {
        var n = text.Length;
        var inClass = false;
        var j = slash + 1;
        while (j < n)
        {
            var state = inClass ? FailsInside : FailsOutside;
            if (fails is not null && (fails[j] & state) != 0)
            {
                return -1;
            }

            if (mark)
            {
                (fails ??= new byte[n])[j] |= state;
            }

            var c = text[j];
            if (c is '\n' or '\r' or '\u2028' or '\u2029')
            {
                return -1;
            }

            if (c == '\\')
            {
                j += 2;
                continue;
            }

            if (c == '[')
            {
                inClass = true;
            }
            else if (c == ']')
            {
                inClass = false;
            }
            else if (c == '/' && !inClass)
            {
                j++;
                while (j < n && JsLexer.IsIdent(text[j]))
                {
                    j++;
                }

                return j;
            }

            j++;
        }

        return -1;
    }
}
