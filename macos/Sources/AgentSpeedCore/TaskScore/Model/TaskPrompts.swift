import Foundation

/// The prompt kind of one transcript line (METRICS 6.9 *Tasks*).
public struct TaskPromptLine: Equatable, Sendable {
    public enum Kind: String, Sendable {
        case prompt, command, commandOutput
    }

    public var kind: Kind
    /// A prompt in the continuation list; always false for a command or command output.
    public var continuation: Bool
    /// The prompt's own time when it is not the line's (a queued Claude message with a valid `attachment.timestamp`).
    public var t: Int64?

    public init(kind: Kind, continuation: Bool = false, t: Int64? = nil) {
        self.kind = kind
        self.continuation = continuation
        self.t = t
    }
}

/// A line classified for withdrawal (METRICS 6.9 *Withdrawal*): a prompt, command or command output, or an assistant
/// line that is `duplicate` when 4.1 or 4.7 step 2 ignored it.
public struct TaskClassifiedLine: Equatable, Sendable {
    public enum Kind: Sendable {
        case prompt, command, commandOutput, assistant
    }

    public var kind: Kind
    public var t: Int64
    public var continuation: Bool
    public var duplicate: Bool

    public init(kind: Kind, t: Int64, continuation: Bool = false, duplicate: Bool = false) {
        self.kind = kind
        self.t = t
        self.continuation = continuation
        self.duplicate = duplicate
    }
}

/// Task segmentation (METRICS 6.9 *Tasks*): continuations, prompt lines and withdrawal, as `task_score.py`.
public enum TaskPrompts {
    static let continuations: Set<[Unicode.Scalar]> = Set([
        "continue", "continue please", "please continue", "go on", "keep going", "proceed", "please proceed",
        "carry on", "go ahead", "resume", "ok", "okay", "ok continue", "ok go ahead", "yes", "y", "yep", "yes please",
        "sure", "do it", "lgtm",
        "繼續", "繼續吧", "請繼續", "繼續做", "接著做", "好", "好的", "好啊", "可以", "嗯", "是", "對", "行", "沒問題",
        "继续", "继续吧", "请继续", "继续做", "接着做", "对", "没问题",
        "続けて", "続けてください", "続行", "進めて", "進めてください", "はい", "お願いします", "うん", "いいよ", "どうぞ",
    ].map { Array($0.unicodeScalars) })

    /// The longest entry, in scalars.
    static let longestContinuation = continuations.map(\.count).max() ?? 0

    static let trailing: Set<Unicode.Scalar> = [".", "!", "~", ",", "\u{3002}", "\u{3001}"]

    /// NFKC, trimmed, trailing `.!~,。、` and whitespace removed, ASCII letters lower-cased, whitespace runs collapsed.
    public static func normalize(_ text: String) -> String {
        var view = String.UnicodeScalarView()
        for scalar in normalizedScalars(text) { view.append(scalar) }
        return String(view)
    }

    public static func isContinuation(_ text: String) -> Bool {
        let scalars = normalizedScalars(text)
        return scalars.count <= longestContinuation && continuations.contains(scalars)
    }

    static func normalizedScalars(_ text: String) -> [Unicode.Scalar] {
        let scalars = Array(text.precomposedStringWithCompatibilityMapping.unicodeScalars)
        var start = 0
        var end = scalars.count
        while start < end, TaskPythonText.isSpace(scalars[start]) { start += 1 }
        while end > start, TaskPythonText.isSpace(scalars[end - 1]) || trailing.contains(scalars[end - 1]) { end -= 1 }
        var out: [Unicode.Scalar] = []
        out.reserveCapacity(end - start)
        var gap = false
        for scalar in scalars[start..<end] {
            if TaskPythonText.isSpace(scalar) {
                gap = true
                continue
            }
            if gap {
                out.append(" ")
                gap = false
            }
            let value = scalar.value
            out.append(value >= 0x41 && value <= 0x5A ? Unicode.Scalar(value + 32)! : scalar)
        }
        return out
    }

    /// Prompt events from classified lines: a prompt or command takes effect at once and is withdrawn when the first
    /// assistant line after it is a duplicate; a command is also withdrawn by a command output before that line.
    public static func segment(_ lines: [TaskClassifiedLine]) -> [TaskEvent] {
        var out: [TaskEvent] = []
        var pending: TaskClassifiedLine.Kind?
        for line in lines {
            switch line.kind {
            case .prompt, .command:
                out.append(.prompt(line.t, continuation: line.kind == .prompt && line.continuation))
                pending = line.kind
            case .commandOutput:
                if pending == .command { out.removeLast() }
                pending = nil
            case .assistant:
                guard pending != nil else { continue }
                if line.duplicate { out.removeLast() }
                pending = nil
            }
        }
        return out
    }
}
