using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>The object shapes of the Pi, OpenClaw and omp <c>edit</c> tool (TASK-PARSERS 6.2, 6.4).</summary>
internal static class PiEdits
{
    /// <summary>
    /// The <c>{oldText, newText}</c> pairs of <c>edits</c> (an array, a single object, or a string holding a JSON array
    /// or object), then the legacy top-level pair when it is not already listed.
    /// </summary>
    public static List<(string Old, string New)> Pairs(JsonElement args)
    {
        var pairs = new List<(string Old, string New)>();
        var edits = TaskJson.Get(args, "edits");
        if (edits.ValueKind == JsonValueKind.String)
        {
            edits = TaskJson.Parse(TaskJson.String(edits)!) ?? default;
        }

        if (edits.ValueKind == JsonValueKind.Object)
        {
            AddPair(edits, pairs);
        }
        else if (edits.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in edits.EnumerateArray())
            {
                AddPair(entry, pairs);
            }
        }

        if (TaskJson.GetString(args, "oldText") is { } old && TaskJson.GetString(args, "newText") is { } @new && !pairs.Contains((old, @new)))
        {
            pairs.Add((old, @new));
        }

        return pairs;
    }

    /// <summary>
    /// omp's <c>patch</c> mode: per entry of <c>edits</c>, <c>op</c> <c>delete</c> → a deletion of <paramref name="path"/>;
    /// <c>create</c> → a whole-file write of <c>diff</c> (its leading <c>+</c> removed when every non-empty line has
    /// one); otherwise the <c>diff</c> chunks as an update, under <c>rename</c> when given (after a deletion).
    /// </summary>
    public static List<EditHunk> OmpPatchMode(JsonElement args, string path, string? cwd)
    {
        var hunks = new List<EditHunk>();
        foreach (var entry in TaskJson.Get(args, "edits").EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var op = TaskJson.GetString(entry, "op");
            var diff = TaskJson.GetString(entry, "diff") ?? "";
            if (op == "delete")
            {
                hunks.Add(EditText.Deleted(path, cwd));
            }
            else if (op == "create")
            {
                var body = EditText.Split(TaskText.Lf(diff));
                if (body.TrueForAll(x => x.IsEmpty || x.Span[0] == '+'))
                {
                    for (var k = 0; k < body.Count; k++)
                    {
                        body[k] = body[k].IsEmpty ? body[k] : body[k][1..];
                    }
                }

                hunks.Add(EditText.WholeLines(EditText.File(path, cwd), body));
            }
            else
            {
                var target = path;
                if (TaskJson.GetString(entry, "rename") is { Length: > 0 } rename)
                {
                    hunks.Add(EditText.Deleted(path, cwd));
                    target = rename;
                }

                CodexPatch.UpdateChunks(EditText.Split(TaskText.Lf(diff)), EditText.File(target, cwd), hunks);
            }
        }

        return hunks;
    }

    private static void AddPair(JsonElement entry, List<(string Old, string New)> pairs)
    {
        if (TaskJson.GetString(entry, "oldText") is { } old && TaskJson.GetString(entry, "newText") is { } @new)
        {
            pairs.Add((old, @new));
        }
    }
}
