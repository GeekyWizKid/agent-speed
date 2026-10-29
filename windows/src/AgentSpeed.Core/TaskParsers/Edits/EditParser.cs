using System.Globalization;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// <c>edit_hunks</c> (TASK-PARSERS 6): the <c>{file, old, new}</c> digests of what an edit call changed, in input order;
/// empty for a call that is not an edit or whose input cannot be split.
/// </summary>
internal static class EditParser
{
    public static IReadOnlyList<EditHunk> Hunks(TaskFamily family, string? tool, JsonElement input, string? cwd)
    {
        if (family == TaskFamily.None || tool is null)
        {
            return [];
        }

        var name = TaskFamilies.ToolName(family, tool);
        var kind = ToolKinds.Of(family, name);
        if (kind == ActionKind.Command)
        {
            return ShellPatches(family, tool, input, cwd);
        }

        if (kind != ActionKind.Edit)
        {
            return [];
        }

        var args = TaskJson.AsObject(input);
        if (family == TaskFamily.Codex)
        {
            if (input.ValueKind == JsonValueKind.String && args is null)
            {
                return CodexPatch.Of(TaskJson.String(input)!, cwd);
            }

            var patch = TaskJson.GetString(args, "input") ?? TaskJson.GetString(args, "patch");
            return patch is null ? [] : CodexPatch.Of(patch, cwd);
        }

        if (args is not { } obj)
        {
            return [];
        }

        return family switch
        {
            TaskFamily.Claude => ClaudeHunks(name, obj, cwd),
            TaskFamily.Droid => DroidHunks(name, obj, cwd),
            _ => PiHunks(family, name, obj, cwd),
        };
    }

    /// <summary>Factory Droid <c>Create</c>, <c>Edit</c> and <c>MultiEdit</c> (6.2); an empty path still counts.</summary>
    private static IReadOnlyList<EditHunk> DroidHunks(string name, JsonElement args, string? cwd)
    {
        if (TaskJson.GetString(args, "file_path") is not { } path)
        {
            return [];
        }

        if (name == "Create")
        {
            return TaskJson.GetString(args, "content") is { } content ? [EditText.Whole(path, content, cwd)] : [];
        }

        if (name == "Edit")
        {
            return DroidPair(args) is var (old, @new) ? [EditText.Hunk(path, old, @new, cwd)] : [];
        }

        var hunks = new List<EditHunk>();
        if (TaskJson.Get(args, "edits") is { ValueKind: JsonValueKind.Array } edits)
        {
            foreach (var entry in edits.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && DroidPair(entry) is var (entryOld, entryNew))
                {
                    hunks.Add(EditText.Hunk(path, entryOld, entryNew, cwd));
                }
            }
        }

        return hunks;
    }

    /// <summary>A Droid replacement: <c>old_str</c> / <c>new_str</c>, else <c>old_string</c> / <c>new_string</c>.</summary>
    private static (string Old, string New)? DroidPair(JsonElement entry)
    {
        if (TaskJson.GetString(entry, "old_str") is { } old && TaskJson.GetString(entry, "new_str") is { } @new)
        {
            return (old, @new);
        }

        if (TaskJson.GetString(entry, "old_string") is { } oldString && TaskJson.GetString(entry, "new_string") is { } newString)
        {
            return (oldString, newString);
        }

        return null;
    }

    /// <summary>Every patch text in a shell command (3.9): heredoc bodies, <c>bash -lc</c> scripts and argv elements.</summary>
    private static List<EditHunk> ShellPatches(TaskFamily family, string tool, JsonElement input, string? cwd)
    {
        var hunks = new List<EditHunk>();
        if (ShellCommands.CommandText(family, tool, input) is { } text)
        {
            foreach (var patch in ShellCommands.PatchTexts(text, cwd))
            {
                CodexPatch.Read(patch, cwd, hunks);
            }
        }

        return hunks;
    }

    private static IReadOnlyList<EditHunk> ClaudeHunks(string name, JsonElement args, string? cwd)
    {
        var path = TaskJson.GetString(args, "file_path");
        switch (name)
        {
            case "Edit" when path is { Length: > 0 }
                             && TaskJson.GetString(args, "old_string") is { } old
                             && TaskJson.GetString(args, "new_string") is { } @new:
                return [EditText.Hunk(path, old, @new, cwd)];
            case "MultiEdit" when path is { Length: > 0 }:
                var hunks = new List<EditHunk>();
                if (TaskJson.Get(args, "edits") is { ValueKind: JsonValueKind.Array } edits)
                {
                    foreach (var entry in edits.EnumerateArray())
                    {
                        if (TaskJson.GetString(entry, "old_string") is { } entryOld && TaskJson.GetString(entry, "new_string") is { } entryNew)
                        {
                            hunks.Add(EditText.Hunk(path, entryOld, entryNew, cwd));
                        }
                    }
                }

                return hunks;
            case "Write" when path is { Length: > 0 } && TaskJson.GetString(args, "content") is { } content:
                return [EditText.Whole(path, content, cwd)];
            case "NotebookEdit" when TaskJson.GetString(args, "notebook_path") is { } notebook:
                var cell = TaskJson.GetString(args, "cell_id")
                           ?? (TaskJson.IsInt(TaskJson.Get(args, "cell_number"), out var number) ? number.ToString(CultureInfo.InvariantCulture) : "");
                var source = TaskJson.GetString(args, "new_source") ?? "";
                return TaskJson.GetString(args, "edit_mode") switch
                {
                    "insert" => [EditText.Hunk(notebook, "", source, cwd)],
                    "delete" => [EditText.Deleted(notebook + "#" + cell, cwd)],
                    _ => [EditText.Whole(notebook + "#" + cell, source, cwd)],
                };
            default:
                return [];
        }
    }

    private static IReadOnlyList<EditHunk> PiHunks(TaskFamily family, string name, JsonElement args, string? cwd)
    {
        var path = TaskJson.GetString(args, "path");
        switch (name)
        {
            case "write":
                return path is { Length: > 0 } && TaskJson.GetString(args, "content") is { } content ? [EditText.Whole(path, content, cwd)] : [];
            case "apply_patch":
                return TaskJson.GetString(args, "input") is { } patch ? CodexPatch.Of(patch, cwd) : [];
            case "edit":
                if (TaskJson.GetString(args, "input") is { } text)
                {
                    return PatchInput(text, cwd);
                }

                if (family == TaskFamily.Omp && path is { Length: > 0 }
                    && TaskJson.GetString(args, "old_string") is { } old && TaskJson.GetString(args, "new_string") is { } @new)
                {
                    return [EditText.Hunk(path, old, @new, cwd)];
                }

                if (family == TaskFamily.Omp && path is { Length: > 0 } && HasPatchEntries(args))
                {
                    return PiEdits.OmpPatchMode(args, path, cwd);
                }

                if (path is { Length: > 0 })
                {
                    var hunks = new List<EditHunk>();
                    foreach (var (pairOld, pairNew) in PiEdits.Pairs(args))
                    {
                        hunks.Add(EditText.Hunk(path, pairOld, pairNew, cwd));
                    }

                    return hunks;
                }

                return [];
            default:
                return [];
        }
    }

    /// <summary>An <c>input</c> string read by its shape (6.4): <c>apply_patch</c>, <c>hashline</c>, else nothing (<c>sloppy</c>).</summary>
    private static IReadOnlyList<EditHunk> PatchInput(string text, string? cwd)
    {
        var lines = EditText.Split(TaskText.Lf(text));
        if (CodexPatch.HasHeader(lines))
        {
            return CodexPatch.Of(text, cwd);
        }

        return HashlinePatch.HasHeader(lines) ? HashlinePatch.Of(lines, cwd) : [];
    }

    private static bool HasPatchEntries(JsonElement args)
    {
        if (TaskJson.Get(args, "edits") is not { ValueKind: JsonValueKind.Array } edits)
        {
            return false;
        }

        foreach (var entry in edits.EnumerateArray())
        {
            if (TaskJson.Has(entry, "diff") || TaskJson.Has(entry, "op"))
            {
                return true;
            }
        }

        return false;
    }
}
