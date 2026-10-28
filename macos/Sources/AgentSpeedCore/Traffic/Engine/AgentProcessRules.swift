import Foundation

/// The agent-process rows of METRICS 10.5: which source, if any, a process runs.
struct AgentProcessRules: Sendable {
    static let scriptHosts: Set<String> = ["node", "nodejs", "bun", "deno"]

    let platform: TrafficPlatform

    private var claudeDesktopCode: [String] {
        platform.isWindows
            ? ["%LOCALAPPDATA%/Claude/claude-code/", "%LOCALAPPDATA%/Claude-3p/claude-code/", "%APPDATA%/Claude/claude-code/"]
            : ["~/Library/Application Support/Claude/claude-code/"]
    }

    private var claudeDesktopApp: [String] {
        platform.isWindows ? ["%LOCALAPPDATA%/AnthropicClaude/"] : ["/Applications/Claude.app/Contents/MacOS/Claude"]
    }

    /// Desktop apps' own executables, never agent processes.
    private var desktopApps: [String] {
        platform.isWindows
            ? ["%LOCALAPPDATA%/AnthropicClaude/"]
            : ["/Applications/Claude.app/Contents/MacOS/Claude", "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
               "/Applications/Codex.app/Contents/MacOS/Codex", "/Applications/OpenCode.app/Contents/MacOS/OpenCode",
               "/Applications/Antigravity.app/Contents/MacOS/Antigravity"]
    }

    private static let desktopPackages = ["/windowsapps/claude_", "/windowsapps/openai.codex_", "/windowsapps/openai.chatgpt"]

    private var deepseekApp: [String] {
        platform.isWindows ? [] : ["/Applications/DeepSeek Harness.app/Contents/MacOS/DeepSeek Harness"]
    }

    /// WorkBuddy runs its embedded CLI on the app's own executable: macOS by the app's bundle id, Windows by name.
    private static let workbuddyBundles: [String: SourceKind] = [
        "com.tencent.workbuddy.mac": .workbuddy, "com.workbuddy.workbuddy-ai": .workbuddyAi,
    ]
    private static let workbuddyExecutables: [String: SourceKind] = ["WorkBuddy": .workbuddy, "WorkBuddyAI": .workbuddyAi]
    private static let workbuddyCLI = "/app.asar.unpacked/cli/"

    /// Arguments are needed for script hosts, Python (Hermes entrypoints), `claude` (the Chrome bridge check), and
    /// WorkBuddy executables; everything else is decided by the path.
    func needsArguments(executable: String, bundleIdentifier: () -> String?) -> Bool {
        let name = platform.name(executable)
        return Self.scriptHosts.contains(name.lowercased()) || Self.isPython(name) || name == "claude"
            || platform.isUnder(executable, ["~/.local/share/claude/versions"])
            || workbuddyEdition(executable: executable, bundleIdentifier: bundleIdentifier) != nil
    }

    /// The WorkBuddy edition whose executable this is.
    func workbuddyEdition(executable: String, bundleIdentifier: () -> String?) -> SourceKind? {
        if platform.isWindows { return Self.workbuddyExecutables[platform.name(executable)] }
        return bundleIdentifier().flatMap { Self.workbuddyBundles[$0] }
    }

    func isScriptHost(_ executable: String) -> Bool {
        Self.scriptHosts.contains(platform.name(executable).lowercased())
    }

    func isClaudeDesktopApp(_ executable: String) -> Bool {
        if platform.isUnder(executable, claudeDesktopApp) { return true }
        return platform.isWindows && platform.name(executable) == "Claude"
            && platform.normalize(executable).contains("/windowsapps/claude_")
    }

    func isDesktopApp(_ executable: String) -> Bool {
        if platform.isUnder(executable, desktopApps) { return true }
        guard platform.isWindows else { return false }
        let normalized = platform.normalize(executable)
        return Self.desktopPackages.contains { normalized.contains($0) }
            && (platform.name(executable).first?.isUppercase ?? false)
    }

    private func scriptArguments(executable: String, arguments: [String]?) -> String {
        guard isScriptHost(executable) else { return "" }
        return (arguments ?? []).dropFirst().map { argument in
            argument.contains("/") || argument.contains("\\") ? platform.normalize(argument) : argument
        }.joined(separator: " ")
    }

    /// The source whose row matches; `ancestorExecutables` are the process's ancestors, nearest first.
    func source(
        executable: String,
        arguments: () -> [String]?,
        bundleIdentifier: () -> String? = { nil },
        ancestorExecutables: [String]
    ) -> SourceKind? {
        if isDesktopApp(executable) { return nil }
        let name = platform.name(executable)
        if name == "claude" {
            if platform.isUnder(executable, claudeDesktopCode) || ancestorExecutables.contains(where: isClaudeDesktopApp) {
                return .claudeDesktop
            }
            return (arguments() ?? []).contains("--chrome-native-host") ? nil : .claudeCode
        }
        if platform.isUnder(executable, ["~/.local/share/claude/versions"]) {
            return (arguments() ?? []).contains("--chrome-native-host") ? nil : .claudeCode
        }
        let script = isScriptHost(executable) ? scriptArguments(executable: executable, arguments: arguments()) : ""
        if script.contains("@anthropic-ai/claude-code/") { return .claudeCode }
        if name == "codex" || (name.hasPrefix("codex-") && Self.containsAny(name, ["x86_64", "aarch64", "apple-darwin", "linux", "windows"])) {
            return .codex
        }
        if script.contains("@openai/codex/bin/codex.js") { return .codex }
        if name == "dsh" || platform.isUnder(executable, deepseekApp) || (platform.isWindows && name == "deepseek harness") {
            return .deepseek
        }
        if name == "opencode" || name == "opencode-cli"
            || (name.hasPrefix("opencode-") && Self.containsAny(name, ["darwin", "linux", "windows"])) {
            return .opencode
        }
        if script.contains("opencode-ai/bin/opencode") { return .opencode }
        if script.contains("/@google/gemini-cli/") || name == "gemini" { return .gemini }
        if name == "omp" || script.contains("/@oh-my-pi/") || script.contains("/oh-my-pi/") { return .omp }
        if name == "agy" || name.hasPrefix("language_server_") { return .antigravity }
        if name == "zcode" || name == "zcode-cli" || script.contains("/@zcode/cli/") { return .zcode }
        if name == "kimi" || name == "kimi-code" || script.contains("/@moonshot-ai/kimi-code/") { return .kimi }
        if name == "grok" || name == "grok-build" { return .grokBuild }
        if name == "hermes" { return .hermes }
        if Self.isPython(name), isHermesPython(arguments() ?? []) { return .hermes }
        // Only the embedded CodeBuddy Code CLI is the agent; the app, its helpers and its daemon are not.
        if let edition = workbuddyEdition(executable: executable, bundleIdentifier: bundleIdentifier),
           (arguments() ?? []).dropFirst().contains(where: { platform.normalize($0).contains(Self.workbuddyCLI) }) {
            return edition
        }
        return nil
    }

    private static func isPython(_ name: String) -> Bool {
        let name = name.lowercased()
        if name == "python" || name == "pythonw" || name == "python3" { return true }
        guard name.hasPrefix("python3.") else { return false }
        let minor = name.dropFirst("python3.".count)
        return !minor.isEmpty && minor.utf8.allSatisfy { (48...57).contains($0) }
    }

    private func isHermesPython(_ arguments: [String]) -> Bool {
        let args = Array(arguments.dropFirst().drop { ["-u", "-B", "-E", "-I", "-s", "-S"].contains($0) })
        if args.count >= 2, args[0] == "-m", ["hermes_cli", "hermes_cli.main", "hermes_cli.__main__"].contains(args[1]) { return true }
        guard let entry = args.first, !entry.hasPrefix("-") else { return false }
        let path = platform.normalize(entry)
        return ["/bin/hermes", "/scripts/hermes", "/hermes-agent/cli.py", "/hermes-agent/run_agent.py", "/hermes-agent/hermes_cli/main.py"].contains { path.hasSuffix($0) }
    }

    private static func containsAny(_ text: String, _ needles: [String]) -> Bool {
        needles.contains { text.contains($0) }
    }
}
