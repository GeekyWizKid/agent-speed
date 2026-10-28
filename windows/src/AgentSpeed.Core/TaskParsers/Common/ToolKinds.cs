namespace AgentSpeed.Core.TaskParsers;

/// <summary>The tool tables of TASK-PARSERS 2 (Appendix A.1): a tool name's kind per source family.</summary>
internal static class ToolKinds
{
    private static readonly Dictionary<string, ActionKind> Claude = Table(
        (ActionKind.Read, ["Read", "NotebookRead", "LS"]),
        (ActionKind.Search, ["Grep", "Glob", "LSP"]),
        (ActionKind.Edit, ["Edit", "MultiEdit", "Write", "NotebookEdit"]),
        (ActionKind.Command, ["Bash", "PowerShell"]),
        (ActionKind.Delegate, ["Task", "Agent", "SendMessage"]),
        (ActionKind.Web, ["WebFetch", "WebSearch"]));

    private static readonly Dictionary<string, ActionKind> Codex = Table(
        (ActionKind.Read, ["read_file", "list_dir", "view_image"]),
        (ActionKind.Search, ["grep_files"]),
        (ActionKind.Edit, ["apply_patch"]),
        (ActionKind.Command, ["shell", "container.exec", "local_shell", "shell_command", "exec_command", "exec"]),
        (ActionKind.Delegate, ["spawn_agent", "send_input", "resume_agent"]),
        (ActionKind.Web, ["web_search", "web_fetch"]));

    private static readonly Dictionary<string, ActionKind> Omp = Table(
        (ActionKind.Read, ["read"]),
        (ActionKind.Search, ["grep", "search", "find", "glob", "ast_grep", "lsp"]),
        (ActionKind.Edit, ["edit", "write", "ast_edit"]),
        (ActionKind.Command, ["bash"]),
        (ActionKind.Delegate, ["task"]),
        (ActionKind.Web, ["web_search", "fetch", "browser"]));

    private static readonly Dictionary<string, ActionKind> Pi = Table(
        (ActionKind.Read, ["read", "ls"]),
        (ActionKind.Search, ["grep", "find"]),
        (ActionKind.Edit, ["edit", "write"]),
        (ActionKind.Command, ["bash", "powershell"]));

    private static readonly Dictionary<string, ActionKind> OpenClaw = Table(
        (ActionKind.Read, ["read", "ls"]),
        (ActionKind.Edit, ["edit", "write", "apply_patch"]),
        (ActionKind.Command, ["exec", "bash"]),
        (ActionKind.Delegate, ["sessions_spawn", "sessions_send"]),
        (ActionKind.Web, ["web_search", "web_fetch", "x_search", "browser"]));

    private static readonly Dictionary<string, ActionKind> Droid = Table(
        (ActionKind.Read, ["Read", "LS"]),
        (ActionKind.Search, ["Grep", "Glob"]),
        (ActionKind.Edit, ["Create", "Edit", "MultiEdit"]),
        (ActionKind.Command, ["Execute"]),
        (ActionKind.Delegate, ["Task"]),
        (ActionKind.Web, ["WebSearch", "FetchUrl"]));

    /// <summary>
    /// The kind of a tool name already folded by <see cref="TaskFamilies.ToolName"/>, or null when the source's table
    /// does not list it (kind <c>other</c>).
    /// </summary>
    public static ActionKind? Of(TaskFamily family, string name)
    {
        var table = family switch
        {
            TaskFamily.Claude => Claude,
            TaskFamily.Codex => Codex,
            TaskFamily.Omp => Omp,
            TaskFamily.Pi => Pi,
            TaskFamily.OpenClaw => OpenClaw,
            TaskFamily.Droid => Droid,
            _ => null,
        };
        return table is not null && table.TryGetValue(name, out var kind) ? kind : null;
    }

    private static Dictionary<string, ActionKind> Table(params (ActionKind Kind, string[] Names)[] rows)
    {
        var table = new Dictionary<string, ActionKind>(StringComparer.Ordinal);
        foreach (var (kind, names) in rows)
        {
            foreach (var name in names)
            {
                table[name] = kind;
            }
        }

        return table;
    }
}
