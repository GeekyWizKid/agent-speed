namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The omp, Pi and OpenClaw lines that <c>outcome</c> reads (TASK-PARSERS 4.2): the first, last and second-to-last
/// lines of a text that are not empty after trimming, trimmed, found without splitting the whole text.
/// </summary>
internal readonly struct PiResultLines
{
    private readonly int firstStart, firstEnd, lastStart, lastEnd, secondStart, secondEnd;

    private PiResultLines(int count, int firstStart, int firstEnd, int lastStart, int lastEnd, int secondStart, int secondEnd)
    {
        Count = count;
        this.firstStart = firstStart;
        this.firstEnd = firstEnd;
        this.lastStart = lastStart;
        this.lastEnd = lastEnd;
        this.secondStart = secondStart;
        this.secondEnd = secondEnd;
    }

    /// <summary>0, 1, or 2 when there are at least two such lines.</summary>
    public int Count { get; }

    public static PiResultLines Of(string text)
    {
        var found = 0;
        int firstStart = 0, firstEnd = 0, lastStart = 0, lastEnd = 0, secondStart = 0, secondEnd = 0;
        var end = text.Length;
        while (true)
        {
            var lineStart = ResultParser.LineStart(text, end);
            var start = lineStart;
            if (Trimmed(text, ref start, end, out var trimmedEnd))
            {
                if (found == 0)
                {
                    (lastStart, lastEnd) = (start, trimmedEnd);
                }
                else
                {
                    (secondStart, secondEnd) = (start, trimmedEnd);
                }

                if (++found == 2)
                {
                    break;
                }
            }

            if (lineStart == 0)
            {
                break;
            }

            end = lineStart - 1;
        }

        if (found == 0)
        {
            return default;
        }

        var next = 0;
        while (true)
        {
            var lf = text.IndexOf('\n', next);
            var lineEnd = lf < 0 ? text.Length : lf;
            var start = next;
            if (Trimmed(text, ref start, lineEnd, out var trimmedEnd))
            {
                (firstStart, firstEnd) = (start, trimmedEnd);
                break;
            }

            if (lf < 0)
            {
                break;
            }

            next = lf + 1;
        }

        return new PiResultLines(found, firstStart, firstEnd, lastStart, lastEnd, secondStart, secondEnd);
    }

    public ReadOnlySpan<char> FirstSpan(string text) => text.AsSpan(firstStart, firstEnd - firstStart);

    public ReadOnlySpan<char> LastSpan(string text) => text.AsSpan(lastStart, lastEnd - lastStart);

    public ReadOnlySpan<char> SecondLastSpan(string text) => text.AsSpan(secondStart, secondEnd - secondStart);

    public string Last(string text) => text[lastStart..lastEnd];

    private static bool Trimmed(string text, ref int start, int end, out int trimmedEnd)
    {
        while (start < end && TaskText.IsWs(text[start]))
        {
            start++;
        }

        trimmedEnd = end;
        while (trimmedEnd > start && TaskText.IsWs(text[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        return trimmedEnd > start;
    }
}
