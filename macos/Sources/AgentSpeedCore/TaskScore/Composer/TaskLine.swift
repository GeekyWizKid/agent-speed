import Foundation

/// What one transcript line gives the task composer (METRICS 6.9 *Composing the stream*), prepared by
/// `TaskLineReader` from the line alone and the file's own earlier lines, so it can be built on a decoding thread. Only
/// the decisions that depend on other files (duplicates) and on the agent's stream (withdrawal, answered calls,
/// sessions) are left to `TaskComposer`, which applies the operations in order.
enum TaskLine: Sendable {
    /// The line adds nothing and is not a line of the stream (no time, copied history, a repeated header).
    case none
    /// A line of the stream at `t` (model and effort are noted after it), with its operations in line order.
    case line(t: Int64, ops: [TaskLineOp])

    static func line(_ t: Int64) -> TaskLine { .line(t: t, ops: []) }
}

enum TaskLineOp: Sendable {
    /// A prompt, command or command output that goes through withdrawal (Claude, the Pi family, Factory Droid).
    case prompt(TaskPromptLine.Kind, t: Int64, continuation: Bool)
    /// A Codex prompt: no withdrawal.
    case plainPrompt(t: Int64, continuation: Bool)
    /// An assistant line that decides withdrawal; `key` is its dedupe key, nil when it has none (never a duplicate).
    case assistant(key: String?)
    /// The calls of the preceding assistant line (skipped when it was a duplicate), or of a Codex line.
    case calls([TaskPreparedCall])
    /// A Codex code-mode script: its calls are the actions.
    case script(TaskPreparedScript)
    /// A result, analysed against the file's call with that id.
    case result(TaskPreparedResult)
    /// A Claude background-task notice (TASK-PARSERS 4.3) for session `session`.
    case notice(session: String, exitCode: Int64, t: Int64)
}

/// A call as its action will be added: everything derived from its input at its line, the input itself released.
struct TaskPreparedCall: Sendable {
    var id: String
    var t: Int64
    var action: TaskAction
    /// `command_info` of a command with command text (its `masked` and `errorFilter` serve the outcome and failures).
    var info: TaskCommandInfo?
    /// The session this call continues (TASK-PARSERS 4.3).
    var continues: String?
    /// The agent's working directory at the call's line.
    var cwd: String?
}

struct TaskPreparedScript: Sendable {
    var id: String
    var calls: [TaskPreparedCall]
}

/// What a result does to its call, computed against the file's call with that id (`answer` nil when the file has none
/// at that point: the result is ignored).
struct TaskPreparedResult: Sendable {
    enum Answer: Sendable {
        case call(TaskPreparedAnswer)
        /// A script's result: the answers of the calls its printed items map to, by call index (none when they do
        /// not map).
        case script([(index: Int, answer: TaskPreparedAnswer)])
    }

    var id: String
    var t: Int64
    var answer: Answer?
}

struct TaskPreparedAnswer: Sendable {
    var outcome: TaskOutcome
    /// For an action with `command`, unless the result leaves a session running.
    var failures: TaskRunFailures?
    /// `running_session`, for a call that is not itself a continuation.
    var session: String?
    /// The call's command is masked (a running session's exit code is then the pipeline's).
    var masked: Bool
    /// The result's text, kept only when a session needs it (a running session, or a continuation).
    var text: String?
}
