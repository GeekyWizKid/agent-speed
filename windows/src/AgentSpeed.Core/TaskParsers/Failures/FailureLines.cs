using System.Buffers;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Cleaning of one output segment (TASK-PARSERS 5.1): escape sequences and control characters removed, LF-normalised,
/// split into right-trimmed lines of at most <see cref="MaxLine"/> characters, workspace prefixes removed.
/// </summary>
internal static partial class FailureLines
{
    public const int MaxLine = 2_000;
    public const int WorkspaceMin = 3;

    // CONTROL plus U+0085, U+2028 and U+2029; CR never reaches a line, it ends one.
    private static readonly SearchValues<char> Special = SearchValues.Create(
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000b\u000c\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017" +
        "\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\u007f\u0085\u2028\u2029");

    [GeneratedRegex("""\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)?""", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiOscShared();


    private static Regex AnsiOsc() => ThreadRegex.Of(AnsiOscShared());

    [GeneratedRegex("""\x1b\[[0-?]*[ -/]*[@-~]""", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiCsiShared();


    private static Regex AnsiCsi() => ThreadRegex.Of(AnsiCsiShared());

    [GeneratedRegex("""\x1b[@-Z\\-_]""", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiFeShared();


    private static Regex AnsiFe() => ThreadRegex.Of(AnsiFeShared());

    [GeneratedRegex("""\A(?:((?:@[A-Za-z0-9._-]+/|[A-Za-z0-9._-]*/)[A-Za-z0-9._/-]* [A-Za-z][A-Za-z0-9:._-]*|@?[A-Za-z0-9._/-]+:[A-Za-z][A-Za-z0-9._-]*): ?)""", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspacePrefixShared();


    private static Regex WorkspacePrefix() => ThreadRegex.Of(WorkspacePrefixShared());

    /// <summary>The cleaned lines of a segment.</summary>
    public static string[] Clean(string segment)
    {
        var text = segment;
        if (text.Contains('\u001b', StringComparison.Ordinal))
        {
            text = AnsiOsc().Replace(text, "");
            text = AnsiCsi().Replace(text, "");
            text = AnsiFe().Replace(text, "");
        }

        var lines = new List<string>();
        var start = 0;
        while (true)
        {
            var at = text.AsSpan(start).IndexOfAny('\r', '\n');
            var end = at < 0 ? text.Length : start + at;
            lines.Add(CleanLine(text.AsSpan(start, end - start)));
            if (at < 0)
            {
                break;
            }

            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }

        return StripWorkspacePrefixes(lines);
    }

    private static string CleanLine(ReadOnlySpan<char> raw)
    {
        if (raw.IndexOfAny(Special) < 0)
        {
            return Cut(raw.TrimEnd(" \t"));
        }

        var buffer = ArrayPool<char>.Shared.Rent(raw.Length);
        try
        {
            var n = 0;
            foreach (var c in raw)
            {
                if (c is '\u0085' or '\u2028' or '\u2029')
                {
                    buffer[n++] = ' ';
                }
                else if (!Special.Contains(c))
                {
                    buffer[n++] = c;
                }
            }

            return Cut(buffer.AsSpan(0, n).TrimEnd(" \t"));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static string Cut(ReadOnlySpan<char> line) =>
        line.Length <= MaxLine ? line.ToString() : TaskText.CutScalars(line.ToString(), MaxLine);

    /// <summary>
    /// Removes the <c>&lt;package&gt; &lt;script&gt;: </c> prefixes that pnpm, yarn and turbo put on interleaved lines
    /// when a prefix starts at least <see cref="WorkspaceMin"/> lines.
    /// </summary>
    private static string[] StripWorkspacePrefixes(List<string> lines)
    {
        int[]? prefix = null;
        Dictionary<string, int>? counts = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var length = PrefixLength(lines[i]);
            if (length < 0)
            {
                continue;
            }

            prefix ??= new int[lines.Count];
            prefix[i] = length + 1;
            counts ??= new Dictionary<string, int>(StringComparer.Ordinal);
            var lookup = counts.GetAlternateLookup<ReadOnlySpan<char>>();
            var key = lines[i].AsSpan(0, length);
            lookup[key] = lookup.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        if (prefix is null || counts is null || !counts.Values.Any(n => n >= WorkspaceMin))
        {
            return [.. lines];
        }

        var common = counts.GetAlternateLookup<ReadOnlySpan<char>>();
        var result = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var length = prefix[i] - 1;
            if (length >= 0 && common.TryGetValue(line.AsSpan(0, length), out var n) && n >= WorkspaceMin)
            {
                var rest = length + 1;
                result[i] = line[(rest < line.Length && line[rest] == ' ' ? rest + 1 : rest)..];
            }
            else
            {
                result[i] = line;
            }
        }

        return result;
    }

    /// <summary>The length of the line's workspace prefix (the first group, before its colon), or -1 when it has none.</summary>
    private static int PrefixLength(string line)
    {
        if (line.Length == 0)
        {
            return -1;
        }

        var match = WorkspacePrefix().Match(line);
        if (!match.Success)
        {
            return -1;
        }

        var length = match.Groups[1].Length;
        return length + 1 >= line.Length || line[length + 1] == ' ' ? length : -1;
    }
}
