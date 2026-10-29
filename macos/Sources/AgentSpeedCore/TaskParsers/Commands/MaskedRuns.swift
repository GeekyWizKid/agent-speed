import Foundation

/// Masked runs (TASK-PARSERS 3.10): a status the command prints itself, and grep error filters with no output.
enum MaskedRuns {
    private static let statusReference = Pattern(#"\$\?|\$\{\?\}|\$\{PIPESTATUS\[([0-9]+)\]\}|\$PIPESTATUS"#)
    private static let statusValue = Pattern(#"-?[0-9]+"#)
    private static let grepPrograms = TaskText.words(["grep", "egrep"])
    private static let grepFlags = Set(Bytes(utf8: "iEnwFPashHo"))
    private static let grepLongFlags = TaskText.words([
        "--ignore-case", "--extended-regexp", "--line-number", "--word-regexp", "--fixed-strings", "--perl-regexp",
        "--text", "--no-messages", "--no-filename", "--with-filename", "--only-matching",
    ])
    private static let grepValueOptions = TaskText.words([
        "-e", "-A", "-B", "-C", "-m", "--regexp", "--after-context", "--before-context", "--context", "--max-count",
    ])
    private static let grepAttached = TaskText.words(["-e", "-A", "-B", "-C", "-m"])
    private static let limiters = TaskText.words(["head", "tail"])
    private static let noOutput = Bytes(utf8: "(Bash completed with no output)")
    private static let pipestatusName = Bytes(utf8: "$PIPESTATUS")

    /// `masked_status` over a parsed command and its `command_info`.
    static func status(_ command: ShellCommand, info: TaskCommandInfo, output: Bytes, cwd: String?) -> TaskMaskedStatus? {
        guard info.masked else { return nil }
        let segments = command.segments
        let chosen = CommandAnalysis.choose(segments, cwd: cwd).1
        let last = output.lfNormalized().split(byte: Byte.lf).map { $0.trimmed() }.last { !$0.isEmpty }
        if let chosen, let last, let echo = statusEcho(command, chosen: chosen) {
            switch echo {
            case .printed(let prefix, let suffix):
                if let value = printedValue(last, prefix: prefix, suffix: suffix) {
                    return TaskMaskedStatus(exitCode: value, failed: value != 0)
                }
            case .branches(let ok, let fail):
                if last == ok.trimmed() { return TaskMaskedStatus(exitCode: 0, failed: false) }
                if let shown = failure(last, fail) { return shown }
            case .orElse(let fail):
                if let shown = failure(last, fail) { return shown }
            }
        }
        if info.errorFilter, isSilent(output) { return TaskMaskedStatus(exitCode: nil, failed: false) }
        return nil
    }

    private static func failure(_ last: Bytes, _ fail: EchoText) -> TaskMaskedStatus? {
        switch fail {
        case .fixed(let text):
            return last == text.trimmed() ? TaskMaskedStatus(exitCode: nil, failed: true) : nil
        case .status(let prefix, let suffix, _):
            guard let value = printedValue(last, prefix: prefix, suffix: suffix), value != 0 else { return nil }
            return TaskMaskedStatus(exitCode: value, failed: true)
        }
    }

    /// No output at all: nothing but JSON whitespace, or Claude Code's placeholder for it.
    static func isSilent(_ output: Bytes) -> Bool {
        var start = 0
        var end = output.count
        while start < end, TaskInput.jsonWhitespace(output[start]) { start += 1 }
        while end > start, TaskInput.jsonWhitespace(output[end - 1]) { end -= 1 }
        return start == end || output[start..<end].elementsEqual(noOutput)
    }

    // MARK: Pipelines

    private static func pipelineEnd(_ segments: [ShellSegment], _ start: Int) -> Int {
        var k = start
        while k + 1 < segments.count, segments[k].opAfter == .pipe { k += 1 }
        return k
    }

    private static func pipelineStart(_ segments: [ShellSegment], _ start: Int) -> Int {
        var k = start
        while k > 0, segments[k].opBefore == .pipe { k -= 1 }
        return k
    }

    /// The chosen segment pipes into a grep for error lines, then at most `head` / `tail`, ending the command.
    static func isErrorFilter(_ segments: [ShellSegment], chosen k: Int) -> Bool {
        let end = pipelineEnd(segments, k)
        guard end != k, end == segments.count - 1, segments[k].opAfter == .pipe else { return false }
        let words = segments[k + 1].words
        guard let first = words.first, grepPrograms.contains(ShellCommand.program(first)),
              let patterns = grepPatterns(words),
              patterns.contains(where: { $0.lowercasedASCII().contains(Bytes(utf8: "error")) }) else { return false }
        return (k + 2..<end + 1).allSatisfy { j in
            segments[j].words.first.map { limiters.contains(ShellCommand.program($0)) } ?? false
        }
    }

    /// The patterns of a grep that reads only its input, else nil.
    private static func grepPatterns(_ words: [Bytes]) -> [Bytes]? {
        var patterns: [Bytes] = []
        var positional: [Bytes] = []
        var i = 1
        while i < words.count {
            let word = words[i]
            if word == CommandOptions.dashDash {
                positional += words[(i + 1)...]
                break
            }
            let equals = word.firstIndex(of: Byte.equals)
            let name = equals.map { Bytes(word[..<$0]) } ?? word
            if grepValueOptions.contains(word)
                || (word.hasPrefix(CommandOptions.dashDash) && equals != nil && grepValueOptions.contains(name)) {
                let argument: Bytes
                if let equals {
                    argument = Bytes(word[(equals + 1)...])
                } else if i + 1 < words.count {
                    argument = words[i + 1]
                    i += 1
                } else {
                    return nil
                }
                if name == Bytes(utf8: "-e") || name == Bytes(utf8: "--regexp") { patterns.append(argument) }
            } else if word.count > 2, grepAttached.contains(Bytes(word[0..<2])), !word.hasPrefix(CommandOptions.dashDash) {
                if word[1] == 0x65 { patterns.append(Bytes(word[2...])) }
            } else if grepLongFlags.contains(word) || name == Bytes(utf8: "--color") || name == Bytes(utf8: "--colour") {
                // A flag that keeps the matching lines.
            } else if word.first == Byte.dash, word.count > 1, !word.hasPrefix(CommandOptions.dashDash) {
                if word.dropFirst().contains(where: { !grepFlags.contains($0) }) { return nil }
            } else if word.hasPrefix(CommandOptions.dashDash) {
                return nil
            } else {
                positional.append(word)
            }
            i += 1
        }
        if patterns.isEmpty {
            guard let first = positional.first else { return nil }
            patterns = [first]
            positional.removeFirst()
        }
        return positional.isEmpty ? patterns : nil
    }

    // MARK: Status echoes

    enum EchoText {
        case fixed(Bytes)
        /// The text around one status reference; the `PIPESTATUS` index when it is one.
        case status(prefix: Bytes, suffix: Bytes, index: Int?)

        var index: Int? {
            if case .status(_, _, let index) = self { return index }
            return nil
        }
    }

    private enum StatusEcho {
        case printed(prefix: Bytes, suffix: Bytes)
        case branches(ok: Bytes, fail: EchoText)
        case orElse(fail: EchoText)
    }

    /// What an `echo` segment prints (its words joined by spaces), or nil.
    private static func echoText(_ segment: ShellSegment) -> Bytes? {
        let words = segment.words
        guard words.count >= 2, ShellCommand.program(words[0]) == Bytes(utf8: "echo"), segment.redirections.isEmpty,
              segment.heredocs.isEmpty, words[1].first != Byte.dash else { return nil }
        return Bytes.joined(Array(words.dropFirst()), separator: [Byte.space])
    }

    private static func statusText(_ text: Bytes) -> EchoText? {
        var references: [PatternMatch] = []
        var position = 0
        while let match = statusReference.search(text, from: position) {
            references.append(match)
            if references.count > 1 { return nil }
            position = match.end
        }
        guard let reference = references.first else {
            if text.contains(0x24) || text.contains(Byte.backtick) || text.trimmed().isEmpty { return nil }
            return .fixed(text)
        }
        let prefix = Bytes(text[..<reference.start])
        let suffix = Bytes(text[reference.end...])
        if prefix.contains(0x24) || prefix.contains(Byte.backtick) || suffix.contains(0x24) || suffix.contains(Byte.backtick) {
            return nil
        }
        let index: Int?
        if reference.text(0) == pipestatusName {
            index = 0
        } else if let digits = reference[1] {
            let value = TaskText.number(digits)
            index = value > Int64(Int.max) ? Int.max : Int(value)
        } else {
            index = nil
        }
        return .status(prefix: prefix, suffix: suffix, index: index)
    }

    /// The status echo that reports the chosen segment's exit status, or nil.
    private static func statusEcho(_ command: ShellCommand, chosen k: Int) -> StatusEcho? {
        let segments = command.segments
        let end = pipelineEnd(segments, k)
        let owns = end == k || command.pipefail
        let after = Array(segments[(end + 1)...])
        var texts: [EchoText] = []
        for segment in after {
            guard let text = statusText(echoText(segment) ?? []) else { return nil }
            texts.append(text)
        }
        if after.count == 1, after[0].opBefore == .semicolon {
            guard case .status(let prefix, let suffix, let index) = texts[0] else { return nil }
            let counts = index.map { $0 == k - pipelineStart(segments, k) } ?? owns
            return counts ? .printed(prefix: prefix, suffix: suffix) : nil
        }
        if !owns || texts.contains(where: { $0.index != nil }) { return nil }
        if after.count == 2, after[0].opBefore == .and, after[1].opBefore == .or {
            guard case .fixed(let ok) = texts[0] else { return nil }
            if case .fixed(let fail) = texts[1], fail.trimmed() == ok.trimmed() { return nil }
            return .branches(ok: ok, fail: texts[1])
        }
        if after.count == 1, after[0].opBefore == .or { return .orElse(fail: texts[0]) }
        return nil
    }

    /// The integer `line` holds between `prefix` and `suffix` (trimmed at their outer edge), or nil.
    private static func printedValue(_ line: Bytes, prefix rawPrefix: Bytes, suffix rawSuffix: Bytes) -> Int64? {
        let prefix = Bytes(rawPrefix[rawPrefix.leadingCount(of: [Byte.space, Byte.tab])...])
        let suffix = rawSuffix.trimmedRight()
        guard line.count >= prefix.count + suffix.count, line.hasPrefix(prefix), line.hasSuffix(suffix) else { return nil }
        let middle = Bytes(line[prefix.count..<(line.count - suffix.count)])
        return statusValue.fullMatches(middle) ? TaskText.number(middle) : nil
    }
}
