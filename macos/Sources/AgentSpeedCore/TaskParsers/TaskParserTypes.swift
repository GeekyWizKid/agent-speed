import Foundation

/// What a tool call did (TASK-PARSERS 2).
public enum TaskActionKind: String, Sendable, CaseIterable {
    case read, search, edit, command, delegate, web, other
}

/// The strongest check a command runs (TASK-PARSERS 3): test > build > check > other.
public enum TaskCommandClass: String, Sendable, CaseIterable {
    case test, build, check, other

    /// other 0, check 1, build 2, test 3.
    public var rank: Int {
        switch self {
        case .other: 0
        case .check: 1
        case .build: 2
        case .test: 3
        }
    }
}

/// `command_info` (TASK-PARSERS 3). METRICS 6.9 keeps `commandClass`, `suite`, `targeted` and `key`; `masked` and
/// `background` serve `outcome`.
public struct TaskCommandInfo: Equatable, Hashable, Sendable {
    public var commandClass: TaskCommandClass
    /// nil or one of `TaskSuites.all`.
    public var suite: String?
    public var targeted: Bool
    /// 16 hex digits identifying the normalised command.
    public var key: String
    public var masked: Bool
    public var background: Bool

    public init(commandClass: TaskCommandClass, suite: String?, targeted: Bool, key: String, masked: Bool,
                background: Bool) {
        self.commandClass = commandClass
        self.suite = suite
        self.targeted = targeted
        self.key = key
        self.masked = masked
        self.background = background
    }
}

/// `outcome` (TASK-PARSERS 4.2). A nil `failed` means unknown, never a failure.
public struct TaskOutcome: Equatable, Hashable, Sendable {
    public var exitCode: Int64?
    public var failed: Bool?
    /// The time of the result's line (Unix ms), passed through.
    public var t: Int64?

    public init(exitCode: Int64?, failed: Bool?, t: Int64?) {
        self.exitCode = exitCode
        self.failed = failed
        self.t = t
    }
}

/// A Claude background-task notice that reports an exit code (TASK-PARSERS 4.3).
public struct TaskNotice: Equatable, Hashable, Sendable {
    public var session: String
    public var exitCode: Int64
}

/// A Codex code-mode script (TASK-PARSERS 4.4).
public struct TaskCodeScript: Equatable, Sendable {
    public struct Call: Equatable, Sendable {
        /// The tool name after `tools.`.
        public var tool: String
        /// The call's single literal argument, or `.null` when it has none or it is not a literal.
        public var input: JSONValue
        /// `sha16` of the script text from `tools` through the call's closing parenthesis.
        public var digest: String
    }

    public struct Print: Equatable, Sendable {
        public enum Mode: String, Sendable {
            /// `text(CALL)`: the item is the call's whole result.
            case result
            /// `text((CALL).output)`: the item is the command's output alone.
            case output
        }

        /// The index of the printed call in `calls`.
        public var call: Int
        public var mode: Mode
    }

    public var calls: [Call]
    /// Only `;`, bindings, awaited calls and printing statements; `prints` is empty otherwise.
    public var plain: Bool
    public var prints: [Print]
}
