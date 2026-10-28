import Foundation

/// What a tool result says (TASK-PARSERS 4.1–4.3): the output text, the exit code and the state.
enum ResultState: Equatable {
    case done, running, aborted, timeout
}

enum ResultReading {
    /// The text of a content value: a string as is; the `text` of an array's text items joined by LF.
    static func blocksText(_ content: JSONValue?) -> Bytes {
        switch content {
        case .string(let text)?:
            return Bytes(utf8: text)
        case .array(let items)?:
            var parts: [Bytes] = []
            for item in items {
                guard let block = item.object, let type = block["type"]?.string,
                      type == "text" || type == "input_text" || type == "output_text",
                      let text = block["text"]?.string else { continue }
                parts.append(Bytes(utf8: text))
            }
            return Bytes.joined(parts, separator: [Byte.lf])
        default:
            return []
        }
    }

    /// A JSON integer (never a boolean or a fraction) in the signed 64-bit range.
    static func integer(_ value: JSONValue?) -> Int64? { value?.int64 }

    // MARK: Claude

    static let claudeExit = Pattern(#"Exit code (-?[0-9]+)"#)
    static let claudeTimeout = Pattern(#"Command timed out after .*"#)
    static let claudeBackground = Pattern(
        #"Command running in background with ID: ?([A-Za-z0-9_:-]+(?:\.[A-Za-z0-9_:-]+)*).*"#)
    static let claudeAbort = TaskText.list([
        "[Request interrupted by user", "The user doesn't want to proceed with this tool use",
        "The user doesn't want to take this action", "Permission to use ", "Claude requested permissions to use",
        "User rejected", "The tool use was rejected",
    ])
    static let bashOutputStatus = Pattern(#"<status>([a-z_]+)</status>"#)
    static let bashOutputExit = Pattern(#"<exit_code>(-?[0-9]+)</exit_code>"#)

    // MARK: omp, Pi, OpenClaw

    static let piExit = Pattern(#"\(?Command exited with code (-?[0-9]+)\)?"#)
    static let piTimeout = Pattern(#"\[?Command timed out.*"#)
    static let piAbort = Pattern(#"(Command aborted.*|Command was terminated.*|Operation aborted.*|Command cancelled.*)"#)
    static let piRunning = Pattern(#"Command still running \(session .*"#)

    // MARK: Codex

    static let codexExit = Pattern(#"Exit code: (-?[0-9]+)"#)
    static let codexExited = Pattern(#"Process exited with code (-?[0-9]+)"#)
    static let codexRunning = Pattern(#"Process running with session ID ([0-9]+)"#)
    static let codexTimeout = Pattern(#"command timed out after [0-9]+ milliseconds"#)
    static let codexHeader = Pattern(
        #"(Chunk ID|Wall time|Exit code|Process exited with code|Process running with session ID|Original token count|Total output lines)[: ].*"#)
    private static let codexOutputLine = Bytes(utf8: "Output:")
    private static let patchApplied = Bytes(utf8: "Success. Updated the following files")
    private static let patchRejected = TaskText.list(["apply_patch verification failed", "Failed to apply patch"])

    struct CodexStatus {
        var text: Bytes
        var body: Bytes
        var exitCode: Int64?
        var state = ResultState.done
        var session: String?
        /// The patch verdict: failed or not, when the output reports an `apply_patch`.
        var patchFailed: Bool?
    }

    /// The output value of a Codex result (TASK-PARSERS 4.1, *Codex status* step 1).
    static func codexOutput(_ result: JSONValue?) -> JSONValue? {
        var out = result
        if let object = result?.object, object["type"] != nil, let output = object["output"] { out = output }
        if let content = out?.object?["content"] {
            if case .array = content { out = content } else if case .string = content { out = content }
        }
        if case .string(let text)? = out, text.utf8.first(where: { !TaskInput.jsonWhitespace($0) }) == UInt8(ascii: "["),
           let items = TaskInput.parse(text)?.array, !items.isEmpty,
           items.allSatisfy({ $0.object?["type"]?.string != nil }) {
            out = .array(items)
        }
        return out
    }

    static func codexStatus(_ result: JSONValue?) -> CodexStatus {
        let text = blocksText(codexOutput(result)).lfNormalized()
        var status = CodexStatus(text: text, body: text)
        let lines = text.split(byte: Byte.lf)
        if let head = TaskInput.leadingObject(text), let output = head["output"]?.string {
            status.body = Bytes(utf8: output).lfNormalized()
            if let code = integer(head["exit_code"]) {
                status.exitCode = code
            } else if let code = integer(head["metadata"]?.object?["exit_code"]) {
                status.exitCode = code
            } else if let session = integer(head["session_id"]) {
                status.state = .running
                status.session = "codex:\(session)"
            }
        } else if let k = lines.prefix(8).firstIndex(of: codexOutputLine),
                  lines[..<k].allSatisfy({ codexHeader.fullMatches($0) }) {
            status.body = Bytes.joined(Array(lines[(k + 1)...]), separator: [Byte.lf])
            for line in lines[..<k] {
                if let match = codexExit.fullMatch(line) ?? codexExited.fullMatch(line) {
                    status.exitCode = TaskText.number(match.text(1))
                }
                if let match = codexRunning.fullMatch(line) {
                    status.state = .running
                    status.session = "codex:" + match.text(1).string
                }
            }
        }
        let bodyFirst = status.body.firstPart(byte: Byte.lf)
        let first = lines[0].trimmed()
        if first == Bytes(utf8: "aborted") || first.hasPrefix(Bytes(utf8: "aborted by user")) || first.contains(Bytes(utf8: "rejected by user")) {
            status.state = .aborted
        } else if codexTimeout.matches(bodyFirst) {
            status.state = .timeout
        }
        if status.exitCode == nil, status.state == .done {
            if bodyFirst.hasPrefix(patchApplied) {
                status.patchFailed = false
            } else if patchRejected.contains(where: { bodyFirst.hasPrefix($0) }) {
                status.patchFailed = true
            }
        }
        return status
    }

    // MARK: result_text

    static func resultText(_ family: TaskFamily?, _ result: JSONValue?) -> Bytes {
        switch family {
        case .claude:
            guard let block = result?.object else { return [] }
            let text = blocksText(block["content"]).lfNormalized()
            var lines = text.split(byte: Byte.lf)
            if claudeExit.fullMatches(lines[0]) {
                lines.removeFirst()
                return Bytes.joined(lines, separator: [Byte.lf])
            }
            return text
        case .codex:
            return codexStatus(result).body
        case .droid:
            guard let block = result?.object else { return [] }
            return blocksText(block["content"]).lfNormalized()
        case .omp, .pi, .openclaw:
            guard let message = result?.object else { return [] }
            var lines = blocksText(message["content"]).lfNormalized().split(byte: Byte.lf)
            while let last = lines.last, last.trimmed().isEmpty { lines.removeLast() }
            if let last = lines.last, piExit.fullMatches(last.trimmed()) { lines.removeLast() }
            return Bytes.joined(lines, separator: [Byte.lf])
        case nil:
            return []
        }
    }
}
