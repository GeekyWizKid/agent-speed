import Foundation

/// The sources that record tool calls (TASK-PARSERS 1, *Sources*) and their tool tables (2, A.1).
enum TaskFamily: Sendable {
    case claude, codex, omp, pi, openclaw, droid

    init?(_ source: SourceKind) {
        switch source {
        case .claudeCode, .claudeDesktop: self = .claude
        case .codex: self = .codex
        case .omp: self = .omp
        case .pi: self = .pi
        case .openclaw: self = .openclaw
        case .droid: self = .droid
        default: return nil
        }
    }

    /// omp, Pi and OpenClaw compare tool names after ASCII case folding.
    var foldsNames: Bool { self == .omp || self == .pi || self == .openclaw }

    func toolName(_ tool: String) -> String {
        foldsNames ? Bytes(utf8: tool).lowercasedASCII().string : tool
    }

    /// The kind of a tool name as the source writes it (before case folding).
    func kind(of tool: String) -> TaskActionKind {
        Self.kinds[self]?[Bytes(utf8: toolName(tool))] ?? .other
    }

    private static let kinds: [TaskFamily: [Bytes: TaskActionKind]] = {
        let tables: [TaskFamily: [TaskActionKind: [String]]] = [
            .claude: [
                .read: ["Read", "NotebookRead", "LS"],
                .search: ["Grep", "Glob", "LSP"],
                .edit: ["Edit", "MultiEdit", "Write", "NotebookEdit"],
                .command: ["Bash", "PowerShell"],
                .delegate: ["Task", "Agent", "SendMessage"],
                .web: ["WebFetch", "WebSearch"],
            ],
            .codex: [
                .read: ["read_file", "list_dir", "view_image"],
                .search: ["grep_files"],
                .edit: ["apply_patch"],
                .command: ["shell", "container.exec", "local_shell", "shell_command", "exec_command", "exec"],
                .delegate: ["spawn_agent", "send_input", "resume_agent"],
                .web: ["web_search", "web_fetch"],
            ],
            .omp: [
                .read: ["read"],
                .search: ["grep", "search", "find", "glob", "ast_grep", "lsp"],
                .edit: ["edit", "write", "ast_edit"],
                .command: ["bash"],
                .delegate: ["task"],
                .web: ["web_search", "fetch", "browser"],
            ],
            .pi: [
                .read: ["read", "ls"],
                .search: ["grep", "find"],
                .edit: ["edit", "write"],
                .command: ["bash", "powershell"],
            ],
            .openclaw: [
                .read: ["read", "ls"],
                .edit: ["edit", "write", "apply_patch"],
                .command: ["exec", "bash"],
                .delegate: ["sessions_spawn", "sessions_send"],
                .web: ["web_search", "web_fetch", "x_search", "browser"],
            ],
            .droid: [
                .read: ["Read", "LS"],
                .search: ["Grep", "Glob"],
                .edit: ["Create", "Edit", "MultiEdit"],
                .command: ["Execute"],
                .delegate: ["Task"],
                .web: ["WebSearch", "FetchUrl"],
            ],
        ]
        return tables.mapValues { table in
            table.reduce(into: [Bytes: TaskActionKind]()) { result, entry in
                for name in entry.value { result[Bytes(utf8: name)] = entry.key }
            }
        }
    }()
}

/// Reading tool inputs as stored (TASK-PARSERS 1, *Inputs as stored*, *JSON*).
enum TaskInput {
    /// The input as an object: an object as is, a string holding a JSON object parsed, anything else nil.
    static func object(_ value: JSONValue?) -> [String: JSONValue]? {
        switch value {
        case .object(let members)?:
            return members
        case .string(let text)?:
            guard text.utf8.first(where: { !jsonWhitespace($0) }) == UInt8(ascii: "{") else { return nil }
            return parse(text)?.object
        default:
            return nil
        }
    }

    /// An RFC 8259 document, or nil.
    static func parse(_ text: String) -> JSONValue? {
        try? JSONParser.parse(text)
    }

    /// The leading JSON object of a text: the first value after JSON whitespace when it is an object, ignoring what
    /// follows it.
    static func leadingObject(_ text: Bytes) -> [String: JSONValue]? {
        guard text.first(where: { !jsonWhitespace($0) }) == UInt8(ascii: "{") else { return nil }
        return (try? JSONParser.parseLeading(Data(text)))?.object
    }

    @inline(__always) static func jsonWhitespace(_ byte: UInt8) -> Bool {
        byte == 0x20 || byte == 0x09 || byte == 0x0A || byte == 0x0D
    }

    private static let safeArgument = Pattern(#"[A-Za-z0-9_@%+=:,./-]+"#)

    static func quote(_ argument: Bytes) -> Bytes {
        if safeArgument.fullMatches(argument) { return argument }
        return [Byte.apostrophe] + argument.replacing([Byte.apostrophe], with: Bytes(utf8: "'\"'\"'")) + [Byte.apostrophe]
    }

    private static let argvTools: Set<String> = ["shell", "container.exec", "local_shell"]

    /// The command line a shell tool runs, or nil (TASK-PARSERS 3.1).
    static func commandText(_ family: TaskFamily, tool: String?, input: JSONValue?) -> String? {
        guard let tool else { return nil }
        let name = family.toolName(tool)
        guard family.kind(of: tool) == .command else { return nil }
        let args = object(input)
        if family == .codex, name == "exec" {
            if let args {
                return args["cmd"]?.string ?? args["command"]?.string
            }
            return input?.string
        }
        guard let args else { return nil }
        let field = family == .codex && name == "exec_command" ? "cmd" : "command"
        switch args[field] {
        case .string(let command)?:
            return command
        case .array(let items)? where family == .codex && argvTools.contains(name) && !items.isEmpty:
            var parts: [Bytes] = []
            for item in items {
                guard let text = item.string else { return nil }
                parts.append(quote(Bytes(utf8: text)))
            }
            return Bytes.joined(parts, separator: [Byte.space]).string
        default:
            return nil
        }
    }
}
