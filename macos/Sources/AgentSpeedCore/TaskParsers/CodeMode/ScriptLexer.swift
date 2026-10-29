import Foundation

/// The tokens of a Codex code-mode script (TASK-PARSERS 4.4, *Tokens*), read over scalars so that positions and
/// slices match the reference's string indices.
struct ScriptToken {
    enum Kind: Equatable {
        case number, string, regex, identifier, punctuator
    }

    let kind: Kind
    let start: Int
    let end: Int
    let text: String
    /// A string's value (nil for a template holding `${…}`) or a valued number's literal.
    let value: JSONValue?

    func isPunctuator(_ text: String) -> Bool { kind == .punctuator && self.text == text }
    func isIdentifier(_ text: String) -> Bool { kind == .identifier && self.text == text }
}

struct ScriptLexError: Error {}

enum ScriptLexer {
    private static let whitespace: Set<Unicode.Scalar> = [" ", "\t", "\r", "\n", "\u{0C}", "\u{0B}"]
    private static let lineBreaks: [[Unicode.Scalar]] = [["\r", "\n"], ["\n"], ["\r"], ["\u{2028}"], ["\u{2029}"]]
    static let regexAfter: Set<String> = [
        "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield",
        "await",
    ]
    private static let number = Pattern(#"[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?"#)

    @inline(__always) static func isIdentifierStart(_ c: Unicode.Scalar) -> Bool {
        c.isASCII && (Byte.isLetter(UInt8(c.value)) || c == "_" || c == "$")
    }

    @inline(__always) static func isIdentifierPart(_ c: Unicode.Scalar) -> Bool {
        isIdentifierStart(c) || ("0"..."9").contains(c)
    }

    @inline(__always) private static func isDigit(_ c: Unicode.Scalar) -> Bool { ("0"..."9").contains(c) }

    private static func starts(_ text: [Unicode.Scalar], _ prefix: [Unicode.Scalar], at i: Int) -> Bool {
        i + prefix.count <= text.count && text[i..<(i + prefix.count)].elementsEqual(prefix)
    }

    /// Tokens of a script, or of a template's `${…}` up to its closing brace, and the index after them.
    static func lex(_ text: [Unicode.Scalar], from start: Int = 0, untilBrace: Bool = false) throws(ScriptLexError)
        -> ([ScriptToken], Int) {
        var tokens: [ScriptToken] = []
        var i = start
        var depth = 0
        let n = text.count
        func slice(_ from: Int, _ to: Int) -> String { String(String.UnicodeScalarView(text[from..<to])) }
        while i < n {
            let c = text[i]
            if whitespace.contains(c) {
                i += 1
            } else if starts(text, ["/", "/"], at: i) {
                i = text[i...].firstIndex(of: "\n") ?? n
            } else if starts(text, ["/", "*"], at: i) {
                var j = i + 2
                while j + 1 < n, !(text[j] == "*" && text[j + 1] == "/") { j += 1 }
                i = j + 1 < n ? j + 2 : n
            } else if c == "'" || c == "\"" {
                let (value, j) = try string(text, at: i)
                tokens.append(ScriptToken(kind: .string, start: i, end: j, text: slice(i, j), value: .string(value)))
                i = j
            } else if c == "`" {
                let (value, j) = try template(text, at: i)
                tokens.append(ScriptToken(kind: .string, start: i, end: j, text: slice(i, j), value: value.map(JSONValue.string)))
                i = j
            } else if c == "/", regexAllowed(tokens), let j = regexEnd(text, at: i) {
                tokens.append(ScriptToken(kind: .regex, start: i, end: j, text: slice(i, j), value: nil))
                i = j
            } else if isIdentifierStart(c) {
                var j = i + 1
                while j < n, isIdentifierPart(text[j]) { j += 1 }
                tokens.append(ScriptToken(kind: .identifier, start: i, end: j, text: slice(i, j), value: nil))
                i = j
            } else if isDigit(c) {
                var j = i + 1
                while j < n {
                    let d = text[j]
                    if d.isASCII, Byte.isAlnum(UInt8(d.value)) || d == "_" || d == "." {
                        j += 1
                    } else if (d == "+" || d == "-"), text[j - 1] == "e" || text[j - 1] == "E" {
                        j += 1
                    } else {
                        break
                    }
                }
                let raw = slice(i, j)
                tokens.append(ScriptToken(kind: .number, start: i, end: j, text: raw, value: numberValue(raw)))
                i = j
            } else {
                if untilBrace {
                    if c == "{" {
                        depth += 1
                    } else if c == "}" {
                        if depth == 0 { return (tokens, i + 1) }
                        depth -= 1
                    }
                }
                tokens.append(ScriptToken(kind: .punctuator, start: i, end: i + 1, text: String(Character(c)), value: nil))
                i += 1
            }
        }
        if untilBrace { throw ScriptLexError() }
        return (tokens, i)
    }

    /// A valued number (`JS_NUMBER`): an integer literal when it is all digits, else a double.
    private static func numberValue(_ raw: String) -> JSONValue? {
        let bytes = Bytes(utf8: raw)
        guard number.fullMatches(bytes) else { return nil }
        if bytes.allSatisfy(Byte.isDigit) {
            let digits = bytes.drop { $0 == 0x30 }
            return .number(JSONLiteral(digits.isEmpty ? "0" : String(decoding: digits, as: UTF8.self)))
        }
        guard let double = Double(raw) else { return nil }
        return .number(JSONLiteral("\(double)"))
    }

    private static func regexAllowed(_ tokens: [ScriptToken]) -> Bool {
        guard let last = tokens.last else { return true }
        switch last.kind {
        case .number, .string, .regex: return false
        case .identifier: return regexAfter.contains(last.text)
        case .punctuator: return !(last.text == ")" || last.text == "]" || last.text == "}")
        }
    }

    private static func regexEnd(_ text: [Unicode.Scalar], at i: Int) -> Int? {
        var j = i + 1
        var inClass = false
        let n = text.count
        while j < n {
            let c = text[j]
            if c == "\n" || c == "\r" || c == "\u{2028}" || c == "\u{2029}" { return nil }
            if c == "\\" {
                j += 2
                continue
            }
            if c == "[" {
                inClass = true
            } else if c == "]" {
                inClass = false
            } else if c == "/", !inClass {
                j += 1
                while j < n, isIdentifierPart(text[j]) { j += 1 }
                return j
            }
            j += 1
        }
        return nil
    }

    /// One escape after the backslash at `i - 1`, appended as UTF-16-style units (surrogates combine later).
    private static func escape(_ text: [Unicode.Scalar], at i: Int, into out: inout [UInt32]) -> Int {
        for lineBreak in lineBreaks where starts(text, lineBreak, at: i) { return i + lineBreak.count }
        let c = text[i]
        switch c {
        case "n": out.append(0x0A); return i + 1
        case "r": out.append(0x0D); return i + 1
        case "t": out.append(0x09); return i + 1
        case "b": out.append(0x08); return i + 1
        case "f": out.append(0x0C); return i + 1
        case "v": out.append(0x0B); return i + 1
        default: break
        }
        if c == "0", !(i + 1 < text.count && isDigit(text[i + 1])) {
            out.append(0)
            return i + 1
        }
        if c == "x", let value = hex(text, from: i + 1, count: 2) {
            out.append(value)
            return i + 3
        }
        if c == "u" {
            if i + 1 < text.count, text[i + 1] == "{" {
                var j = i + 2
                while j < text.count, j - (i + 2) < 6, text[j].properties.isASCIIHexDigit { j += 1 }
                if j > i + 2, j < text.count, text[j] == "}", let value = hex(text, from: i + 2, count: j - i - 2),
                   value <= 0x10FFFF {
                    out.append(value)
                    return j + 1
                }
            }
            if let value = hex(text, from: i + 1, count: 4) {
                out.append(value)
                return i + 5
            }
        }
        out.append(c.value)
        return i + 1
    }

    private static func hex(_ text: [Unicode.Scalar], from start: Int, count: Int) -> UInt32? {
        guard start + count <= text.count else { return nil }
        var value: UInt32 = 0
        for scalar in text[start..<(start + count)] {
            guard scalar.properties.isASCIIHexDigit, let digit = UInt32(String(scalar), radix: 16) else { return nil }
            value = value << 4 | digit
        }
        return value
    }

    /// Decoded characters: surrogate pairs from escapes combined, lone surrogates read as U+FFFD.
    private static func join(_ units: [UInt32]) -> String {
        var scalars = String.UnicodeScalarView()
        var k = 0
        while k < units.count {
            let unit = units[k]
            if (0xD800...0xDBFF).contains(unit), k + 1 < units.count, (0xDC00...0xDFFF).contains(units[k + 1]) {
                scalars.append(Unicode.Scalar(0x10000 + ((unit - 0xD800) << 10) + (units[k + 1] - 0xDC00))!)
                k += 2
                continue
            }
            scalars.append(Unicode.Scalar(unit) ?? "\u{FFFD}")
            k += 1
        }
        return String(scalars)
    }

    private static func string(_ text: [Unicode.Scalar], at i: Int) throws(ScriptLexError) -> (String, Int) {
        let quote = text[i]
        var out: [UInt32] = []
        var j = i + 1
        let n = text.count
        while j < n {
            let c = text[j]
            if c == quote { return (join(out), j + 1) }
            if c == "\\", j + 1 < n {
                j = escape(text, at: j + 1, into: &out)
                continue
            }
            if c == "\n" || c == "\r" { break }
            out.append(c.value)
            j += 1
        }
        throw ScriptLexError()
    }

    private static func template(_ text: [Unicode.Scalar], at i: Int) throws(ScriptLexError) -> (String?, Int) {
        var out: [UInt32] = []
        var j = i + 1
        var opaque = false
        let n = text.count
        while j < n {
            let c = text[j]
            if c == "`" { return (opaque ? nil : join(out), j + 1) }
            if c == "\\", j + 1 < n {
                j = escape(text, at: j + 1, into: &out)
                continue
            }
            if c == "$", j + 1 < n, text[j + 1] == "{" {
                opaque = true
                j = try lex(text, from: j + 2, untilBrace: true).1
                continue
            }
            if c == "\r" {
                out.append(0x0A)
                j += j + 1 < n && text[j + 1] == "\n" ? 2 : 1
                continue
            }
            out.append(c.value)
            j += 1
        }
        throw ScriptLexError()
    }
}
