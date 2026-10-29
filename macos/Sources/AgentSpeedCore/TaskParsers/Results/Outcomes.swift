import Foundation

/// `outcome`, `continuation`, `running_session` and `notification` (TASK-PARSERS 4.2, 4.3).
enum Outcomes {
    static func outcome(_ source: SourceKind, result: JSONValue?, t: Int64?, tool: String?, input: JSONValue?,
                        cwd: String?) -> TaskOutcome {
        var exitCode: Int64?
        var failed: Bool?
        var state = ResultState.done
        switch TaskFamily(source) {
        case .claude?:
            guard let block = result?.object else { return TaskOutcome(exitCode: nil, failed: nil, t: t) }
            let text = ResultReading.blocksText(block["content"]).lfNormalized()
            let first = text.firstPart(byte: Byte.lf)
            if let match = ResultReading.claudeExit.fullMatch(first) { exitCode = TaskText.number(match.text(1)) }
            if ResultReading.claudeAbort.contains(where: { text.hasPrefix($0) }) {
                state = .aborted
            } else if ResultReading.claudeTimeout.fullMatches(first) {
                state = .timeout
            } else if ResultReading.claudeBackground.fullMatches(first) {
                state = .running
            } else if tool == "BashOutput" || tool == "TaskOutput" {
                let status = ResultReading.bashOutputStatus.search(text)
                if status?.text(1) == Bytes(utf8: "running") { state = .running }
                if status != nil, exitCode == nil, let exit = ResultReading.bashOutputExit.search(text) {
                    exitCode = TaskText.number(exit.text(1))
                }
            }
            failed = exitCode.map { $0 != 0 } ?? (block["is_error"] == .bool(true))
        case .codex?:
            let status = ResultReading.codexStatus(result)
            exitCode = status.exitCode
            state = status.state
            failed = exitCode.map { $0 != 0 } ?? status.patchFailed
        case .omp?, .pi?, .openclaw?:
            guard let message = result?.object else { return TaskOutcome(exitCode: nil, failed: nil, t: t) }
            let lines = ResultReading.blocksText(message["content"]).lfNormalized().split(byte: Byte.lf).map { $0.trimmed() }
            let nonEmpty = lines.filter { !$0.isEmpty }
            let details = message["details"]?.object ?? [:]
            if let code = ResultReading.integer(details["exitCode"]) {
                exitCode = code
            } else if let code = ResultReading.integer(details["exit_code"]) {
                exitCode = code
            } else if let last = nonEmpty.last, let match = ResultReading.piExit.fullMatch(last) {
                exitCode = TaskText.number(match.text(1))
            }
            if details["status"] == .string("running")
                || (nonEmpty.first.map { ResultReading.piRunning.matches($0) } ?? false) {
                state = .running
            } else if details["timedOut"] == .bool(true)
                        || nonEmpty.suffix(2).contains(where: { ResultReading.piTimeout.fullMatches($0) }) {
                state = .timeout
            } else if let last = nonEmpty.last, let first = nonEmpty.first,
                      ResultReading.piAbort.fullMatches(last) || ResultReading.piAbort.fullMatches(first) {
                state = .aborted
            }
            failed = exitCode.map { $0 != 0 } ?? (message["isError"] == .bool(true))
        case .droid?:
            guard let block = result?.object else { return TaskOutcome(exitCode: nil, failed: nil, t: t) }
            failed = block["is_error"] == .bool(true)
        case nil:
            return TaskOutcome(exitCode: nil, failed: nil, t: t)
        }
        if tool != nil, state == .done, let family = TaskFamily(source),
           let text = TaskInput.commandText(family, tool: tool, input: input) {
            let command = ShellCommand(text, cwd: cwd)
            let info = CommandAnalysis.info(command, cwd: cwd)
            let args = TaskInput.object(input) ?? [:]
            let flagged = (family == .claude && args["run_in_background"] == .bool(true))
                || (family == .openclaw && args["background"] == .bool(true))
            if flagged || info.background {
                state = .running
            } else if info.masked {
                let output = ResultReading.resultText(family, result)
                if let shown = MaskedRuns.status(command, info: info, output: output, cwd: cwd) {
                    failed = shown.failed
                    if shown.exitCode != nil || shown.failed { exitCode = shown.exitCode }
                } else {
                    failed = nil
                }
            }
        }
        if state != .done {
            failed = nil
            if state == .running || state == .aborted { exitCode = nil }
        }
        return TaskOutcome(exitCode: exitCode, failed: failed, t: t)
    }

    static func continuation(_ source: SourceKind, tool: String?, input: JSONValue?) -> String? {
        let family = TaskFamily(source)
        let args = TaskInput.object(input) ?? [:]
        if family == .codex, tool == "write_stdin", let session = ResultReading.integer(args["session_id"]) {
            return "codex:\(session)"
        }
        if family == .claude, tool == "BashOutput" || tool == "TaskOutput" {
            for field in ["bash_id", "shell_id", "task_id"] {
                if let id = args[field]?.string, !id.isEmpty { return "claude:" + id }
            }
        }
        return nil
    }

    static func runningSession(_ source: SourceKind, result: JSONValue?) -> String? {
        switch TaskFamily(source) {
        case .codex?:
            return ResultReading.codexStatus(result).session
        case .claude?:
            guard let block = result?.object else { return nil }
            let first = ResultReading.blocksText(block["content"]).lfNormalized().firstPart(byte: Byte.lf)
            return ResultReading.claudeBackground.fullMatch(first).map { "claude:" + $0.text(1).string }
        default:
            return nil
        }
    }

    private static let notifyTask = Pattern(#"<task-id>([^<]+)</task-id>"#)
    private static let notifySummary = Pattern(#"<summary>([^\n]*?)</summary>"#)
    private static let notifyExit = Pattern(
        #"Background command [^\n]*(?:\(exit code (-?[0-9]+)\)|with exit code (-?[0-9]+))"#)

    static func notification(_ source: SourceKind, line: JSONValue?) -> TaskNotice? {
        guard TaskFamily(source) == .claude, let line = line?.object, line["type"] == .string("attachment"),
              let attachment = line["attachment"]?.object, attachment["type"] == .string("queued_command"),
              attachment["commandMode"] == .string("task-notification"),
              let prompt = attachment["prompt"]?.string else { return nil }
        let text = Bytes(utf8: prompt).lfNormalized()
        guard let task = notifyTask.search(text), let summary = notifySummary.search(text),
              let exit = notifyExit.fullMatch(summary.text(1).trimmed()) else { return nil }
        return TaskNotice(session: "claude:" + task.text(1).string,
                          exitCode: TaskText.number(exit[1] ?? exit.text(2)))
    }
}
