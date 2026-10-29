import Foundation

/// The operators that split a command into segments (TASK-PARSERS 3.2); `|&` reads as `|` and `;;` as `;`.
enum ShellOperator: Equatable, Sendable {
    case semicolon
    case background
    case pipe
    case and
    case or
    case open
    case close

    var isParenthesis: Bool { self == .open || self == .close }
}

enum ShellItem: Equatable {
    case word(Bytes)
    case op(ShellOperator)
    case redirection(operator: Bytes, target: Bytes)
    case heredoc(Bytes)
}

/// Shell tokens (TASK-PARSERS 3.2): words with quotes and escapes removed, operators, redirections and here-document
/// bodies. Every special character is ASCII, so the scan runs over UTF-8 bytes.
enum ShellTokenizer {
    static func tokenize(_ text: Bytes) -> [ShellItem] {
        var items: [ShellItem] = []
        var pending: [(delimiter: Bytes, stripTabs: Bool, index: Int)] = []
        var i = 0
        let n = text.count
        while i < n {
            let c = text[i]
            if c == Byte.space || c == Byte.tab {
                i += 1
                continue
            }
            if c == Byte.backslash, i + 1 < n, text[i + 1] == Byte.lf {
                i += 2
                continue
            }
            if c == Byte.lf {
                items.append(.op(.semicolon))
                i += 1
                for entry in pending {
                    let (body, next) = heredoc(text, from: i, delimiter: entry.delimiter, stripTabs: entry.stripTabs)
                    items[entry.index] = .heredoc(body)
                    i = next
                }
                pending = []
                continue
            }
            if c == 0x23 {
                i = text[i...].firstIndex(of: Byte.lf) ?? n
                continue
            }
            if i + 1 < n {
                let next = text[i + 1]
                let two: ShellOperator? = switch (c, next) {
                case (0x26, 0x26): .and
                case (0x7C, 0x7C): .or
                case (0x7C, 0x26): .pipe
                case (0x3B, 0x3B): .semicolon
                default: nil
                }
                if let two {
                    items.append(.op(two))
                    i += 2
                    continue
                }
            }
            if let (op, end) = redirection(text, at: i) {
                i = end
                while i < n, text[i] == Byte.space || text[i] == Byte.tab { i += 1 }
                let (word, next) = readWord(text, from: i)
                i = next
                if op == Bytes(utf8: "<<") || op == Bytes(utf8: "<<-") {
                    items.append(.heredoc([]))
                    pending.append((word, op == Bytes(utf8: "<<-"), items.count - 1))
                } else {
                    items.append(.redirection(operator: op, target: word))
                }
                continue
            }
            let single: ShellOperator? = switch c {
            case 0x3B: .semicolon
            case 0x26: .background
            case 0x7C: .pipe
            case 0x28: .open
            case 0x29: .close
            default: nil
            }
            if let single {
                items.append(.op(single))
                i += 1
                continue
            }
            let (word, next) = readWord(text, from: i)
            items.append(.word(word))
            i = next
        }
        for entry in pending { items[entry.index] = .heredoc([]) }
        return items
    }

    private static let redirectionOperators: [Bytes] = TaskText.list(
        ["<<<", "<<-", "<<", ">>", ">&", "<&", ">|", "<>", "&>>", "&>", ">", "<"])

    /// `REDIR` at `i`: optional digits, then an operator.
    private static func redirection(_ text: Bytes, at i: Int) -> (Bytes, Int)? {
        var j = i
        while j < text.count, Byte.isDigit(text[j]) { j += 1 }
        for op in redirectionOperators where text.hasPrefix(op, at: j) {
            return (op, j + op.count)
        }
        return nil
    }

    private static func heredoc(_ text: Bytes, from start: Int, delimiter: Bytes, stripTabs: Bool) -> (Bytes, Int) {
        var body = Bytes()
        var i = start
        let n = text.count
        while i < n {
            let end = text[i...].firstIndex(of: Byte.lf)
            let line = Bytes(text[i..<(end ?? n)])
            i = end.map { $0 + 1 } ?? n
            let compared = stripTabs ? Bytes(line[line.leadingCount(of: [Byte.tab])...]) : line
            if compared == delimiter { return (body, i) }
            body.append(contentsOf: line)
            body.append(Byte.lf)
        }
        return (body, n)
    }

    /// One word from `i`: quotes and escapes removed; `$(…)`, `${…}` and backquotes kept verbatim.
    static func readWord(_ text: Bytes, from start: Int) -> (Bytes, Int) {
        let n = text.count
        var out = Bytes()
        var i = start
        while i < n {
            let c = text[i]
            switch c {
            case Byte.space, Byte.tab, Byte.lf, 0x3B, 0x26, 0x7C, 0x28, 0x29, 0x3C, 0x3E:
                return (out, i)
            case Byte.backslash:
                if i + 1 < n {
                    if text[i + 1] != Byte.lf { out.append(text[i + 1]) }
                    i += 2
                } else {
                    i += 1
                }
            case Byte.apostrophe:
                let end = text[(i + 1)...].firstIndex(of: Byte.apostrophe) ?? n
                out.append(contentsOf: text[(i + 1)..<end])
                i = end + 1
            case 0x24 where i + 1 < n && text[i + 1] == Byte.apostrophe:
                var j = i + 2
                while j < n, text[j] != Byte.apostrophe {
                    if text[j] == Byte.backslash, j + 1 < n {
                        out.append(text[j + 1])
                        j += 2
                        continue
                    }
                    out.append(text[j])
                    j += 1
                }
                i = j + 1
            case Byte.quote:
                var j = i + 1
                while j < n, text[j] != Byte.quote {
                    if text[j] == Byte.backslash, j + 1 < n,
                       [Byte.quote, Byte.backslash, 0x24, Byte.backtick, Byte.lf].contains(text[j + 1]) {
                        if text[j + 1] != Byte.lf { out.append(text[j + 1]) }
                        j += 2
                        continue
                    }
                    out.append(text[j])
                    j += 1
                }
                i = j + 1
            case 0x24 where i + 1 < n && (text[i + 1] == 0x28 || text[i + 1] == 0x7B):
                let open = text[i + 1]
                let close: UInt8 = open == 0x28 ? 0x29 : 0x7D
                var depth = 0
                var j = i + 1
                while j < n {
                    if text[j] == open {
                        depth += 1
                    } else if text[j] == close {
                        depth -= 1
                        if depth == 0 { break }
                    }
                    j += 1
                }
                out.append(contentsOf: text[i..<min(j + 1, n)])
                i = j + 1
            case Byte.backtick:
                let end = text[(i + 1)...].firstIndex(of: Byte.backtick) ?? n
                out.append(contentsOf: text[i..<min(end + 1, n)])
                i = end + 1
            default:
                out.append(c)
                i += 1
            }
        }
        return (out, i)
    }
}
