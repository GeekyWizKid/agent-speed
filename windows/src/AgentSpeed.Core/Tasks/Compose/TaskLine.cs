using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// What one log line gives the composer (METRICS 6.9 <i>Composing the stream</i>), computed by the file's
/// <see cref="TaskLineReader"/> while the line is extracted: everything that depends on the file alone. The composer adds
/// what depends on the other files (duplicates). Holds no input, path or prompt text; a result's output text only while a
/// running session needs it.
/// </summary>
internal sealed class TaskLine
{
    /// <summary>A prompt, command or command-output line (6.9 <i>Tasks</i>).</summary>
    public PromptLine? Prompt { get; set; }

    /// <summary>An assistant line or entry that decides a pending withdrawal.</summary>
    public bool Assistant { get; set; }

    /// <summary>The dedupe key whose owner (another file) makes <see cref="Assistant"/> a duplicate; null: never one.</summary>
    public string? DedupeKey { get; set; }

    /// <summary>The line's calls in content order; a duplicate line adds none of them.</summary>
    public List<TaskCall>? Calls { get; set; }

    /// <summary>The line's results in content order.</summary>
    public List<TaskResult>? Results { get; set; }

    /// <summary>A Claude background-task notice that ends a running session (TASK-PARSERS 4.3).</summary>
    public TaskNotice? Notice { get; set; }

    public bool IsEmpty => Prompt is null && !Assistant && Calls is null && Results is null && Notice is null;
}

/// <summary>
/// A call as the line holds it: its action (kind, digest, command, hunks at the line's cwd), the session it continues,
/// and for a command the parsed <see cref="CommandInfo"/> the composer needs to compute stitched failures. A Codex
/// code-mode script carries its tool calls as <see cref="Script"/> instead of an action of its own (TASK-PARSERS 4.4).
/// </summary>
internal sealed class TaskCall(string id, TaskAction? action, string? continues, CommandInfo? info, string? cwd)
{
    public string Id { get; } = id;

    /// <summary>Null for a script.</summary>
    public TaskAction? Action { get; } = action;

    /// <summary><c>continuation(source, tool, input)</c>: the session key this call continues.</summary>
    public string? Continues { get; } = continues;

    public CommandInfo? Info { get; } = info;

    /// <summary>The call line's working directory, which the stitched failures receive.</summary>
    public string? Cwd { get; } = cwd;

    /// <summary>A script's calls in source order.</summary>
    public IReadOnlyList<TaskCall>? Script { get; init; }
}

/// <summary>
/// A result as the line holds it, already read against the file's call with that id. <see cref="Answer"/> is null when the
/// file had no such call before it; a script's result answers the calls whose printed items it maps, in printing order
/// (<see cref="ScriptAnswers"/>: the call's index in the script and its answer).
/// </summary>
internal sealed class TaskResult(string id)
{
    public string Id { get; } = id;

    public TaskAnswer? Answer { get; init; }

    public IReadOnlyList<(int Index, TaskAnswer Answer)>? ScriptAnswers { get; init; }

    public bool IsScript { get; init; }
}

/// <summary>A call's result read: its outcome, its failures (an action with a command, unless a session stays running), the
/// session it leaves running, and its output text when a session needs it.</summary>
internal sealed class TaskAnswer(TaskOutcome outcome)
{
    public TaskOutcome Outcome { get; } = outcome;

    public FailureReport? Failures { get; init; }

    /// <summary><c>running_session</c>: the session the result leaves running (only for a call that continues none).</summary>
    public string? Session { get; init; }

    /// <summary>The running command's <c>masked</c> flag (3.10): its exit code is the pipeline's.</summary>
    public bool Masked { get; init; }

    /// <summary><c>result_text</c>, kept when a session starts or the call continues one.</summary>
    public string? Text { get; init; }
}

/// <summary>A Claude <c>task-notification</c> notice: the session it ends, with its exit code at <see cref="T"/>.</summary>
internal sealed record TaskNotice(string Session, long ExitCode, long T);
