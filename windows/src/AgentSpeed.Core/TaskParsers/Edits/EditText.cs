using System.Buffers;
using System.Text;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The normalisation of TASK-PARSERS 6.1: file digests, <i>text lines</i>, hunks without their common context lines,
/// whole-file writes and deletions. Lines are slices of their text; digests are taken over the UTF-8 of the lines
/// joined by LF without building the joined string.
/// </summary>
internal static class EditText
{
    private const int StackLimit = 1024;

    /// <summary>The digest of a path: <c>\</c> → <c>/</c>, no <c>file://</c>, cwd-relative, <c>//</c>, <c>./</c> and <c>/./</c> folded.</summary>
    public static string File(string path, string? cwd) => TaskText.Sha16(NormPath(path, cwd));

    public static string NormPath(string path, string? cwd)
    {
        var p = path.Replace('\\', '/');
        if (p.StartsWith("file://", StringComparison.Ordinal))
        {
            p = p["file://".Length..];
        }

        p = TaskText.CwdRelative(p, cwd, out _);
        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        while (p.Contains("/./", StringComparison.Ordinal))
        {
            p = p.Replace("/./", "/", StringComparison.Ordinal);
        }

        return p;
    }

    /// <summary>The lines of an LF-normalised text, split at LF (empty pieces kept).</summary>
    public static List<ReadOnlyMemory<char>> Split(string text)
    {
        var lines = new List<ReadOnlyMemory<char>>();
        var memory = text.AsMemory();
        var start = 0;
        while (true)
        {
            var lf = text.IndexOf('\n', start);
            if (lf < 0)
            {
                lines.Add(memory[start..]);
                return lines;
            }

            lines.Add(memory[start..lf]);
            start = lf + 1;
        }
    }

    /// <summary><i>Text lines</i> of a string: LF-normalised, split at LF, right-trimmed, empty lines removed from both ends.</summary>
    public static List<ReadOnlyMemory<char>> TextLines(string text) => Normalize(Split(TaskText.Lf(text)));

    /// <summary>
    /// <i>Text lines</i> of lines that hold no line break (the reference joins them with LF and reads the text lines of
    /// that): each right-trimmed, empty lines removed from both ends.
    /// </summary>
    public static List<ReadOnlyMemory<char>> Normalize(IReadOnlyList<ReadOnlyMemory<char>> lines)
    {
        var result = new List<ReadOnlyMemory<char>>(lines.Count);
        foreach (var line in lines)
        {
            result.Add(line[..line.Span.TrimEnd(" \t").Length]);
        }

        var last = result.Count;
        while (last > 0 && result[last - 1].IsEmpty)
        {
            last--;
        }

        result.RemoveRange(last, result.Count - last);
        var first = 0;
        while (first < result.Count && result[first].IsEmpty)
        {
            first++;
        }

        result.RemoveRange(0, first);
        return result;
    }

    /// <summary>The hunk of an old and a new text in <paramref name="path"/>.</summary>
    public static EditHunk Hunk(string path, string old, string @new, string? cwd) =>
        Hunk(File(path, cwd), TextLines(old), TextLines(@new));

    /// <summary>The hunk of old and new lines (without line breaks) in the file of digest <paramref name="file"/>.</summary>
    public static EditHunk HunkLines(string file, IReadOnlyList<ReadOnlyMemory<char>> old, IReadOnlyList<ReadOnlyMemory<char>> @new) =>
        Hunk(file, Normalize(old), Normalize(@new));

    /// <summary>A whole-file write: no old side, the new side the content's text lines.</summary>
    public static EditHunk Whole(string path, string content, string? cwd)
    {
        var lines = TextLines(content);
        return new EditHunk(File(path, cwd), null, Digest(lines, 0, lines.Count));
    }

    /// <summary>A whole-file write of lines that hold no line break.</summary>
    public static EditHunk WholeLines(string file, IReadOnlyList<ReadOnlyMemory<char>> lines)
    {
        var normalized = Normalize(lines);
        return new EditHunk(file, null, Digest(normalized, 0, normalized.Count));
    }

    /// <summary>A deletion: no old side, the new side an empty file.</summary>
    public static EditHunk Deleted(string path, string? cwd) => new(File(path, cwd), null, TaskText.EmptyDigest);

    private static EditHunk Hunk(string file, List<ReadOnlyMemory<char>> old, List<ReadOnlyMemory<char>> @new)
    {
        int a0 = 0, a1 = old.Count, b0 = 0, b1 = @new.Count;
        while (a0 < a1 && b0 < b1 && old[a0].Span.SequenceEqual(@new[b0].Span))
        {
            a0++;
            b0++;
        }

        while (a0 < a1 && b0 < b1 && old[a1 - 1].Span.SequenceEqual(@new[b1 - 1].Span))
        {
            a1--;
            b1--;
        }

        return new EditHunk(file, Digest(old, a0, a1), Digest(@new, b0, b1));
    }

    /// <summary><c>sha16</c> of <c>lines[from..to]</c> joined by LF.</summary>
    private static string Digest(List<ReadOnlyMemory<char>> lines, int from, int to)
    {
        if (from >= to)
        {
            return TaskText.EmptyDigest;
        }

        var size = to - from - 1;
        for (var k = from; k < to; k++)
        {
            size += Encoding.UTF8.GetByteCount(lines[k].Span);
        }

        byte[]? rented = null;
        var buffer = size <= StackLimit ? stackalloc byte[size] : (rented = ArrayPool<byte>.Shared.Rent(size));
        try
        {
            var at = 0;
            for (var k = from; k < to; k++)
            {
                if (k > from)
                {
                    buffer[at++] = (byte)'\n';
                }

                at += Encoding.UTF8.GetBytes(lines[k].Span, buffer[at..]);
            }

            return TaskText.Sha16(buffer[..at]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
