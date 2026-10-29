using System.Text;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The output budget of <c>failures</c> (TASK-PARSERS 5.1): the whole text when its UTF-8 form fits in
/// <see cref="HeadBytes"/> + <see cref="TailBytes"/>, else a head and a tail cut on character and line boundaries.
/// </summary>
internal static class FailureBudget
{
    public const int HeadBytes = 65_536;
    public const int TailBytes = 196_608;

    /// <summary>The segments of a text whose unpaired surrogates were already replaced (<see cref="TaskText.Scalars"/>).</summary>
    public static string[] Segments(string text)
    {
        if (text.Length <= (HeadBytes + TailBytes) / 3 || Encoding.UTF8.GetByteCount(text) <= HeadBytes + TailBytes)
        {
            return [text];
        }

        // The longest run of whole characters within the first HeadBytes bytes, then up to its last LF.
        var bytes = 0;
        var headEnd = 0;
        while (headEnd < text.Length)
        {
            var units = Units(text, headEnd);
            var width = Utf8Width(text[headEnd], units);
            if (bytes + width > HeadBytes)
            {
                break;
            }

            bytes += width;
            headEnd += units;
        }

        var lf = headEnd == 0 ? -1 : text.LastIndexOf('\n', headEnd - 1, headEnd);
        var head = text[..(lf >= 0 ? lf + 1 : headEnd)];

        // The characters that start within the last TailBytes bytes, from after their first LF.
        bytes = 0;
        var tailStart = text.Length;
        while (tailStart > 0)
        {
            var units = tailStart >= 2 && char.IsLowSurrogate(text[tailStart - 1]) && char.IsHighSurrogate(text[tailStart - 2]) ? 2 : 1;
            var width = Utf8Width(text[tailStart - units], units);
            if (bytes + width > TailBytes)
            {
                break;
            }

            bytes += width;
            tailStart -= units;
        }

        lf = text.IndexOf('\n', tailStart);
        var tail = text[(lf >= 0 ? lf + 1 : tailStart)..];
        return [head, tail];
    }

    private static int Units(string text, int i) =>
        char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;

    private static int Utf8Width(char first, int units) => units == 2 ? 4 : first < 0x80 ? 1 : first < 0x800 ? 2 : 3;
}
