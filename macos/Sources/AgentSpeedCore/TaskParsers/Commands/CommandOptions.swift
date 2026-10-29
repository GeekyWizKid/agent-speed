import Foundation

/// A program's *value options* (TASK-PARSERS 3.4): the options that take the next word as their value.
struct OptionList: Sendable {
    let names: Set<Bytes>
    /// The two-character ones, which may also carry their value attached (`-kpush`).
    let short: Set<Bytes>

    init(_ names: [String]) {
        self.names = TaskText.words(names)
        short = Set(self.names.filter { $0.count == 2 })
    }

    static let none = OptionList([])

    @inline(__always) func takesValue(_ word: Bytes) -> Bool { names.contains(word) }
}

/// An option's value: a flag, or the text it carries.
enum OptionValue: Equatable {
    case flag
    case value(Bytes)

    /// Python truthiness: a flag, or a non-empty value.
    var isTruthy: Bool {
        if case .value(let text) = self { return !text.isEmpty }
        return true
    }

    var text: Bytes? {
        if case .value(let text) = self { return text }
        return nil
    }
}

/// Positionals, options and the words after `--` of one program's arguments (`parse_opts`).
struct ParsedOptions {
    var positionals: [Bytes] = []
    var options: [Bytes: OptionValue] = [:]
    var afterDashDash: [Bytes] = []

    func has(_ name: String) -> Bool { options[Bytes(utf8: name)] != nil }

    func hasAny(_ names: Set<Bytes>) -> Bool { names.contains { options[$0] != nil } }

    subscript(_ name: String) -> OptionValue? { options[Bytes(utf8: name)] }

    /// Positionals and the words after `--`, which the path and name rules read.
    var operands: [Bytes] { positionals + afterDashDash }
}

enum CommandOptions {
    static let dashDash = Bytes(utf8: "--")

    /// Python's `a or b or …` over option values: the first truthy one before the last, else the last.
    static func firstTruthy(_ values: OptionValue?...) -> OptionValue? {
        for value in values.dropLast() where value?.isTruthy == true { return value }
        return values.last ?? nil
    }

    /// Skips option words from `start`; value options consume the next word (`drop_options`).
    static func dropOptions(_ words: [Bytes], from start: Int, values: OptionList = .none,
                            stopAtDashDash: Bool = true) -> Int {
        var i = start
        while i < words.count {
            let word = words[i]
            if word == dashDash, stopAtDashDash { return i + 1 }
            if word.first != Byte.dash || word.count == 1 { return i }
            i += values.takesValue(word) ? 2 : 1
        }
        return i
    }

    /// `parse_opts` (TASK-PARSERS 3.4).
    static func parse(_ args: [Bytes], values: OptionList = .none, stopAtDashDash: Bool = true) -> ParsedOptions {
        var result = ParsedOptions()
        var k = 0
        while k < args.count {
            let word = args[k]
            if word == dashDash, stopAtDashDash {
                result.afterDashDash = Array(args[(k + 1)...])
                break
            }
            if word.first == Byte.dash, word.count > 1 {
                if word.count > 1, word[1] == Byte.dash, let equals = word.firstIndex(of: Byte.equals) {
                    result.options[Bytes(word[0..<equals])] = .value(Bytes(word[(equals + 1)...]))
                } else if values.takesValue(word), k + 1 < args.count {
                    result.options[word] = .value(args[k + 1])
                    k += 1
                } else if word.count > 2, values.short.contains(Bytes(word[0..<2])) {
                    result.options[Bytes(word[0..<2])] = .value(Bytes(word[2...]))
                } else {
                    result.options[word] = .flag
                }
            } else {
                result.positionals.append(word)
            }
            k += 1
        }
        return result
    }

    /// The word ends with `.` and 1 to 10 ASCII letters or digits.
    static func hasExtension(_ word: Bytes) -> Bool {
        guard let dot = word.lastIndex(of: Byte.dot) else { return false }
        let length = word.count - dot - 1
        return length >= 1 && length <= 10 && word[(dot + 1)...].allSatisfy(Byte.isAlnum)
    }

    /// `.`, `./`, or a directory path of at most one component (TASK-PARSERS 3.5, path rule).
    static func isBroadPath(_ arg: Bytes, cwd: String?) -> Bool {
        var (a, _) = TaskPaths.cwdRelative(arg.replacing(byte: Byte.backslash, with: Byte.slash), cwd: cwd)
        while a.hasPrefix(Bytes(utf8: "./")) { a.removeFirst(2) }
        a = a.trimmedRight(Byte.slash)
        if a.isEmpty || a == [Byte.dot] { return true }
        if a.contains(0x2A) || a.contains(0x3F) || a.contains(Bytes(utf8: "::")) || a.first == Byte.slash
            || (a.count >= 2 && Byte.isLetter(a[0]) && a[1] == Byte.colon) {
            return false
        }
        return !a.contains(Byte.slash) && !hasExtension(a)
    }

    static func pathTargeted(_ words: [Bytes], cwd: String?) -> Bool {
        words.contains { !isBroadPath($0, cwd: cwd) }
    }

    /// Runners whose positionals are name or path patterns: any word but `.` or cwd narrows the run.
    static func nameTargeted(_ words: [Bytes], cwd: String?) -> Bool {
        for word in words {
            let (a, inside) = TaskPaths.cwdRelative(
                word.replacing(byte: Byte.backslash, with: Byte.slash).trimmedRight(Byte.slash), cwd: cwd)
            if !a.isEmpty, a != [Byte.dot], !(inside && a.isEmpty) { return true }
        }
        return false
    }
}
