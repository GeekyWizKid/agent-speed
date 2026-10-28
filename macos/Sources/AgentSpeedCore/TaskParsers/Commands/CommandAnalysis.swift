import Foundation

/// `command_info` and `shell_kind` over a parsed command (TASK-PARSERS 3.7, 3.9).
enum CommandAnalysis {
    static func info(_ command: ShellCommand, cwd: String?) -> TaskCommandInfo {
        let segments = command.segments
        var bestIndex: Int?
        var best = Classification.other
        var classes: [Classification] = []
        classes.reserveCapacity(segments.count)
        for (index, segment) in segments.enumerated() {
            let result = segment.isNeutral ? Classification.other : ProgramRules.classify(segment.words, cwd: cwd)
            classes.append(result)
            if result.commandClass.rank > best.commandClass.rank {
                bestIndex = index
                best = result
            }
        }
        var kept = zip(segments, classes).filter { $0.1.commandClass != .other }.map { tokens($0.0) }
        if kept.isEmpty { kept = segments.filter { !$0.isNeutral }.map(tokens) }
        if kept.isEmpty { kept = segments.filter { !$0.words.isEmpty }.map(tokens) }
        let key = TaskText.sha16(Bytes.joined(kept.map { Bytes.joined($0, separator: [0x1F]) }, separator: [0x1E]))
        guard let bestIndex else {
            return TaskCommandInfo(commandClass: .other, suite: nil, targeted: false, key: key, masked: false,
                                   background: segments.contains { $0.background })
        }
        let chosen = segments[bestIndex]
        let later = segments[(bestIndex + 1)...]
        let masked = !later.isEmpty && ((chosen.opAfter == .pipe && !command.pipefail)
            || later.contains { [.semicolon, .or, .background].contains($0.opBefore) })
        return TaskCommandInfo(commandClass: best.commandClass, suite: best.suite, targeted: best.targeted, key: key,
                               masked: masked, background: chosen.background)
    }

    private static let cosmetic = TaskText.words([
        "-q", "-qq", "-v", "-vv", "-vvv", "--quiet", "--verbose", "--color", "--no-color", "--colors", "--no-colors",
        "--ansi", "--no-ansi", "--nocapture", "--no-capture", "--show-output", "--silent",
    ])
    private static let cosmeticPrefixes = TaskText.list(["--color=", "--colour=", "--colors=", "--tb="])
    private static let pytestReport = Pattern(#"-r[A-Za-z]+"#)
    private static let npmLike = TaskText.words(["npm", "pnpm", "yarn"])
    private static let scriptRunners = TaskText.words(["npm", "pnpm", "yarn", "bun"])
    private static let workspaceRunners = TaskText.words(["pnpm", "yarn"])
    private static let testAliases = TaskText.words(["test", "t", "tst"])
    private static let runAliases = TaskText.words(["run-script", "rum", "urn"])

    /// The key tokens of one kept segment (TASK-PARSERS 3.7, *key*).
    private static func tokens(_ segment: ShellSegment) -> [Bytes] {
        let words = segment.words
        let p = ShellCommand.program(words[0])
        var tokens = [p]
        for word in words.dropFirst() {
            if cosmetic.contains(word) || cosmeticPrefixes.contains(where: { word.hasPrefix($0) }) { continue }
            if p == Bytes(utf8: "pytest"),
               word == Bytes(utf8: "-s") || pytestReport.fullMatches(word) || word == Bytes(utf8: "--capture=no") {
                continue
            }
            tokens.append(word)
        }
        if npmLike.contains(p), tokens.count > 1, testAliases.contains(tokens[1]) {
            tokens = [p, Bytes(utf8: "run"), Bytes(utf8: "test")] + tokens.dropFirst(2)
        } else if scriptRunners.contains(p), tokens.count > 1, runAliases.contains(tokens[1]) {
            tokens = [p, Bytes(utf8: "run")] + tokens.dropFirst(2)
        } else if workspaceRunners.contains(p), tokens.count > 1, !ScriptRunners.npmBuiltins.contains(tokens[1]),
                  tokens[1].first != Byte.dash {
            tokens = [p, Bytes(utf8: "run")] + tokens.dropFirst()
        }
        if !segment.context.isEmpty { tokens.insert([0x40] + segment.context, at: 0) }
        return tokens
    }

    private static let writeRedirections = TaskText.words([">", ">>", ">|", "&>", "&>>", "<>"])
    private static let nullTargets = TaskText.words(["/dev/null", "NUL", "nul"])
    private static let fdTarget = Pattern(#"[0-9-]+"#)
    static let readPrograms = TaskText.words([
        "cat", "head", "tail", "less", "more", "nl", "wc", "ls", "tree", "stat", "file", "pwd", "bat", "batcat", "sed",
        "awk", "jq", "yq", "realpath", "readlink", "du", "basename", "dirname", "od", "xxd", "hexdump", "strings",
        "diff", "cmp", "sort", "uniq", "cut", "tr", "column", "which", "type", "whereis", "printenv", "date", "env",
        "exa", "eza", "lsd",
    ])
    static let searchPrograms = TaskText.words([
        "rg", "grep", "egrep", "fgrep", "ag", "ack", "find", "fd", "fdfind", "locate", "mdfind",
    ])
    private static let gitRead = TaskText.words([
        "status", "diff", "log", "show", "blame", "rev-parse", "ls-tree", "cat-file", "describe", "shortlog", "reflog",
        "branch", "remote", "tag", "config",
    ])
    private static let gitSearch = TaskText.words(["grep", "ls-files"])
    private static let gitValue = OptionList([
        "-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path", "--config-env",
    ])
    private static let findActions = TaskText.words([
        "-delete", "-exec", "-execdir", "-ok", "-okdir", "-fprint", "-fprintf", "-fls",
    ])

    /// `shell_kind` (TASK-PARSERS 3.9).
    static func shellKind(_ command: ShellCommand) -> TaskActionKind {
        if command.appliesPatch || !command.patches.isEmpty { return .edit }
        if info(command, cwd: nil).commandClass != .other { return .command }
        for segment in command.segments {
            for (op, target) in segment.redirections where writeRedirections.contains(op) {
                if !nullTargets.contains(target), !fdTarget.fullMatches(target) { return .command }
            }
        }
        let segments = command.segments.filter { !$0.isNeutral }
        if segments.isEmpty { return .command }
        var search = false
        for segment in segments {
            let p = ShellCommand.program(segment.words[0])
            let args = Array(segment.words.dropFirst())
            if p == Bytes(utf8: "git") {
                let k = CommandOptions.dropOptions(args, from: 0, values: gitValue)
                let sub = k < args.count ? args[k] : nil
                if let sub, gitRead.contains(sub) {
                    continue
                } else if let sub, gitSearch.contains(sub) {
                    search = true
                } else {
                    return .command
                }
            } else if p == Bytes(utf8: "sed"), args.contains(where: { $0.hasPrefix(Bytes(utf8: "-i")) || $0.hasPrefix(Bytes(utf8: "--in-place")) }) {
                return .command
            } else if readPrograms.contains(p) {
                continue
            } else if searchPrograms.contains(p) {
                if p == Bytes(utf8: "find"), args.contains(where: { findActions.contains($0) }) { return .command }
                search = true
            } else {
                return .command
            }
        }
        return search ? .search : .read
    }
}
