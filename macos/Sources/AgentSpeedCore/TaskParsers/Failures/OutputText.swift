import Foundation

/// The budget, cleaning and workspace prefixes of an output (TASK-PARSERS 5.1).
enum OutputText {
    static let headBytes = 65_536
    static let tailBytes = 196_608
    static let maxLine = 2_000

    /// All of the output, or a head and a tail cut on character and line boundaries.
    static func budget(_ text: Bytes) -> [Bytes] {
        guard text.count > headBytes + tailBytes else { return [text] }
        var cut = headBytes
        while cut > 0, !Byte.isLead(text[cut]) { cut -= 1 }
        var head = Bytes(text[0..<cut])
        var start = text.count - tailBytes
        while start < text.count, !Byte.isLead(text[start]) { start += 1 }
        var tail = Bytes(text[start...])
        if let newline = head.lastIndex(of: Byte.lf) { head = Bytes(head[...newline]) }
        if let newline = tail.firstIndex(of: Byte.lf) { tail = Bytes(tail[(newline + 1)...]) }
        return [head, tail]
    }

    private static let osc = Pattern(#"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)?"#)
    private static let csi = Pattern(#"\x1b\[[0-?]*[ -/]*[@-~]"#)
    private static let fe = Pattern(#"\x1b[@-Z\\-_]"#)

    /// The cleaned lines of one segment.
    static func cleanLines(_ segment: Bytes) -> [Bytes] {
        var text = segment
        if text.contains(0x1B) {
            text = osc.replacingAll(in: text, with: "")
            text = csi.replacingAll(in: text, with: "")
            text = fe.replacingAll(in: text, with: "")
        }
        text = text.lfNormalized()
        var cleaned = Bytes()
        cleaned.reserveCapacity(text.count)
        var index = 0
        while index < text.count {
            let byte = text[index]
            if byte < 0x20, byte != Byte.tab, byte != Byte.lf {
                index += 1
                continue
            }
            if byte == 0x7F {
                index += 1
                continue
            }
            if byte == 0xC2, index + 1 < text.count, text[index + 1] == 0x85 {
                cleaned.append(Byte.space)
                index += 2
                continue
            }
            if byte == 0xE2, index + 2 < text.count, text[index + 1] == 0x80,
               text[index + 2] == 0xA8 || text[index + 2] == 0xA9 {
                cleaned.append(Byte.space)
                index += 3
                continue
            }
            cleaned.append(byte)
            index += 1
        }
        let lines = cleaned.split(byte: Byte.lf).map { $0.trimmedRight().prefix(scalars: maxLine) }
        return stripWorkspacePrefixes(lines)
    }

    private static let workspacePrefix = Pattern(
        #"((?:@[A-Za-z0-9._-]+/|[A-Za-z0-9._-]*/)[A-Za-z0-9._/-]* [A-Za-z][A-Za-z0-9:._-]*|@?[A-Za-z0-9._/-]+:[A-Za-z][A-Za-z0-9._-]*): ?"#)
    static let workspaceMin = 3

    /// The prefix a line has: the first group of `WORKSPACE_PREFIX` at its start, followed by `:` and a space or the end.
    private static func prefix(_ line: Bytes) -> Bytes? {
        guard let match = workspacePrefix.match(line), let group = match[1] else { return nil }
        let after = group.count + 1
        guard after >= line.count || line[after] == Byte.space else { return nil }
        return group
    }

    /// Removes the `<package> <script>: ` prefixes that pnpm, yarn and turbo put on interleaved lines.
    static func stripWorkspacePrefixes(_ lines: [Bytes]) -> [Bytes] {
        var counts: [Bytes: Int] = [:]
        var prefixes: [Bytes?] = []
        prefixes.reserveCapacity(lines.count)
        for line in lines {
            let found = prefix(line)
            prefixes.append(found)
            if let found { counts[found, default: 0] += 1 }
        }
        let common = Set(counts.filter { $0.value >= workspaceMin }.keys)
        guard !common.isEmpty else { return lines }
        return zip(lines, prefixes).map { line, found in
            guard let found, common.contains(found) else { return line }
            var rest = line[(found.count + 1)...]
            if rest.first == Byte.space { rest = rest.dropFirst() }
            return Bytes(rest)
        }
    }
}

/// Masking volatile parts and normalising files (TASK-PARSERS 5.2).
enum OutputMasking {
    static let maxMessage = 200

    private static let notInPath = #"(?<![A-Za-z0-9._~/\\-])"#
    static let tmpPath = Pattern(notInPath + #"(?:(?:/private)?/var/folders/|(?:/private)?/tmp/|[A-Za-z]:[\\/]Users[\\/][^\\/ \t]+[\\/]AppData[\\/]Local[\\/]Temp[\\/])[^ \t'\"()\[\]\{\}<>,;|`]*"#)
    static let homePath = Pattern(notInPath + #"(?:/Users/[^/ \t'\"]+/|/home/[^/ \t'\"]+/|[A-Za-z]:[\\/]Users[\\/][^\\/ \t'\"]+[\\/])"#)
    private static let uuid = Pattern(#"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"#)
    private static let isoTime = Pattern(#"[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:[.,][0-9]+)?)?(?:Z|[+-][0-9]{2}:?[0-9]{2})?"#)
    private static let timeOfDay = Pattern(#"(?<![0-9])[0-9]{1,2}:[0-9]{2}:[0-9]{2}(?:[.,][0-9]+)?(?![0-9])"#)
    private static let date = Pattern(#"(?<![0-9])[0-9]{4}-[0-9]{2}-[0-9]{2}(?![0-9])"#)
    private static let hexAddress = Pattern(#"0x[0-9A-Fa-f]+"#)
    private static let filePosition = Pattern(#"(\.[A-Za-z][A-Za-z0-9+]{0,9}):[0-9]+(?::[0-9]+)?"#)
    private static let filePositionParen = Pattern(#"(\.[A-Za-z][A-Za-z0-9+]{0,9})\([0-9]+(?:,[0-9]+)?\)"#)
    private static let filePositionBracket = Pattern(#"(\.[A-Za-z][A-Za-z0-9+]{0,9}):\[[0-9]+,[0-9]+\]"#)
    private static let lineWord = Pattern(#"(?<![A-Za-z0-9_])(line|Line|LINE|lines|col|column|Column|col\.)( ?)[0-9]+(?![0-9])"#)
    private static let duration = Pattern(#"(?<![A-Za-z0-9_.])[0-9]+(?:\.[0-9]+)? ?(?:ns|us|µs|ms|s|sec|secs|seconds|second|m|min|mins|minutes|minute|h|hr|hrs|hours|hour)(?![A-Za-z0-9_])"#)
    private static let longHex = Pattern(#"(?<![A-Za-z0-9_])[0-9A-Fa-f]{8,}(?![A-Za-z0-9_])"#)
    private static let pidPort = Pattern(#"(?<![A-Za-z0-9_])(pid|PID|port)([ =:#]+)[0-9]+"#)
    private static let spaces = Pattern(#"[ \t]+"#)

    private static let tmpTemplate = PatternTemplate("<tmp>")
    private static let homeTemplate = PatternTemplate("~/")
    private static let substitutions: [(Pattern, PatternTemplate)] = [
        (uuid, PatternTemplate("<uuid>")),
        (isoTime, PatternTemplate("<time>")),
        (timeOfDay, PatternTemplate("<time>")),
        (date, PatternTemplate("<date>")),
        (hexAddress, PatternTemplate("0x?")),
        (filePositionBracket, PatternTemplate(#"\1:[N]"#)),
        (filePosition, PatternTemplate(#"\1:N"#)),
        (filePositionParen, PatternTemplate(#"\1(N)"#)),
        (lineWord, PatternTemplate(#"\1\2N"#)),
        (duration, PatternTemplate("<dur>")),
        (longHex, PatternTemplate("<hex>")),
        (pidPort, PatternTemplate(#"\1 N"#)),
    ]
    private static let space = PatternTemplate(" ")

    /// Every `cwd/` that does not follow a path character removed; `/` and `\` both match its separators, and a drive
    /// cwd compares ASCII case-insensitively.
    static func stripCwd(_ text: Bytes, cwd: String?) -> Bytes {
        guard let c = TaskPaths.normalizedCwd(cwd), text.count > c.count else { return text }
        let fold = TaskPaths.isDrive(c)
        var out = Bytes()
        var copied = 0
        var i = 0
        while i + c.count < text.count {
            if matchesCwd(text, at: i, c, fold: fold), i == 0 || !isPathCharacter(text[i - 1]) {
                out.append(contentsOf: text[copied..<i])
                i += c.count + 1
                copied = i
                continue
            }
            i += 1
        }
        guard copied > 0 else { return text }
        out.append(contentsOf: text[copied...])
        return out
    }

    @inline(__always) private static func isPathCharacter(_ byte: UInt8) -> Bool {
        Byte.isAlnum(byte) || byte == Byte.dot || byte == 0x5F || byte == 0x7E || byte == Byte.slash
            || byte == Byte.backslash || byte == Byte.dash
    }

    private static func matchesCwd(_ text: Bytes, at start: Int, _ c: Bytes, fold: Bool) -> Bool {
        for (offset, expected) in c.enumerated() {
            let byte = text[start + offset]
            if expected == Byte.slash {
                if byte != Byte.slash, byte != Byte.backslash { return false }
            } else if fold, Byte.isLetter(expected) {
                if byte | 0x20 != expected | 0x20 || !Byte.isLetter(byte) { return false }
            } else if byte != expected {
                return false
            }
        }
        let separator = text[start + c.count]
        return separator == Byte.slash || separator == Byte.backslash
    }

    private static func collapse(_ text: Bytes) -> Bytes {
        spaces.replacingAll(in: text, with: space).trimmed([Byte.space]).prefix(scalars: maxMessage)
    }

    /// A masked message: paths relative, volatile parts replaced, spaces collapsed, at most 200 characters.
    static func mask(_ text: Bytes, cwd: String?) -> Bytes {
        var t = tmpPath.replacingAll(in: text, with: tmpTemplate)
        t = stripCwd(t, cwd: cwd)
        t = homePath.replacingAll(in: t, with: homeTemplate)
        for (pattern, template) in substitutions { t = pattern.replacingAll(in: t, with: template) }
        return collapse(t)
    }

    /// A test name keeps its text: only paths are made relative and spaces collapsed.
    static func maskName(_ text: Bytes, cwd: String?) -> Bytes {
        var t = tmpPath.replacingAll(in: text, with: tmpTemplate)
        t = stripCwd(t, cwd: cwd)
        t = homePath.replacingAll(in: t, with: homeTemplate)
        return collapse(t)
    }

    private static let quotes: Bytes = [Byte.apostrophe, Byte.quote, Byte.backtick]

    static func normalizedFile(_ path: Bytes, cwd: String?) -> Bytes {
        var p = path.trimmed().trimmed(quotes)
        if p.hasPrefix(Bytes(utf8: "file://")) { p.removeFirst(7) }
        p = p.replacing(byte: Byte.backslash, with: Byte.slash)
        p = tmpPath.replacingAll(in: p, with: tmpTemplate)
        p = TaskPaths.cwdRelative(p, cwd: cwd).0
        p = homePath.replacingAll(in: p, with: homeTemplate)
        while p.contains(Bytes(utf8: "//")) { p = p.replacing(Bytes(utf8: "//"), with: [Byte.slash]) }
        while p.hasPrefix(Bytes(utf8: "./")) { p.removeFirst(2) }
        return p
    }
}
