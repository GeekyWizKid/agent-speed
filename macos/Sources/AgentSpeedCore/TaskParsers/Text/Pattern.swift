import Foundation

/// A regular expression of the subset TASK-PARSERS 1 allows (*Patterns*), matched over UTF-8 with Python `re`
/// semantics: backtracking, leftmost-first alternation, greedy and lazy quantifiers, `.` matching any scalar but LF,
/// negated classes matching any other scalar (LF included), one-character lookarounds and Python's `$` (the end, or
/// before a final LF). Patterns are written verbatim from Appendix A and compiled once; matching never allocates
/// unless groups are read.
struct Pattern: Sendable {
    let source: String
    let groupCount: Int
    private let program: [Instruction]
    private let sets: [CharSet]
    /// The bytes a match can start with; nil when the pattern can match the empty text.
    private let firstBytes: ByteMask?

    init(_ source: String) {
        self.source = source
        var parser = PatternParser(source)
        let node = parser.parse()
        var compiler = PatternCompiler(sets: parser.sets)
        compiler.emit(node)
        compiler.program.append(.match)
        program = compiler.program
        sets = compiler.sets
        groupCount = parser.groupCount
        firstBytes = PatternCompiler.firstBytes(program, sets: sets)
    }

    /// The whole text matches (`\A(?:P)\z`).
    func fullMatch(_ text: Bytes) -> PatternMatch? {
        text.withUnsafeBufferPointer { buffer in
            guard admits(buffer, at: 0) else { return nil }
            return run(buffer, at: 0, full: true, captures: true).map { PatternMatch(subject: text, slots: $0) }
        }
    }

    func fullMatches(_ text: Bytes) -> Bool {
        text.withUnsafeBufferPointer { buffer in
            admits(buffer, at: 0) && run(buffer, at: 0, full: true, captures: false) != nil
        }
    }

    /// A match anchored at `offset` (Python `pattern.match(text, offset)`).
    func match(_ text: Bytes, at offset: Int = 0) -> PatternMatch? {
        text.withUnsafeBufferPointer { buffer in
            guard admits(buffer, at: offset) else { return nil }
            return run(buffer, at: offset, full: false, captures: true).map { PatternMatch(subject: text, slots: $0) }
        }
    }

    func matches(_ text: Bytes, at offset: Int = 0) -> Bool {
        text.withUnsafeBufferPointer { buffer in
            admits(buffer, at: offset) && run(buffer, at: offset, full: false, captures: false) != nil
        }
    }

    /// The first match anywhere from `offset` (Python `pattern.search`).
    func search(_ text: Bytes, from offset: Int = 0) -> PatternMatch? {
        text.withUnsafeBufferPointer { buffer in
            search(buffer, from: offset).map { PatternMatch(subject: text, slots: $0) }
        }
    }

    func occurs(in text: Bytes) -> Bool { search(text) != nil }

    /// Every non-overlapping match replaced by `template`, left to right (Python `pattern.sub`); the template's `\N`
    /// refers to group N (empty when it did not take part).
    func replacingAll(in text: Bytes, with template: PatternTemplate) -> Bytes {
        text.withUnsafeBufferPointer { buffer in
            var out: Bytes?
            var position = 0
            var copied = 0
            while position <= buffer.count, let slots = search(buffer, from: position) {
                if out == nil { out = Bytes(); out!.reserveCapacity(buffer.count) }
                out!.append(contentsOf: buffer[copied..<slots[0]])
                template.append(to: &out!, slots: slots, subject: buffer)
                copied = slots[1]
                if slots[1] == slots[0] {
                    guard slots[1] < buffer.count else { break }
                    position = slots[1] + PatternCompiler.scalarLength(buffer[slots[1]])
                } else {
                    position = slots[1]
                }
            }
            guard var result = out else { return text }
            result.append(contentsOf: buffer[copied..<buffer.count])
            return result
        }
    }

    func replacingAll(in text: Bytes, with template: String) -> Bytes {
        replacingAll(in: text, with: PatternTemplate(template))
    }

    // MARK: Matching

    @inline(__always) private func admits(_ buffer: UnsafeBufferPointer<UInt8>, at offset: Int) -> Bool {
        guard let mask = firstBytes else { return offset <= buffer.count }
        return offset < buffer.count && mask.contains(buffer[offset])
    }

    private func search(_ buffer: UnsafeBufferPointer<UInt8>, from offset: Int) -> [Int]? {
        var start = offset
        while start <= buffer.count {
            if admits(buffer, at: start), let slots = run(buffer, at: start, full: false, captures: true) {
                return slots
            }
            guard start < buffer.count else { break }
            start += 1
            while start < buffer.count, !Byte.isLead(buffer[start]) { start += 1 }
        }
        return nil
    }

    /// The backtracking machine. The stack holds pairs: a branch (program counter ≥ 0, text position) or a capture
    /// to restore (−2 − slot, old value).
    private func run(_ text: UnsafeBufferPointer<UInt8>, at start: Int, full: Bool, captures: Bool) -> [Int]? {
        let end = text.count
        var slots = captures ? [Int](repeating: -1, count: 2 * (groupCount + 1)) : []
        var stack: [Int] = []
        var pc = 0
        var sp = start
        return program.withUnsafeBufferPointer { program in
            sets.withUnsafeBufferPointer { sets in
                while true {
                    var advanced = true
                    switch program[pc] {
                    case .byte(let byte):
                        if sp < end, text[sp] == byte {
                            sp += 1
                            pc += 1
                        } else {
                            advanced = false
                        }
                    case .any:
                        if sp < end, text[sp] != Byte.lf {
                            sp += PatternCompiler.scalarLength(text[sp])
                            pc += 1
                        } else {
                            advanced = false
                        }
                    case .set(let index):
                        if sp < end, let length = sets[index].match(text, at: sp) {
                            sp += length
                            pc += 1
                        } else {
                            advanced = false
                        }
                    case .split(let first, let second):
                        stack.append(second)
                        stack.append(sp)
                        pc = first
                    case .jump(let target):
                        pc = target
                    case .save(let slot):
                        if captures {
                            stack.append(-2 - slot)
                            stack.append(slots[slot])
                            slots[slot] = sp
                        }
                        pc += 1
                    case .behind(let index, let negated):
                        var hit = false
                        if sp > 0 {
                            var lead = sp - 1
                            while lead > 0, !Byte.isLead(text[lead]) { lead -= 1 }
                            hit = sets[index].match(text, at: lead) != nil
                        }
                        if hit != negated { pc += 1 } else { advanced = false }
                    case .ahead(let index, let negated):
                        let hit = sp < end && sets[index].match(text, at: sp) != nil
                        if hit != negated { pc += 1 } else { advanced = false }
                    case .dollar:
                        if sp == end || (sp == end - 1 && text[sp] == Byte.lf) { pc += 1 } else { advanced = false }
                    case .match:
                        if !full || sp == end {
                            if captures {
                                slots[0] = start
                                slots[1] = sp
                            }
                            return slots
                        }
                        advanced = false
                    }
                    if !advanced {
                        while true {
                            guard let value = stack.popLast() else { return nil }
                            let tag = stack.removeLast()
                            if tag >= 0 {
                                pc = tag
                                sp = value
                                break
                            }
                            slots[-2 - tag] = value
                        }
                    }
                }
            }
        }
    }
}

/// The groups of one match; group 0 is the whole match.
struct PatternMatch {
    let subject: Bytes
    let slots: [Int]

    var start: Int { slots[0] }
    var end: Int { slots[1] }

    /// The text of a group, or nil when it did not take part.
    subscript(_ group: Int) -> Bytes? {
        let from = slots[2 * group]
        let to = slots[2 * group + 1]
        guard from >= 0, to >= from else { return nil }
        return Bytes(subject[from..<to])
    }

    /// The text of a group, empty when it did not take part.
    func text(_ group: Int) -> Bytes { self[group] ?? [] }
}

/// A replacement for `Pattern.replacingAll`: literal text and `\N` group references.
struct PatternTemplate: Sendable {
    private enum Part: Sendable {
        case literal(Bytes)
        case group(Int)
    }

    private let parts: [Part]

    init(_ template: String) {
        var parts: [Part] = []
        var literal = Bytes()
        let bytes = Bytes(utf8: template)
        var index = 0
        while index < bytes.count {
            if bytes[index] == Byte.backslash, index + 1 < bytes.count, Byte.isDigit(bytes[index + 1]) {
                if !literal.isEmpty { parts.append(.literal(literal)) }
                literal = []
                parts.append(.group(Int(bytes[index + 1] - 0x30)))
                index += 2
                continue
            }
            literal.append(bytes[index])
            index += 1
        }
        if !literal.isEmpty { parts.append(.literal(literal)) }
        self.parts = parts
    }

    fileprivate func append(to out: inout Bytes, slots: [Int], subject: UnsafeBufferPointer<UInt8>) {
        for part in parts {
            switch part {
            case .literal(let text):
                out.append(contentsOf: text)
            case .group(let group):
                let from = slots[2 * group]
                let to = slots[2 * group + 1]
                if from >= 0, to >= from { out.append(contentsOf: subject[from..<to]) }
            }
        }
    }
}

// MARK: - Compilation

private enum Instruction: Sendable {
    case byte(UInt8)
    case any
    case set(Int)
    case split(Int, Int)
    case jump(Int)
    case save(Int)
    case behind(Int, negated: Bool)
    case ahead(Int, negated: Bool)
    case dollar
    case match
}

/// A set of scalars: an ASCII bitmap and ranges above it, possibly negated.
struct CharSet: Sendable {
    var low: UInt64 = 0
    var high: UInt64 = 0
    var ranges: [ClosedRange<UInt32>] = []
    var negated = false

    mutating func insert(_ range: ClosedRange<UInt32>) {
        var value = range.lowerBound
        while value <= min(range.upperBound, 127) {
            if value < 64 { low |= 1 << UInt64(value) } else { high |= 1 << UInt64(value - 64) }
            value += 1
        }
        if range.upperBound >= 128 { ranges.append(max(range.lowerBound, 128)...range.upperBound) }
    }

    @inline(__always) func contains(_ scalar: UInt32) -> Bool {
        let member: Bool
        if scalar < 64 {
            member = low >> UInt64(scalar) & 1 == 1
        } else if scalar < 128 {
            member = high >> UInt64(scalar - 64) & 1 == 1
        } else {
            member = ranges.contains { $0.contains(scalar) }
        }
        return member != negated
    }

    /// The byte length of the scalar at `offset` when the set holds it.
    @inline(__always) func match(_ text: UnsafeBufferPointer<UInt8>, at offset: Int) -> Int? {
        let lead = text[offset]
        if lead < 0x80 { return contains(UInt32(lead)) ? 1 : nil }
        let length = PatternCompiler.scalarLength(lead)
        var scalar = UInt32(lead) & (length == 2 ? 0x1F : length == 3 ? 0x0F : 0x07)
        var index = offset + 1
        while index < offset + length, index < text.count {
            scalar = scalar << 6 | UInt32(text[index] & 0x3F)
            index += 1
        }
        return contains(scalar) ? length : nil
    }
}

struct ByteMask: Sendable {
    var words: (UInt64, UInt64, UInt64, UInt64) = (0, 0, 0, 0)

    mutating func insert(_ byte: UInt8) {
        let bit = UInt64(1) << UInt64(byte & 63)
        switch byte >> 6 {
        case 0: words.0 |= bit
        case 1: words.1 |= bit
        case 2: words.2 |= bit
        default: words.3 |= bit
        }
    }

    @inline(__always) func contains(_ byte: UInt8) -> Bool {
        let bit = UInt64(1) << UInt64(byte & 63)
        switch byte >> 6 {
        case 0: return words.0 & bit != 0
        case 1: return words.1 & bit != 0
        case 2: return words.2 & bit != 0
        default: return words.3 & bit != 0
        }
    }
}

private indirect enum Node {
    case empty
    case literal(Unicode.Scalar)
    case any
    case set(Int)
    case group(Node, capture: Int?)
    case concat([Node])
    case alternation([Node])
    case repetition(Node, min: Int, max: Int?, greedy: Bool)
    case behind(Int, negated: Bool)
    case ahead(Int, negated: Bool)
    case dollar

    var nullable: Bool {
        switch self {
        case .empty, .behind, .ahead, .dollar: return true
        case .literal, .any, .set: return false
        case .group(let node, _): return node.nullable
        case .concat(let nodes): return nodes.allSatisfy(\.nullable)
        case .alternation(let nodes): return nodes.contains(where: \.nullable)
        case .repetition(let node, let min, _, _): return min == 0 || node.nullable
        }
    }
}

private struct PatternParser {
    let scalars: [Unicode.Scalar]
    let source: String
    var index = 0
    var groupCount = 0
    var sets: [CharSet] = []

    init(_ source: String) {
        self.source = source
        scalars = Array(source.unicodeScalars)
    }

    mutating func parse() -> Node {
        let node = alternation()
        precondition(index == scalars.count, "unbalanced pattern: \(source)")
        return node
    }

    private var current: Unicode.Scalar? { index < scalars.count ? scalars[index] : nil }

    private mutating func alternation() -> Node {
        var branches = [sequence()]
        while current == "|" {
            index += 1
            branches.append(sequence())
        }
        return branches.count == 1 ? branches[0] : .alternation(branches)
    }

    private mutating func sequence() -> Node {
        var items: [Node] = []
        while let scalar = current, scalar != "|", scalar != ")" {
            var atom = self.atom()
            while let quantified = quantifier(atom) { atom = quantified }
            items.append(atom)
        }
        if items.isEmpty { return .empty }
        return items.count == 1 ? items[0] : .concat(items)
    }

    private mutating func atom() -> Node {
        let scalar = scalars[index]
        index += 1
        switch scalar {
        case ".":
            return .any
        case "$":
            return .dollar
        case "^":
            preconditionFailure("`^` is not used: \(source)")
        case "[":
            sets.append(characterClass())
            return .set(sets.count - 1)
        case "(":
            return group()
        case "\\":
            return .literal(escape())
        default:
            return .literal(scalar)
        }
    }

    private mutating func group() -> Node {
        if current == "?" {
            index += 1
            let kind = scalars[index]
            index += 1
            switch kind {
            case ":":
                let inner = alternation()
                close()
                return .group(inner, capture: nil)
            case "!", "=":
                return lookaround(ahead: true, negated: kind == "!")
            case "<":
                let sign = scalars[index]
                index += 1
                return lookaround(ahead: false, negated: sign == "!")
            default:
                preconditionFailure("unsupported group in \(source)")
            }
        }
        groupCount += 1
        let number = groupCount
        let inner = alternation()
        close()
        return .group(inner, capture: number)
    }

    /// Lookarounds are one character wide (TASK-PARSERS 1).
    private mutating func lookaround(ahead: Bool, negated: Bool) -> Node {
        let inner = alternation()
        close()
        let set: Int
        switch inner {
        case .set(let existing):
            set = existing
        case .literal(let scalar):
            var single = CharSet()
            single.insert(scalar.value...scalar.value)
            sets.append(single)
            set = sets.count - 1
        default:
            preconditionFailure("lookarounds must be one character wide: \(source)")
        }
        return ahead ? .ahead(set, negated: negated) : .behind(set, negated: negated)
    }

    private mutating func close() {
        precondition(current == ")", "unbalanced group in \(source)")
        index += 1
    }

    private mutating func quantifier(_ atom: Node) -> Node? {
        guard let scalar = current else { return nil }
        var minimum = 0
        var maximum: Int?
        switch scalar {
        case "*":
            index += 1
        case "+":
            minimum = 1
            index += 1
        case "?":
            maximum = 1
            index += 1
        case "{":
            guard let bounds = braces() else { return nil }
            (minimum, maximum) = bounds
        default:
            return nil
        }
        var greedy = true
        if current == "?" {
            greedy = false
            index += 1
        }
        if maximum == nil { precondition(!atom.nullable, "a repeated empty match is not supported: \(source)") }
        return .repetition(atom, min: minimum, max: maximum, greedy: greedy)
    }

    /// `{m}`, `{m,}`, `{m,n}` or `{,n}`; anything else leaves `{` a literal, as in Python.
    private mutating func braces() -> (Int, Int?)? {
        var probe = index + 1
        func digits() -> Int? {
            var value: Int?
            while probe < scalars.count, let digit = Int(String(scalars[probe])), scalars[probe].isASCII {
                value = (value ?? 0) * 10 + digit
                probe += 1
            }
            return value
        }
        let low = digits()
        var high: Int?
        var comma = false
        if probe < scalars.count, scalars[probe] == "," {
            comma = true
            probe += 1
            high = digits()
        }
        guard probe < scalars.count, scalars[probe] == "}", low != nil || high != nil else { return nil }
        index = probe + 1
        return (low ?? 0, comma ? high : low)
    }

    private mutating func escape() -> Unicode.Scalar {
        let scalar = scalars[index]
        index += 1
        switch scalar {
        case "t": return "\t"
        case "n": return "\n"
        case "r": return "\r"
        case "f": return "\u{0C}"
        case "v": return "\u{0B}"
        case "a": return "\u{07}"
        case "x": return hex(2)
        case "u": return hex(4)
        case "U": return hex(8)
        default:
            precondition(!(scalar.properties.isAlphabetic || ("0"..."9").contains(scalar)),
                         "escape \\\(scalar) is not allowed: \(source)")
            return scalar
        }
    }

    private mutating func hex(_ count: Int) -> Unicode.Scalar {
        let digits = String(String.UnicodeScalarView(scalars[index..<(index + count)]))
        index += count
        return Unicode.Scalar(UInt32(digits, radix: 16)!)!
    }

    private mutating func characterClass() -> CharSet {
        var set = CharSet()
        if current == "^" {
            set.negated = true
            index += 1
        }
        var first = true
        while let scalar = current, scalar != "]" || first {
            first = false
            let low = classMember()
            if current == "-", index + 1 < scalars.count, scalars[index + 1] != "]" {
                index += 1
                let high = classMember()
                set.insert(low.value...high.value)
            } else {
                set.insert(low.value...low.value)
            }
            _ = scalar
        }
        precondition(current == "]", "unterminated class in \(source)")
        index += 1
        return set
    }

    private mutating func classMember() -> Unicode.Scalar {
        let scalar = scalars[index]
        index += 1
        return scalar == "\\" ? escape() : scalar
    }
}

private struct PatternCompiler {
    var program: [Instruction] = []
    var sets: [CharSet]

    init(sets: [CharSet]) {
        self.sets = sets
    }

    mutating func emit(_ node: Node) {
        switch node {
        case .empty:
            break
        case .literal(let scalar):
            for byte in String(Character(scalar)).utf8 { program.append(.byte(byte)) }
        case .any:
            program.append(.any)
        case .set(let index):
            program.append(.set(index))
        case .group(let inner, let capture):
            if let capture { program.append(.save(2 * capture)) }
            emit(inner)
            if let capture { program.append(.save(2 * capture + 1)) }
        case .concat(let nodes):
            nodes.forEach { emit($0) }
        case .alternation(let branches):
            var jumps: [Int] = []
            for (number, branch) in branches.enumerated() {
                if number < branches.count - 1 {
                    let split = program.count
                    program.append(.split(split + 1, -1))
                    emit(branch)
                    jumps.append(program.count)
                    program.append(.jump(-1))
                    program[split] = .split(split + 1, program.count)
                } else {
                    emit(branch)
                }
            }
            for jump in jumps { program[jump] = .jump(program.count) }
        case .repetition(let inner, let minimum, let maximum, let greedy):
            for _ in 0..<minimum { emit(inner) }
            if let maximum {
                var splits: [Int] = []
                for _ in minimum..<maximum {
                    splits.append(program.count)
                    program.append(.split(-1, -1))
                    emit(inner)
                }
                let out = program.count
                for split in splits {
                    program[split] = greedy ? .split(split + 1, out) : .split(out, split + 1)
                }
            } else {
                let loop = program.count
                program.append(.split(-1, -1))
                emit(inner)
                program.append(.jump(loop))
                let out = program.count
                program[loop] = greedy ? .split(loop + 1, out) : .split(out, loop + 1)
            }
        case .behind(let index, let negated):
            program.append(.behind(index, negated: negated))
        case .ahead(let index, let negated):
            program.append(.ahead(index, negated: negated))
        case .dollar:
            program.append(.dollar)
        }
    }

    @inline(__always) static func scalarLength(_ lead: UInt8) -> Int {
        lead < 0x80 ? 1 : lead < 0xE0 ? 2 : lead < 0xF0 ? 3 : 4
    }

    /// The bytes that can start a match, or nil when the empty text can match.
    static func firstBytes(_ program: [Instruction], sets: [CharSet]) -> ByteMask? {
        var mask = ByteMask()
        var seen = Set<Int>()
        var pending = [0]
        while let pc = pending.popLast() {
            guard seen.insert(pc).inserted else { continue }
            switch program[pc] {
            case .byte(let byte):
                mask.insert(byte)
            case .any:
                for byte in 0...255 where byte != Int(Byte.lf) { mask.insert(UInt8(byte)) }
            case .set(let index):
                let set = sets[index]
                for byte in 0..<128 where set.contains(UInt32(byte)) { mask.insert(UInt8(byte)) }
                if set.negated || !set.ranges.isEmpty {
                    for byte in 0xC0...0xFF { mask.insert(UInt8(byte)) }
                }
            case .split(let first, let second):
                pending.append(first)
                pending.append(second)
            case .jump(let target):
                pending.append(target)
            case .save, .behind, .ahead:
                pending.append(pc + 1)
            case .dollar, .match:
                return nil
            }
        }
        return mask
    }
}
