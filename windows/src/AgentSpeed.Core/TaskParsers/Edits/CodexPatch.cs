namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Codex patches (TASK-PARSERS 6.3): the envelope between <c>*** Begin Patch</c> and <c>*** End Patch</c>, its
/// <c>Add File</c>, <c>Delete File</c> and <c>Update File</c> sections (with <c>Move to</c>), and the <c>@@</c> chunks
/// of an update.
/// </summary>
internal static class CodexPatch
{
    public static List<EditHunk> Of(string text, string? cwd)
    {
        var hunks = new List<EditHunk>();
        Read(text, cwd, hunks);
        return hunks;
    }

    /// <summary>Appends the hunks of the patch <paramref name="text"/> in input order.</summary>
    public static void Read(string text, string? cwd, List<EditHunk> hunks)
    {
        var lines = EditText.Split(TaskText.Lf(text));
        var start = 0;
        for (var k = 0; k < lines.Count; k++)
        {
            if (lines[k].Span.Trim(" \t").SequenceEqual("*** Begin Patch"))
            {
                start = k + 1;
                break;
            }
        }

        var sections = new List<Section>();
        for (var k = start; k < lines.Count; k++)
        {
            var line = lines[k];
            if (line.Span.Trim(" \t").SequenceEqual("*** End Patch"))
            {
                break;
            }

            if (Header(line.Span.TrimEnd(" \t"), out var op, out var path))
            {
                if (op != "Move to")
                {
                    sections.Add(new Section(op, path));
                    continue;
                }

                if (sections.Count > 0 && sections[^1].Op == "Update File")
                {
                    sections[^1].MoveTo = path;
                    continue;
                }
            }

            if (sections.Count > 0)
            {
                sections[^1].Body.Add(line);
            }
        }

        foreach (var section in sections)
        {
            switch (section.Op)
            {
                case "Add File":
                    var added = new List<ReadOnlyMemory<char>>();
                    foreach (var line in section.Body)
                    {
                        if (line.Span.StartsWith('+'))
                        {
                            added.Add(line[1..]);
                        }
                    }

                    hunks.Add(EditText.WholeLines(EditText.File(section.Path, cwd), added));
                    break;
                case "Delete File":
                    hunks.Add(EditText.Deleted(section.Path, cwd));
                    break;
                default:
                    var target = section.Path;
                    if (section.MoveTo is { Length: > 0 } moveTo)
                    {
                        hunks.Add(EditText.Deleted(section.Path, cwd));
                        target = moveTo;
                    }

                    UpdateChunks(section.Body, EditText.File(target, cwd), hunks);
                    break;
            }
        }
    }

    /// <summary>Whether a line of the LF-normalised text, right-trimmed, is a <c>PATCH_HEADER</c> line.</summary>
    public static bool HasHeader(IReadOnlyList<ReadOnlyMemory<char>> lines)
    {
        foreach (var line in lines)
        {
            if (EditPatterns.PatchHeader().IsMatch(line.Span.TrimEnd(" \t")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The hunks of the <c>@@</c> chunks of an Update File section or a diff: <c>-</c> old only, <c>+</c> new only,
    /// <c>*** End of File</c> skipped, any other line context for both (one leading space removed).
    /// </summary>
    public static void UpdateChunks(IReadOnlyList<ReadOnlyMemory<char>> lines, string file, List<EditHunk> hunks)
    {
        var old = new List<ReadOnlyMemory<char>>();
        var @new = new List<ReadOnlyMemory<char>>();
        var changed = false;
        foreach (var line in lines)
        {
            var span = line.Span;
            if (span.StartsWith("@@"))
            {
                if (changed)
                {
                    hunks.Add(EditText.HunkLines(file, old, @new));
                }

                old.Clear();
                @new.Clear();
                changed = false;
            }
            else if (span.SequenceEqual("*** End of File"))
            {
                continue;
            }
            else if (span.StartsWith('-'))
            {
                old.Add(line[1..]);
                changed = true;
            }
            else if (span.StartsWith('+'))
            {
                @new.Add(line[1..]);
                changed = true;
            }
            else
            {
                var context = span.StartsWith(' ') ? line[1..] : line;
                old.Add(context);
                @new.Add(context);
            }
        }

        if (changed)
        {
            hunks.Add(EditText.HunkLines(file, old, @new));
        }
    }

    private static bool Header(ReadOnlySpan<char> line, out string op, out string path)
    {
        op = path = "";
        if (!line.StartsWith("*** ") || !EditPatterns.PatchHeader().IsMatch(line))
        {
            return false;
        }

        var m = EditPatterns.PatchHeader().Match(line.ToString());
        op = m.Groups[1].Value;
        path = TaskText.Trim(m.Groups[2].Value);
        return true;
    }

    private sealed class Section(string op, string path)
    {
        public string Op { get; } = op;

        public string Path { get; } = path;

        public string? MoveTo { get; set; }

        public List<ReadOnlyMemory<char>> Body { get; } = [];
    }
}
