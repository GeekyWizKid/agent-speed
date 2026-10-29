import Foundation

/// Parses a transcript line into a `JSONValue` for the task composer, straight from the line's bytes. Same grammar and
/// results as `JSONParser` (strict RFC 8259, the last of repeated members, a lone surrogate escape as U+FFFD), with a
/// deeper nesting limit (tool inputs nest) and strings without escapes decoded in one step.
enum TaskJSON {
    static let maxDepth = 512

    static func parse(_ bytes: UnsafeRawBufferPointer) -> JSONValue? {
        var cursor = Cursor(bytes: bytes)
        cursor.skipWhitespace()
        guard let value = try? cursor.value(depth: 0) else { return nil }
        cursor.skipWhitespace()
        return cursor.index == bytes.count ? value : nil
    }

    private struct Failure: Error {}

    private struct Cursor {
        let bytes: UnsafeRawBufferPointer
        var index = 0

        init(bytes: UnsafeRawBufferPointer) {
            self.bytes = bytes
        }

        mutating func skipWhitespace() {
            while index < bytes.count {
                switch bytes[index] {
                case 0x20, 0x09, 0x0A, 0x0D: index += 1
                default: return
                }
            }
        }

        mutating func value(depth: Int) throws(Failure) -> JSONValue {
            guard index < bytes.count else { throw Failure() }
            switch bytes[index] {
            case UInt8(ascii: "{"): return try object(depth: depth + 1)
            case UInt8(ascii: "["): return try array(depth: depth + 1)
            case UInt8(ascii: "\""): return .string(try string())
            case UInt8(ascii: "t"):
                try literal("true")
                return .bool(true)
            case UInt8(ascii: "f"):
                try literal("false")
                return .bool(false)
            case UInt8(ascii: "n"):
                try literal("null")
                return .null
            default:
                return .number(try number())
            }
        }

        private mutating func object(depth: Int) throws(Failure) -> JSONValue {
            guard depth <= TaskJSON.maxDepth else { throw Failure() }
            index += 1
            var members: [String: JSONValue] = [:]
            skipWhitespace()
            if index < bytes.count, bytes[index] == UInt8(ascii: "}") {
                index += 1
                return .object(members)
            }
            while true {
                skipWhitespace()
                guard index < bytes.count, bytes[index] == UInt8(ascii: "\"") else { throw Failure() }
                let key = try string()
                skipWhitespace()
                guard index < bytes.count, bytes[index] == UInt8(ascii: ":") else { throw Failure() }
                index += 1
                skipWhitespace()
                members[key] = try value(depth: depth)
                skipWhitespace()
                guard index < bytes.count else { throw Failure() }
                if bytes[index] == UInt8(ascii: ",") {
                    index += 1
                } else if bytes[index] == UInt8(ascii: "}") {
                    index += 1
                    return .object(members)
                } else {
                    throw Failure()
                }
            }
        }

        private mutating func array(depth: Int) throws(Failure) -> JSONValue {
            guard depth <= TaskJSON.maxDepth else { throw Failure() }
            index += 1
            var items: [JSONValue] = []
            skipWhitespace()
            if index < bytes.count, bytes[index] == UInt8(ascii: "]") {
                index += 1
                return .array(items)
            }
            while true {
                skipWhitespace()
                items.append(try value(depth: depth))
                skipWhitespace()
                guard index < bytes.count else { throw Failure() }
                if bytes[index] == UInt8(ascii: ",") {
                    index += 1
                } else if bytes[index] == UInt8(ascii: "]") {
                    index += 1
                    return .array(items)
                } else {
                    throw Failure()
                }
            }
        }

        private mutating func literal(_ word: StaticString) throws(Failure) {
            let count = word.utf8CodeUnitCount
            guard index + count <= bytes.count else { throw Failure() }
            for offset in 0..<count where bytes[index + offset] != word.utf8Start[offset] { throw Failure() }
            index += count
        }

        private mutating func number() throws(Failure) -> JSONLiteral {
            let start = index
            if index < bytes.count, bytes[index] == UInt8(ascii: "-") { index += 1 }
            guard index < bytes.count, isDigit(bytes[index]) else { throw Failure() }
            if bytes[index] == UInt8(ascii: "0") {
                index += 1
            } else {
                while index < bytes.count, isDigit(bytes[index]) { index += 1 }
            }
            if index < bytes.count, bytes[index] == UInt8(ascii: ".") {
                index += 1
                guard index < bytes.count, isDigit(bytes[index]) else { throw Failure() }
                while index < bytes.count, isDigit(bytes[index]) { index += 1 }
            }
            if index < bytes.count, bytes[index] == UInt8(ascii: "e") || bytes[index] == UInt8(ascii: "E") {
                index += 1
                if index < bytes.count, bytes[index] == UInt8(ascii: "+") || bytes[index] == UInt8(ascii: "-") { index += 1 }
                guard index < bytes.count, isDigit(bytes[index]) else { throw Failure() }
                while index < bytes.count, isDigit(bytes[index]) { index += 1 }
            }
            return JSONLiteral(String(decoding: UnsafeRawBufferPointer(rebasing: bytes[start..<index]), as: UTF8.self))
        }

        /// Runs between escapes are validated and copied whole; a string without escapes is decoded straight from the
        /// line.
        private mutating func string() throws(Failure) -> String {
            index += 1
            var runStart = index
            var utf8: [UInt8]?
            while index < bytes.count {
                let byte = bytes[index]
                if byte == UInt8(ascii: "\"") {
                    let run = UnsafeRawBufferPointer(rebasing: bytes[runStart..<index])
                    index += 1
                    guard var assembled = utf8 else { return String(decoding: run, as: UTF8.self) }
                    assembled.append(contentsOf: run)
                    return String(decoding: assembled, as: UTF8.self)
                }
                if byte == UInt8(ascii: "\\") {
                    if utf8 == nil { utf8 = [] }
                    utf8!.append(contentsOf: UnsafeRawBufferPointer(rebasing: bytes[runStart..<index]))
                    index += 1
                    guard index < bytes.count else { throw Failure() }
                    let escape = bytes[index]
                    index += 1
                    switch escape {
                    case UInt8(ascii: "\""), UInt8(ascii: "\\"), UInt8(ascii: "/"): utf8!.append(escape)
                    case UInt8(ascii: "b"): utf8!.append(0x08)
                    case UInt8(ascii: "f"): utf8!.append(0x0C)
                    case UInt8(ascii: "n"): utf8!.append(0x0A)
                    case UInt8(ascii: "r"): utf8!.append(0x0D)
                    case UInt8(ascii: "t"): utf8!.append(0x09)
                    case UInt8(ascii: "u"): utf8!.append(contentsOf: String(Character(try unicodeEscape())).utf8)
                    default: throw Failure()
                    }
                    runStart = index
                    continue
                }
                if byte < 0x20 { throw Failure() }
                if byte >= 0x80 {
                    index += try utf8Sequence(at: index)
                    continue
                }
                index += 1
                index = skipPlainASCII(from: index)
            }
            throw Failure()
        }

        /// Skips 8 bytes at a time while they are all printable ASCII other than `"` and `\`.
        private func skipPlainASCII(from start: Int) -> Int {
            var index = start
            guard let base = bytes.baseAddress else { return index }
            let ones: UInt64 = 0x0101_0101_0101_0101
            let highs: UInt64 = 0x8080_8080_8080_8080
            while index + 8 <= bytes.count {
                let word = UInt64(littleEndian: (base + index).loadUnaligned(as: UInt64.self))
                let quote = word ^ (ones &* 0x22)
                let slash = word ^ (ones &* 0x5C)
                // A byte below 0x20, a zero after the XORs (a quote or backslash), or a high bit: stop.
                let special = ((word &- ones &* 0x20) | (quote &- ones) | (slash &- ones) | word) & highs
                guard special == 0 else { break }
                index += 8
            }
            return index
        }

        private mutating func unicodeEscape() throws(Failure) -> Unicode.Scalar {
            let high = try hex4()
            if (0xD800...0xDBFF).contains(high), index + 1 < bytes.count,
               bytes[index] == UInt8(ascii: "\\"), bytes[index + 1] == UInt8(ascii: "u") {
                let saved = index
                index += 2
                let low = try hex4()
                if (0xDC00...0xDFFF).contains(low) {
                    let value = 0x10000 + ((UInt32(high) - 0xD800) << 10) + (UInt32(low) - 0xDC00)
                    return Unicode.Scalar(value) ?? "\u{FFFD}"
                }
                index = saved
            }
            return Unicode.Scalar(UInt32(high)) ?? "\u{FFFD}"
        }

        private mutating func hex4() throws(Failure) -> UInt16 {
            guard index + 4 <= bytes.count else { throw Failure() }
            var value: UInt16 = 0
            for _ in 0..<4 {
                let byte = bytes[index]
                let digit: UInt8
                switch byte {
                case UInt8(ascii: "0")...UInt8(ascii: "9"): digit = byte - UInt8(ascii: "0")
                case UInt8(ascii: "a")...UInt8(ascii: "f"): digit = byte - UInt8(ascii: "a") + 10
                case UInt8(ascii: "A")...UInt8(ascii: "F"): digit = byte - UInt8(ascii: "A") + 10
                default: throw Failure()
                }
                value = value << 4 | UInt16(digit)
                index += 1
            }
            return value
        }

        /// Length of the valid UTF-8 sequence starting at `start`.
        private func utf8Sequence(at start: Int) throws(Failure) -> Int {
            let lead = bytes[start]
            let length: Int
            let minimum: UInt32
            switch lead {
            case 0xC2...0xDF: (length, minimum) = (2, 0x80)
            case 0xE0...0xEF: (length, minimum) = (3, 0x800)
            case 0xF0...0xF4: (length, minimum) = (4, 0x10000)
            default: throw Failure()
            }
            guard start + length <= bytes.count else { throw Failure() }
            var value = UInt32(lead) & (length == 2 ? 0x1F : length == 3 ? 0x0F : 0x07)
            for offset in 1..<length {
                let byte = bytes[start + offset]
                guard byte & 0xC0 == 0x80 else { throw Failure() }
                value = value << 6 | UInt32(byte & 0x3F)
            }
            guard value >= minimum, value <= 0x10FFFF, !(0xD800...0xDFFF).contains(value) else { throw Failure() }
            return length
        }

        private func isDigit(_ byte: UInt8) -> Bool {
            byte >= UInt8(ascii: "0") && byte <= UInt8(ascii: "9")
        }
    }
}

/// The top-level facts of a line the composer looks at before deciding to parse it.
struct TaskLinePeek {
    /// Index of `type` in the candidates, nil for any other or no type.
    var type: Int?
    var idIsString = false
    /// `timestamp` as METRICS 3.1 reads it.
    var timestampMs: Int64?

    static func read(_ bytes: UnsafeRawBufferPointer, types: [StaticString]) -> TaskLinePeek? {
        var scanner = JSONScanner(bytes)
        var peek = TaskLinePeek()
        do {
            try scanner.beginObject()
            while let key = try scanner.nextMember() {
                if scanner.key(key, is: "type") {
                    peek.type = try scanner.readStringMatch(types)
                } else if scanner.key(key, is: "id") {
                    peek.idIsString = scanner.nextIsString
                    try scanner.skipValue()
                } else if scanner.key(key, is: "timestamp") {
                    peek.timestampMs = try scanner.readTimestamp()
                } else {
                    try scanner.skipValue()
                }
            }
            try scanner.finish()
        } catch {
            return nil
        }
        return peek
    }
}
