import Foundation

/// The task-score parsers (spec/TASK-PARSERS.md): pure, deterministic functions that turn one tool call and its result
/// into the small facts METRICS 6.9 keeps (kinds, classes, fixed-vocabulary suites, 16-hex digests, counts, exit
/// codes). They apply to the sources that record tool calls (Claude Code and Desktop, Codex, omp, Pi, OpenClaw and
/// Factory Droid); any other source gives `other`, an unknown outcome, empty text, no session and no hunks.
///
/// Inputs are the values as the transcript line stores them: `tool` is the call's name, `input` the call's input (an
/// object, or a string for Codex), `result` the source's result block or message, `cwd` the agent's working directory.
/// Any input of an unexpected type is treated as absent and no function fails. Inputs are read in memory only: nothing
/// of them is stored, logged or returned (TASK-PARSERS 7).
public enum TaskParsers {
    /// `action_kind` (TASK-PARSERS 2): shell calls are refined by their command (an `apply_patch` is an edit, a test,
    /// build or check is a command, only reading or searching programs are a read or a search).
    public static func actionKind(_ source: SourceKind, tool: String?, input: JSONValue?, cwd: String?) -> TaskActionKind {
        guard let family = TaskFamily(source), let tool else { return .other }
        let kind = family.kind(of: tool)
        guard kind == .command else { return kind }
        guard let text = TaskInput.commandText(family, tool: tool, input: input) else { return .command }
        return CommandAnalysis.shellKind(ShellCommand(text, cwd: cwd))
    }

    /// The command line a shell tool runs (TASK-PARSERS 3.1), or nil: the text `commandInfo` reads.
    public static func commandText(_ source: SourceKind, tool: String?, input: JSONValue?) -> String? {
        guard let family = TaskFamily(source) else { return nil }
        return TaskInput.commandText(family, tool: tool, input: input)
    }

    /// `command_info` (TASK-PARSERS 3).
    public static func commandInfo(_ text: String, cwd: String?) -> TaskCommandInfo {
        CommandAnalysis.info(ShellCommand(text, cwd: cwd), cwd: cwd)
    }

    /// `masked_status` (TASK-PARSERS 3.10): the status a masked command shows in its own output, or nil.
    public static func maskedStatus(_ text: String, output: String, cwd: String?) -> TaskMaskedStatus? {
        let command = ShellCommand(text, cwd: cwd)
        return MaskedRuns.status(command, info: CommandAnalysis.info(command, cwd: cwd), output: Bytes(utf8: output),
                                 cwd: cwd)
    }

    /// `outcome` (TASK-PARSERS 4.2): `t` (the result line's time, Unix ms) is passed through.
    public static func outcome(_ source: SourceKind, result: JSONValue?, t: Int64?, tool: String?, input: JSONValue?,
                               cwd: String?) -> TaskOutcome {
        Outcomes.outcome(source, result: result, t: t, tool: tool, input: input, cwd: cwd)
    }

    /// `result_text` (TASK-PARSERS 4.1): the output text `failures` reads, LF-normalised.
    public static func resultText(_ source: SourceKind, result: JSONValue?) -> String {
        ResultReading.resultText(TaskFamily(source), result).string
    }

    /// `continuation` (TASK-PARSERS 4.3): the session a call continues (`codex:<id>`, `claude:<id>`), or nil.
    public static func continuation(_ source: SourceKind, tool: String?, input: JSONValue?) -> String? {
        Outcomes.continuation(source, tool: tool, input: input)
    }

    /// `running_session` (TASK-PARSERS 4.3): the session a result leaves running, or nil.
    public static func runningSession(_ source: SourceKind, result: JSONValue?) -> String? {
        Outcomes.runningSession(source, result: result)
    }

    /// `notification` (TASK-PARSERS 4.3): a Claude background-task notice line's session and exit code, or nil.
    public static func notification(_ source: SourceKind, line: JSONValue?) -> TaskNotice? {
        Outcomes.notification(source, line: line)
    }

    /// `code_script` (TASK-PARSERS 4.4): the tool calls and printing statements of a Codex code-mode script, or nil.
    public static func codeScript(_ source: SourceKind, tool: String?, input: JSONValue?) -> TaskCodeScript? {
        CodeScripts.script(source, tool: tool, input: input)
    }

    /// `code_items` (TASK-PARSERS 4.4): the printed results of a completed code-mode script (nil for an item without
    /// text), or nil.
    public static func codeItems(_ source: SourceKind, result: JSONValue?) -> [String?]? {
        CodeScripts.items(source, result: result)
    }

    /// `failures` (TASK-PARSERS 5) of a command's output: `info` is its `commandInfo` (only `suite` and `errorFilter`
    /// are read), `output` its `resultText`, `failed` its `outcome.failed`.
    public static func failures(_ info: TaskCommandInfo?, output: String, cwd: String?, failed: Bool?) -> TaskFailures {
        FailureAnalysis.detail(suite: info?.suite, errorFilter: info?.errorFilter ?? false, output: Bytes(utf8: output),
                               cwd: cwd, failed: failed).0
    }

    /// `edit_hunks` (TASK-PARSERS 6): the changed regions of an edit in input order; empty when the call is not an
    /// edit or its input cannot be split.
    public static func editHunks(_ source: SourceKind, tool: String?, input: JSONValue?, cwd: String?) -> [TaskEditHunk] {
        EditHunks.hunks(source, tool: tool, input: input, cwd: cwd)
    }

    /// `failures` plus the canonical strings behind its fingerprints, for fixture tests only (TASK-PARSERS 5.7).
    static func failuresDetail(_ info: TaskCommandInfo?, output: String, cwd: String?,
                               failed: Bool?) -> (TaskFailures, [String]) {
        let (result, canonical) = FailureAnalysis.detail(suite: info?.suite, errorFilter: info?.errorFilter ?? false,
                                                         output: Bytes(utf8: output), cwd: cwd, failed: failed)
        return (result, canonical.map(\.string))
    }
}
