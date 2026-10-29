import Foundation

/// The per-source rules of METRICS 6.9 *Composing the stream* (the table and *Prompts*, *Calls*, *Results*). Lines that
/// can add nothing but their time are not parsed; the source's own line extraction already holds their facts.
extension TaskLineReader {
    // MARK: - Claude Code and Claude Desktop

    static let syntheticModel = "<synthetic>"
    private static let queuedCommand: [UInt8] = Array("queued_command".utf8)

    mutating func claude(_ bytes: UnsafeRawBufferPointer, line: ClaudeLine) -> TaskLine {
        guard let t = line.timestampMs else { return .none }
        setCwd(line.cwd)
        switch line.type {
        case .user, .attachment:
            if line.type == .attachment, !Self.contains(bytes, Self.queuedCommand) { return .line(t) }
            guard let object = TaskJSON.parse(bytes)?.object else { return .line(t) }
            var ops: [TaskLineOp] = []
            if let found = TaskPrompts.line(source, .object(object)) {
                ops.append(.prompt(found.kind, t: found.t ?? t, continuation: found.continuation))
            }
            if line.type == .attachment, let notice = TaskParsers.notification(source, line: .object(object)) {
                let queued = object["attachment"]?.object?["timestamp"]?.string.flatMap { Timestamp.parse($0) }
                ops.append(.notice(session: notice.session, exitCode: notice.exitCode, t: queued ?? t))
            }
            if line.type == .user, let content = object["message"]?.object?["content"]?.array {
                for block in content where block["type"]?.string == "tool_result" {
                    if let prepared = result(id: block["tool_use_id"]?.string, result: block, t: t) {
                        ops.append(.result(prepared))
                    }
                }
            }
            return .line(t: t, ops: ops)
        case .assistant:
            guard line.model != Self.syntheticModel else { return .line(t) }
            var ops: [TaskLineOp] = [.assistant(key: line.messageId ?? line.requestId ?? line.uuid)]
            if line.hasToolUse, let object = TaskJSON.parse(bytes)?.object {
                let calls = blockCalls(object["message"]?.object?["content"], bytes: bytes, type: "tool_use",
                                       field: "input", t: t)
                if !calls.isEmpty { ops.append(.calls(calls)) }
            }
            return .line(t: t, ops: ops)
        case .apiRequest, .other:
            return .line(t)
        }
    }

    // MARK: - Codex

    private static let codexTypes: [StaticString] = ["session_meta", "turn_context", "response_item"]

    mutating func codex(_ bytes: UnsafeRawBufferPointer, line: CodexLine) -> TaskLine {
        guard let t = line.timestampMs else { return .none }
        if case .sessionMeta(let meta) = line.kind {
            guard !headerSeen else { return .none }
            headerSeen = true
            createdMs = t
            startOrdinal = meta.subagentStartOrdinal
            setCwd(meta.cwd)
            return .line(t)
        }
        if let createdMs, t < createdMs - Self.copyToleranceMs { return .none }
        if let startOrdinal, let ordinal = line.ordinal, ordinal < startOrdinal { return .none }
        if case .turnContext(_, let cwd) = line.kind {
            setCwd(cwd)
            return .line(t)
        }
        guard TaskLinePeek.read(bytes, types: Self.codexTypes)?.type == 2,
              let object = TaskJSON.parse(bytes)?.object, let payload = object["payload"]?.object else { return .line(t) }
        if let found = TaskPrompts.line(source, .object(object)) {
            return .line(t: t, ops: [.plainPrompt(t: found.t ?? t, continuation: found.continuation)])
        }
        switch payload["type"]?.string {
        case "function_call"?, "custom_tool_call"?:
            let custom = payload["type"]?.string == "custom_tool_call"
            let field: StaticString = custom ? "input" : "arguments"
            let input = payload[custom ? "input" : "arguments"]
            let id = payload["call_id"]?.string
            if custom, let script = TaskParsers.codeScript(source, tool: payload["name"]?.string, input: input) {
                guard let prepared = self.script(id: id, t: t, script: script) else { return .line(t) }
                return .line(t: t, ops: [.script(prepared)])
            }
            let raw: TaskDigest? = Self.isComposite(input) ? TaskRawInputs.payloadDigest(bytes, field: field) : nil
            guard let prepared = call(id: id, t: t, tool: payload["name"]?.string, input: input,
                                      digest: .of(input: input, raw: raw)) else { return .line(t) }
            return .line(t: t, ops: [.calls([prepared])])
        case "function_call_output"?, "custom_tool_call_output"?:
            guard let prepared = result(id: payload["call_id"]?.string, result: .object(payload), t: t) else { return .line(t) }
            return .line(t: t, ops: [.result(prepared)])
        default:
            return .line(t)
        }
    }

    // MARK: - omp, Pi and OpenClaw

    private static let piFamilyTypes: [StaticString] = ["session", "message"]

    mutating func piFamily(_ bytes: UnsafeRawBufferPointer) -> TaskLine {
        guard let peek = TaskLinePeek.read(bytes, types: Self.piFamilyTypes) else { return .none }
        if peek.type == 0 {
            guard !headerSeen else { return .none }
            headerSeen = true
            createdMs = peek.timestampMs
            if let object = TaskJSON.parse(bytes)?.object { setCwd(object["cwd"]?.string) }
            return peek.timestampMs.map { .line($0) } ?? .none
        }
        guard headerSeen, peek.idIsString, let t = peek.timestampMs else { return .none }
        if let createdMs, t < createdMs - Self.copyToleranceMs { return .none }
        guard peek.type == 1, let object = TaskJSON.parse(bytes)?.object,
              let message = object["message"]?.object else { return .line(t) }
        switch message["role"]?.string {
        case "user"?:
            guard let found = TaskPrompts.line(source, .object(object)) else { return .line(t) }
            return .line(t: t, ops: [.prompt(found.kind, t: found.t ?? t, continuation: found.continuation)])
        case "assistant"?:
            guard Self.ompGuard(message) else { return .line(t) }
            let stamp = message["timestamp"].flatMap(Self.positiveStamp) ?? t
            let id = object["id"]?.string ?? ""
            var ops: [TaskLineOp] = [.assistant(key: "\(source.rawValue):\(id):\(stamp)")]
            if !(source == .openclaw && isOpenClawMirror(message)) {
                let calls = blockCalls(message["content"], bytes: bytes, type: "toolCall", field: "arguments", t: t)
                if !calls.isEmpty { ops.append(.calls(calls)) }
            }
            return .line(t: t, ops: ops)
        case "toolResult"?:
            guard let prepared = result(id: message["toolCallId"]?.string, result: .object(message), t: t) else {
                return .line(t)
            }
            return .line(t: t, ops: [.result(prepared)])
        default:
            return .line(t)
        }
    }

    /// METRICS 4.7 step 1 as reference.py's `omp_guard`: string `model`, `provider` and `api`, an object `usage`.
    private static func ompGuard(_ message: [String: JSONValue]) -> Bool {
        message["model"]?.string != nil && message["provider"]?.string != nil && message["api"]?.string != nil
            && message["usage"]?.object != nil
    }

    /// METRICS 4.14 *Mirrors*: a delegated turn mirrored into the transcript.
    private func isOpenClawMirror(_ message: [String: JSONValue]) -> Bool {
        if let origin = message["__openclaw"]?.object?["mirrorOrigin"]?.string, !origin.isEmpty { return true }
        let api = message["api"]?.string ?? "", provider = message["provider"]?.string ?? ""
        return (api == "cli" && provider == "claude-cli") || provider.utf8.starts(with: "acp-".utf8) || mirrorWindow
    }

    /// `trunc64(timestamp)` of a finite number above 0.
    private static func positiveStamp(_ value: JSONValue) -> Int64? {
        guard case .number(let literal) = value, let double = literal.double, double.isFinite, double > 0 else {
            return nil
        }
        if let exact = literal.int64 { return exact }
        return double >= 9.223372036854775807e18 ? .max : Int64(double)
    }

    // MARK: - Factory Droid

    private static let droidTypes: [StaticString] = ["session_start", "message"]

    mutating func droid(_ bytes: UnsafeRawBufferPointer) -> TaskLine {
        guard let peek = TaskLinePeek.read(bytes, types: Self.droidTypes) else { return .none }
        if peek.type == 0 {
            guard !headerSeen else { return .none }
            headerSeen = true
            if let object = TaskJSON.parse(bytes)?.object { setCwd(object["cwd"]?.string) }
            return peek.timestampMs.map { .line($0) } ?? .none
        }
        guard let t = peek.timestampMs else { return .none }
        guard peek.type == 1, let object = TaskJSON.parse(bytes)?.object,
              let message = object["message"]?.object else { return .line(t) }
        switch message["role"]?.string {
        case "user"?:
            var ops: [TaskLineOp] = []
            if let found = TaskPrompts.line(source, .object(object)) {
                ops.append(.prompt(found.kind, t: found.t ?? t, continuation: found.continuation))
            }
            for block in message["content"]?.array ?? [] where block["type"]?.string == "tool_result" {
                if let prepared = result(id: block["tool_use_id"]?.string, result: block, t: t) {
                    ops.append(.result(prepared))
                }
            }
            return .line(t: t, ops: ops)
        case "assistant"?:
            guard let id = object["id"]?.string, !id.isEmpty else { return .line(t) }
            var ops: [TaskLineOp] = [.assistant(key: "droid:" + id)]
            let calls = blockCalls(message["content"], bytes: bytes, type: "tool_use", field: "input", t: t)
            if !calls.isEmpty { ops.append(.calls(calls)) }
            return .line(t: t, ops: ops)
        default:
            return .line(t)
        }
    }

    // MARK: - Shared

    /// The calls of a message's content blocks of `type`, in content order, their digests from the line's raw text.
    private mutating func blockCalls(_ content: JSONValue?, bytes: UnsafeRawBufferPointer, type: String,
                                     field: StaticString, t: Int64) -> [TaskPreparedCall] {
        guard let blocks = content?.array else { return [] }
        let fieldName = String(describing: field)
        var raws: [Int: TaskDigest]?
        var calls: [TaskPreparedCall] = []
        for (index, block) in blocks.enumerated() {
            guard let object = block.object, object["type"]?.string == type else { continue }
            let input = object[fieldName]
            if raws == nil, Self.isComposite(input) { raws = TaskRawInputs.contentDigests(bytes, field: field) }
            let digest = TaskDigest.of(input: input, raw: raws?[index])
            if let call = call(id: object["id"]?.string, t: t, tool: object["name"]?.string, input: input, digest: digest) {
                calls.append(call)
            }
        }
        return calls
    }

    private static func isComposite(_ value: JSONValue?) -> Bool {
        switch value {
        case .object?, .array?, .number?, .bool?: true
        default: false
        }
    }

    static func contains(_ bytes: UnsafeRawBufferPointer, _ needle: [UInt8]) -> Bool {
        guard let first = needle.first, bytes.count >= needle.count else { return false }
        var index = 0
        let last = bytes.count - needle.count
        while index <= last {
            if bytes[index] == first {
                var offset = 1
                while offset < needle.count, bytes[index + offset] == needle[offset] { offset += 1 }
                if offset == needle.count { return true }
            }
            index += 1
        }
        return false
    }
}

extension TaskLineReader {
    /// The task line of a decoded transcript line (malformed lines and other families add nothing).
    mutating func read(_ bytes: UnsafeRawBufferPointer, _ line: DecodedLine) -> TaskLine {
        switch line {
        case .claude(.line(let decoded)): claude(bytes, line: decoded)
        case .codex(.line(let decoded)): codex(bytes, line: decoded)
        case .omp(.line), .pi(.line): piFamily(bytes)
        case .droid(let read): read.extraction.isLine ? droid(bytes) : .none
        default: .none
        }
    }
}

extension LineExtraction {
    var isLine: Bool {
        if case .line = self { return true }
        return false
    }
}
