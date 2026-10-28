import Foundation

/// A string compared and hashed by its UTF-8 bytes (Python's code point equality), never by Swift's canonical
/// equivalence: tool names, suites and model ids as `task_score.py` keys them.
struct TaskOrdinalKey: Hashable, Sendable {
    let text: String

    init(_ text: String) {
        self.text = text
    }

    static func == (lhs: TaskOrdinalKey, rhs: TaskOrdinalKey) -> Bool {
        lhs.text.utf8.elementsEqual(rhs.text.utf8)
    }

    func hash(into hasher: inout Hasher) {
        for byte in text.utf8 { hasher.combine(byte) }
        hasher.combine(0xFF as UInt8)
    }
}

/// Python `str` operations over Unicode scalars (code points).
enum TaskPythonText {
    @inline(__always)
    static func isSpace(_ scalar: Unicode.Scalar) -> Bool {
        JSONScanner.isPythonWhitespace(scalar.value)
    }

    /// Code point equality (Python `==` on `str`).
    @inline(__always)
    static func same(_ lhs: String?, _ rhs: String) -> Bool {
        guard let lhs else { return false }
        return lhs.utf8.elementsEqual(rhs.utf8)
    }

    @inline(__always)
    static func same(_ lhs: String?, _ rhs: String?) -> Bool {
        guard let lhs, let rhs else { return lhs == nil && rhs == nil }
        return lhs.utf8.elementsEqual(rhs.utf8)
    }

    /// Ordinal order by code points (Python `<` on `str`; UTF-8 bytes order the same way).
    @inline(__always)
    static func precedes(_ lhs: String, _ rhs: String) -> Bool {
        lhs.utf8.lexicographicallyPrecedes(rhs.utf8)
    }

    /// `not text.strip()`.
    static func isBlank(_ text: String) -> Bool {
        !text.unicodeScalars.contains { !isSpace($0) }
    }

    /// `text.lstrip()`.
    static func leftStripped(_ text: String) -> Substring.UnicodeScalarView {
        let scalars = text.unicodeScalars
        let start = scalars.firstIndex { !isSpace($0) } ?? scalars.endIndex
        return Substring(text).unicodeScalars[start...]
    }

    /// `text.startswith(prefix)` by code points.
    static func hasPrefix(_ scalars: Substring.UnicodeScalarView, _ prefix: String) -> Bool {
        var index = scalars.startIndex
        for expected in prefix.unicodeScalars {
            guard index < scalars.endIndex, scalars[index] == expected else { return false }
            index = scalars.index(after: index)
        }
        return true
    }
}
