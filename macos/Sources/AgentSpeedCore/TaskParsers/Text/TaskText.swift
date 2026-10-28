import CryptoKit
import Foundation

/// UTF-8 text as the task parsers read it (TASK-PARSERS 1, *Text*). Comparisons, searches, sets and sorting are ordinal
/// over the bytes, which orders exactly like the scalar values; lengths and cuts that count characters count scalars.
/// Swift `String` comparison is not used for input text because it applies canonical equivalence.
typealias Bytes = [UInt8]

enum Byte {
    static let tab: UInt8 = 0x09
    static let lf: UInt8 = 0x0A
    static let cr: UInt8 = 0x0D
    static let space: UInt8 = 0x20
    static let quote: UInt8 = 0x22
    static let apostrophe: UInt8 = 0x27
    static let backslash: UInt8 = 0x5C
    static let slash: UInt8 = 0x2F
    static let dash: UInt8 = 0x2D
    static let dot: UInt8 = 0x2E
    static let colon: UInt8 = 0x3A
    static let equals: UInt8 = 0x3D
    static let backtick: UInt8 = 0x60

    @inline(__always) static func isBlank(_ byte: UInt8) -> Bool { byte == space || byte == tab }
    @inline(__always) static func isDigit(_ byte: UInt8) -> Bool { byte >= 0x30 && byte <= 0x39 }
    @inline(__always) static func isLetter(_ byte: UInt8) -> Bool { (byte | 0x20) >= 0x61 && (byte | 0x20) <= 0x7A }
    @inline(__always) static func isAlnum(_ byte: UInt8) -> Bool { isDigit(byte) || isLetter(byte) }
    /// The first byte of a scalar (not a UTF-8 continuation byte).
    @inline(__always) static func isLead(_ byte: UInt8) -> Bool { byte & 0xC0 != 0x80 }
}

extension Array where Element == UInt8 {
    init(utf8 text: String) {
        self = Array(text.utf8)
    }

    var string: String { String(decoding: self, as: UTF8.self) }

    func hasPrefix(_ prefix: Bytes) -> Bool {
        count >= prefix.count && self[0..<prefix.count].elementsEqual(prefix)
    }

    func hasSuffix(_ suffix: Bytes) -> Bool {
        count >= suffix.count && self[(count - suffix.count)...].elementsEqual(suffix)
    }

    func hasPrefix(_ prefix: Bytes, at offset: Int) -> Bool {
        offset >= 0 && count - offset >= prefix.count && self[offset..<(offset + prefix.count)].elementsEqual(prefix)
    }

    /// Without leading and trailing spaces and tabs (Python `strip(" \t")`).
    func trimmed() -> Bytes {
        var start = 0
        var end = count
        while start < end, Byte.isBlank(self[start]) { start += 1 }
        while end > start, Byte.isBlank(self[end - 1]) { end -= 1 }
        return start == 0 && end == count ? self : Bytes(self[start..<end])
    }

    func trimmedRight() -> Bytes {
        var end = count
        while end > 0, Byte.isBlank(self[end - 1]) { end -= 1 }
        return end == count ? self : Bytes(self[0..<end])
    }

    /// Without the given ASCII bytes at either end, paired or not (Python `strip(chars)`).
    func trimmed(_ bytes: Bytes) -> Bytes {
        var start = 0
        var end = count
        while start < end, bytes.contains(self[start]) { start += 1 }
        while end > start, bytes.contains(self[end - 1]) { end -= 1 }
        return start == 0 && end == count ? self : Bytes(self[start..<end])
    }

    /// Without the given trailing byte, repeatedly (Python `rstrip(c)`).
    func trimmedRight(_ byte: UInt8) -> Bytes {
        var end = count
        while end > 0, self[end - 1] == byte { end -= 1 }
        return end == count ? self : Bytes(self[0..<end])
    }

    /// The number of leading bytes equal to one of `bytes` (ASCII only).
    func leadingCount(of bytes: Bytes) -> Int {
        var index = 0
        while index < count, bytes.contains(self[index]) { index += 1 }
        return index
    }

    /// The index of the first `byte` at or after `start` (`memchr`), or nil.
    func firstIndex(ofByte byte: UInt8, from start: Int = 0) -> Int? {
        guard start < count else { return nil }
        return withUnsafeBufferPointer { buffer in
            guard let base = buffer.baseAddress,
                  let found = memchr(base + start, Int32(byte), count - start) else { return nil }
            return base.distance(to: found.assumingMemoryBound(to: UInt8.self))
        }
    }

    /// CR LF, then any remaining CR, replaced by LF (TASK-PARSERS 1, *LF-normalised*).
    func lfNormalized() -> Bytes {
        guard firstIndex(ofByte: Byte.cr) != nil else { return self }
        var out = Bytes()
        out.reserveCapacity(count)
        var index = 0
        while index < count {
            let byte = self[index]
            if byte == Byte.cr {
                out.append(Byte.lf)
                if index + 1 < count, self[index + 1] == Byte.lf { index += 1 }
            } else {
                out.append(byte)
            }
            index += 1
        }
        return out
    }

    /// Every part between separators, empty parts included (Python `str.split(sep)`).
    func split(byte separator: UInt8) -> [Bytes] {
        var parts: [Bytes] = []
        var start = 0
        while let index = firstIndex(ofByte: separator, from: start) {
            parts.append(Bytes(self[start..<index]))
            start = index + 1
        }
        parts.append(Bytes(self[start..<count]))
        return parts
    }

    /// Every part between occurrences of `separator` (Python `str.split(sep)` for a longer separator).
    func split(_ separator: Bytes) -> [Bytes] {
        var parts: [Bytes] = []
        var start = 0
        var index = 0
        while let found = firstRange(of: separator, from: index) {
            parts.append(Bytes(self[start..<found]))
            start = found + separator.count
            index = start
        }
        parts.append(Bytes(self[start..<count]))
        return parts
    }

    /// The part before the first separator (Python `str.split(sep, 1)[0]`).
    func firstPart(byte separator: UInt8) -> Bytes {
        guard let index = firstIndex(ofByte: separator) else { return self }
        return Bytes(self[0..<index])
    }

    /// The part after the last separator (Python `str.split(sep)[-1]`).
    func lastPart(byte separator: UInt8) -> Bytes {
        guard let index = lastIndex(of: separator) else { return self }
        return Bytes(self[(index + 1)...])
    }

    func firstRange(of needle: Bytes, from start: Int = 0) -> Int? {
        guard let first = needle.first else { return start <= count ? start : nil }
        var index = start
        let last = count - needle.count
        while index <= last {
            if self[index] == first, hasPrefix(needle, at: index) { return index }
            index += 1
        }
        return nil
    }

    func contains(_ needle: Bytes) -> Bool { firstRange(of: needle) != nil }

    /// Every occurrence of `target` replaced, left to right (Python `str.replace`).
    func replacing(_ target: Bytes, with replacement: Bytes) -> Bytes {
        guard var found = firstRange(of: target) else { return self }
        var out = Bytes()
        out.reserveCapacity(count)
        var start = 0
        while true {
            out.append(contentsOf: self[start..<found])
            out.append(contentsOf: replacement)
            start = found + target.count
            guard let next = firstRange(of: target, from: start) else { break }
            found = next
        }
        out.append(contentsOf: self[start..<count])
        return out
    }

    func replacing(byte target: UInt8, with replacement: UInt8) -> Bytes {
        guard contains(target) else { return self }
        return map { $0 == target ? replacement : $0 }
    }

    func lowercasedASCII() -> Bytes {
        map { $0 >= 0x41 && $0 <= 0x5A ? $0 | 0x20 : $0 }
    }

    func uppercasedASCII() -> Bytes {
        map { $0 >= 0x61 && $0 <= 0x7A ? $0 & ~0x20 : $0 }
    }

    /// The number of scalars.
    var scalarCount: Int {
        var total = 0
        for byte in self where Byte.isLead(byte) { total += 1 }
        return total
    }

    /// The first `limit` scalars (Python `text[:limit]`).
    func prefix(scalars limit: Int) -> Bytes {
        guard count > limit else { return self }
        var seen = 0
        for index in indices where Byte.isLead(self[index]) {
            if seen == limit { return Bytes(self[0..<index]) }
            seen += 1
        }
        return self
    }

    /// Ordinal order (by scalar values).
    func ordinalLess(_ other: Bytes) -> Bool {
        lexicographicallyPrecedes(other)
    }

    /// Joined with a separator.
    static func joined(_ parts: [Bytes], separator: Bytes) -> Bytes {
        var out = Bytes()
        for (index, part) in parts.enumerated() {
            if index > 0 { out.append(contentsOf: separator) }
            out.append(contentsOf: part)
        }
        return out
    }
}

enum TaskText {
    /// `sha16`: the first 16 lowercase hex digits of SHA-256 over the UTF-8 bytes (TASK-PARSERS 1, *Digests*).
    static func sha16(_ bytes: Bytes) -> String {
        let digits = Array("0123456789abcdef".utf8)
        var hex = Bytes()
        hex.reserveCapacity(16)
        for byte in SHA256.hash(data: bytes).prefix(8) {
            hex.append(digits[Int(byte >> 4)])
            hex.append(digits[Int(byte & 0x0F)])
        }
        return String(decoding: hex, as: UTF8.self)
    }

    static func sha16(_ text: String) -> String { sha16(Bytes(utf8: text)) }

    static let emptyDigest = sha16(Bytes())

    static let maxNumber: Int64 = 999_999_999_999_999_999

    /// A number read from text: a run of digits with an optional sign; more than 18 significant digits read as the cap
    /// (TASK-PARSERS 1, *Text*).
    static func number(_ text: Bytes) -> Int64 {
        let negative = text.first == Byte.dash
        var start = 0
        while start < text.count, text[start] == Byte.dash || text[start] == 0x2B { start += 1 }
        while start < text.count, text[start] == 0x30 { start += 1 }
        let digits = text.count - start
        var value: Int64 = 0
        if digits > 18 {
            value = maxNumber
        } else {
            for byte in text[start...] { value = value * 10 + Int64(byte - 0x30) }
        }
        return negative ? -value : value
    }

    static func number(_ text: Bytes?) -> Int64? {
        text.map { number($0) }
    }

    /// Saturating sum, so pathological counts never trap.
    @inline(__always) static func add(_ a: Int64, _ b: Int64) -> Int64 {
        let (sum, overflow) = a.addingReportingOverflow(b)
        return overflow ? (b > 0 ? .max : .min) : sum
    }

    @inline(__always) static func subtract(_ a: Int64, _ b: Int64) -> Int64 {
        let (difference, overflow) = a.subtractingReportingOverflow(b)
        return overflow ? (b < 0 ? .max : .min) : difference
    }

    static func words(_ list: [String]) -> Set<Bytes> { Set(list.map { Bytes(utf8: $0) }) }

    static func list(_ list: [String]) -> [Bytes] { list.map { Bytes(utf8: $0) } }
}
