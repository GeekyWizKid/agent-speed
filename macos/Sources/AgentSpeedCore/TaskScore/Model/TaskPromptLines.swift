import Foundation

/// `prompt_line` of `task_score.py`: the prompt kind of one line that passed its source's copied-history rules.
extension TaskPrompts {
    static let claudeOutputTags = ["local-command-stdout", "local-command-stderr"]
    static let claudeIgnoredTags = ["local-command-caveat", "bash-input", "bash-stdout", "bash-stderr",
                                    "user-memory-input", "system-reminder", "task-notification"]
    static let codexAgents = "# AGENTS.md instructions"

    /// prompt, command or commandOutput for a line of a scored source; nil for anything else.
    public static func line(_ source: SourceKind, _ line: JSONValue) -> TaskPromptLine? {
        guard case .object(let object) = line else { return nil }
        switch source {
        case .claudeCode, .claudeDesktop: return claudePrompt(object)
        case .codex: return codexPrompt(object)
        case .omp, .pi, .openclaw: return piFamilyPrompt(object)
        case .droid: return droidPrompt(object)
        default: return nil
        }
    }

    struct ContentParts {
        var texts: [String]
        var media: Bool
        var lead: String
    }

    /// The texts and media of a message content, or nil when it has neither a non-blank text nor media.
    static func contentParts(_ content: JSONValue?, textTypes: [String], mediaTypes: [String]) -> ContentParts? {
        let texts: [String]
        var media = false
        switch content {
        case .string(let text):
            texts = [text]
        case .array(let items):
            var found: [String] = []
            for case .object(let block) in items {
                let type = block["type"]?.string
                if textTypes.contains(where: { TaskPythonText.same(type, $0) }), let text = block["text"]?.string {
                    found.append(text)
                }
                if !media, mediaTypes.contains(where: { TaskPythonText.same(type, $0) }) { media = true }
            }
            texts = found
        default:
            return nil
        }
        if !media && texts.allSatisfy(TaskPythonText.isBlank) { return nil }
        return ContentParts(texts: texts, media: media, lead: texts.first ?? "")
    }

    /// The string content, or the text blocks that do not start (after leading whitespace) with `<`, joined with LF.
    static func promptText(_ content: JSONValue?, _ texts: [String]) -> String {
        if case .string(let text) = content { return text }
        return texts.filter { !TaskPythonText.hasPrefix(TaskPythonText.leftStripped($0), "<") }.joined(separator: "\n")
    }

    static func isTrue(_ value: JSONValue?) -> Bool {
        value == .bool(true)
    }

    static func hasToolResult(_ content: JSONValue?) -> Bool {
        guard case .array(let items) = content else { return false }
        return items.contains { TaskPythonText.same($0["type"]?.string, "tool_result") }
    }

    static func startsWithTag(_ head: Substring.UnicodeScalarView, _ tags: [String]) -> Bool {
        for tag in tags {
            let opening = "<" + tag
            guard TaskPythonText.hasPrefix(head, opening) else { continue }
            let next = head.index(head.startIndex, offsetBy: opening.unicodeScalars.count)
            if next == head.endIndex { return true }
            let scalar = head[next]
            if scalar == ">" || scalar == "/" || TaskPythonText.isSpace(scalar) { return true }
        }
        return false
    }

    static func claudeQueuedPrompt(_ object: [String: JSONValue]) -> TaskPromptLine? {
        guard case .object(let attachment)? = object["attachment"],
              TaskPythonText.same(attachment["type"]?.string, "queued_command"),
              TaskPythonText.same(attachment["commandMode"]?.string, "prompt"), isTrue(attachment["humanTurn"]),
              !isTrue(object["isMeta"]), !isTrue(attachment["isMeta"])
        else { return nil }
        let content = attachment["prompt"]
        guard let parts = contentParts(content, textTypes: ["text"], mediaTypes: ["image", "document"]) else {
            return nil
        }
        let t = attachment["timestamp"]?.string.flatMap { Timestamp.parse($0) }
        return TaskPromptLine(kind: .prompt, continuation: isContinuation(promptText(content, parts.texts)), t: t)
    }

    static func claudePrompt(_ object: [String: JSONValue]) -> TaskPromptLine? {
        let type = object["type"]?.string
        if TaskPythonText.same(type, "attachment") { return claudeQueuedPrompt(object) }
        guard TaskPythonText.same(type, "user"),
              !["isMeta", "isCompactSummary", "isVisibleInTranscriptOnly"].contains(where: { isTrue(object[$0]) })
        else { return nil }
        let content = object["message"]?.object?["content"]
        if hasToolResult(content) { return nil }
        guard let parts = contentParts(content, textTypes: ["text"], mediaTypes: ["image", "document"]) else {
            return nil
        }
        let head = TaskPythonText.leftStripped(parts.lead)
        if TaskPythonText.hasPrefix(head, "[Request interrupted by user") { return nil }
        if startsWithTag(head, claudeOutputTags) { return TaskPromptLine(kind: .commandOutput) }
        if startsWithTag(head, claudeIgnoredTags) { return nil }
        if TaskPythonText.hasPrefix(head, "<command-name>") || TaskPythonText.hasPrefix(head, "<command-message>") {
            return TaskPromptLine(kind: .command)
        }
        return TaskPromptLine(kind: .prompt, continuation: isContinuation(promptText(content, parts.texts)))
    }

    /// A `response_item` user message the user wrote; injected context (a text led by a tag, AGENTS.md) is not.
    static func codexPrompt(_ object: [String: JSONValue]) -> TaskPromptLine? {
        guard TaskPythonText.same(object["type"]?.string, "response_item"),
              case .object(let payload)? = object["payload"],
              TaskPythonText.same(payload["type"]?.string, "message"),
              TaskPythonText.same(payload["role"]?.string, "user")
        else { return nil }
        let content = payload["content"]
        guard let parts = contentParts(content, textTypes: ["input_text", "text"], mediaTypes: ["input_image"]) else {
            return nil
        }
        let lead = TaskPythonText.leftStripped(parts.lead)
        var scalars = lead.makeIterator()
        if let first = scalars.next(), let second = scalars.next(), first == "<",
           second.isASCII, (second.value | 0x20) >= 0x61, (second.value | 0x20) <= 0x7A {
            return nil
        }
        if TaskPythonText.hasPrefix(lead, codexAgents) { return nil }
        return TaskPromptLine(kind: .prompt, continuation: isContinuation(promptText(content, parts.texts)))
    }

    static func piFamilyPrompt(_ object: [String: JSONValue]) -> TaskPromptLine? {
        guard TaskPythonText.same(object["type"]?.string, "message"),
              case .object(let message)? = object["message"],
              TaskPythonText.same(message["role"]?.string, "user"),
              let parts = contentParts(message["content"], textTypes: ["text"], mediaTypes: ["image"])
        else { return nil }
        return TaskPromptLine(kind: .prompt, continuation: isContinuation(parts.texts.joined(separator: "\n")))
    }

    /// A Factory Droid user message without `visibility` that holds no tool result and has a non-blank text or an image.
    static func droidPrompt(_ object: [String: JSONValue]) -> TaskPromptLine? {
        guard TaskPythonText.same(object["type"]?.string, "message"),
              case .object(let message)? = object["message"],
              TaskPythonText.same(message["role"]?.string, "user")
        else { return nil }
        if let visibility = message["visibility"], visibility != .null { return nil }
        let content = message["content"]
        if hasToolResult(content) { return nil }
        guard let parts = contentParts(content, textTypes: ["text"], mediaTypes: ["image"]) else { return nil }
        return TaskPromptLine(kind: .prompt, continuation: isContinuation(promptText(content, parts.texts)))
    }
}
