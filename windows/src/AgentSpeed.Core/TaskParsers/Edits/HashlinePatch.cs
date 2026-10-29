namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// omp's <c>hashline</c> edits (TASK-PARSERS 6.4): per <c>[PATH#TAG]</c> section, an insertion with its <c>+</c> lines
/// gives a hunk from nothing, <c>REM</c> a deletion and <c>MV DEST</c> a deletion of the current path, which becomes
/// <c>DEST</c>. Replacements and cuts name line numbers, not the old text, and give nothing.
/// </summary>
internal static class HashlinePatch
{
    /// <summary>The hunks of the LF-normalised <paramref name="lines"/>.</summary>
    public static List<EditHunk> Of(IReadOnlyList<ReadOnlyMemory<char>> lines, string? cwd)
    {
        var hunks = new List<EditHunk>();
        string? path = null;
        var k = 0;
        while (k < lines.Count)
        {
            var line = lines[k].Span.TrimEnd(" \t");
            k++;
            if (EditPatterns.HashlineHeader().IsMatch(line))
            {
                path = TaskText.Trim(EditPatterns.HashlineHeader().Match(line.ToString()).Groups[1].Value);
                continue;
            }

            if (path is null)
            {
                continue;
            }

            if (EditPatterns.HashlineInsert().IsMatch(line))
            {
                var body = new List<ReadOnlyMemory<char>>();
                while (k < lines.Count && lines[k].Span.StartsWith('+'))
                {
                    body.Add(lines[k][1..]);
                    k++;
                }

                if (body.Count > 0)
                {
                    hunks.Add(EditText.HunkLines(EditText.File(path, cwd), [], body));
                }
            }
            else if (line.SequenceEqual("REM"))
            {
                hunks.Add(EditText.Deleted(path, cwd));
            }
            else if (EditPatterns.HashlineMove().IsMatch(line))
            {
                hunks.Add(EditText.Deleted(path, cwd));
                path = TaskText.Trim(EditPatterns.HashlineMove().Match(line.ToString()).Groups[1].Value);
            }
        }

        return hunks;
    }

    /// <summary>Whether a line, right-trimmed, is a <c>HASHLINE_HEADER</c> line.</summary>
    public static bool HasHeader(IReadOnlyList<ReadOnlyMemory<char>> lines)
    {
        foreach (var line in lines)
        {
            if (EditPatterns.HashlineHeader().IsMatch(line.Span.TrimEnd(" \t")))
            {
                return true;
            }
        }

        return false;
    }
}
