import Foundation

/// What the recognisers of one output emit (TASK-PARSERS 5.3): canonical failure strings in output order and
/// summaries.
final class FailureCollector {
    enum Family: Equatable {
        case test, error
    }

    enum Mode: Equatable {
        /// Only the last summary of the framework counts.
        case last
        /// All of them add up.
        case sum
    }

    struct Summary {
        var framework: String
        var family: Family
        var failed: Int64
        var passed: Int64?
        var mode: Mode
    }

    let cwd: String?
    private(set) var canonical: [Bytes] = []
    private(set) var summaries: [Summary] = []

    init(cwd: String?) {
        self.cwd = cwd
    }

    private func record(_ framework: String, _ kind: String, file: Bytes, code: Bytes, message: Bytes) {
        var text = Bytes(utf8: framework)
        text.append(0x7C)
        text.append(contentsOf: Bytes(utf8: kind))
        text.append(0x7C)
        if !file.isEmpty { text.append(contentsOf: OutputMasking.normalizedFile(file, cwd: cwd)) }
        text.append(0x7C)
        text.append(contentsOf: code)
        text.append(0x7C)
        if !message.isEmpty { text.append(contentsOf: OutputMasking.mask(message, cwd: cwd)) }
        canonical.append(text)
    }

    /// A failing test: its code is the masked name, its message empty.
    func test(_ framework: String, _ name: Bytes, file: Bytes = []) {
        record(framework, "test", file: file, code: OutputMasking.maskName(name, cwd: cwd), message: [])
    }

    func error(_ framework: String, file: Bytes, code: Bytes, message: Bytes) {
        record(framework, "error", file: file, code: code, message: message)
    }

    func summary(_ framework: String, _ family: Family, failed: Int64, passed: Int64?, mode: Mode = .last) {
        summaries.append(Summary(framework: framework, family: family, failed: failed, passed: passed, mode: mode))
    }
}

/// One framework family's reader. At line `i` it claims and consumes lines (returning how many) or returns 0.
protocol LineRecognizer: AnyObject {
    func step(_ lines: [TextLine], _ i: Int) -> Int
    func finish()
}

extension LineRecognizer {
    func finish() {}
}

enum FailureCounts {
    private static let part = Pattern(#"[ \t]*([0-9]+) ([A-Za-z][A-Za-z ]*?)[ \t]*"#)

    /// Counts in a summary line (`parse_counts`): each `N label` part adds N to its label.
    static func parse(_ text: Bytes, separator: UInt8 = 0x2C) -> [Bytes: Int64] {
        var counts: [Bytes: Int64] = [:]
        for piece in text.split(byte: separator) {
            guard let match = part.fullMatch(piece) else { continue }
            counts[match.text(2), default: 0] = TaskText.add(counts[match.text(2), default: 0],
                                                            TaskText.number(match.text(1)))
        }
        return counts
    }

    static func sum(_ counts: [Bytes: Int64], _ labels: String...) -> Int64 {
        labels.reduce(0) { TaskText.add($0, counts[Bytes(utf8: $1)] ?? 0) }
    }
}

extension Array where Element == UInt8 {
    /// Starts with a space or a tab.
    var isIndented: Bool { first == Byte.space || first == Byte.tab }
}
