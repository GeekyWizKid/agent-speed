import AgentSpeedCore
import AppKit
import SwiftUI

/// `--render-previews` group `agentintel`: the agents list with its intelligence column (DESIGN 4) — a root with a
/// sub-agent and a sub-sub-agent carrying the widest capsule, a long project name at rest, a model-less agent, a
/// DeepSeek agent, an agent running tools — in Chinese, English and Japanese, both appearances and Increase Contrast,
/// and the same rows with every form held on one value, to check that a form change moves nothing.
@MainActor
enum AgentIntelligencePreviews {
    static func render(model: AppModel, into directory: URL) {
        let saved = (model.settings.language, model.settings.agentsExpanded)
        defer {
            model.settings.language = saved.0
            model.l10n.apply(saved.0)
            model.settings.agentsExpanded = saved.1
        }
        model.settings.agentsExpanded = true
        PreviewRenderer.publish(model, snapshot: snapshot())
        for language in [LanguagePreference.zhHant, .en, .ja] {
            model.settings.language = language
            model.l10n.apply(language)
            for dark in [false, true] {
                let name = "agentintel-\(language.rawValue)-\(dark ? "dark" : "light")"
                write(section(model), dark: dark, to: directory.appendingPathComponent("\(name).png"))
                if language == .zhHant {
                    write(section(model).environment(\._colorSchemeContrast, .increased), dark: dark,
                          to: directory.appendingPathComponent("\(name)-contrast.png"))
                }
            }
        }
        model.settings.language = .zhHant
        model.l10n.apply(.zhHant)
        PreviewRenderer.publish(model, snapshot: steadySnapshot())
        for dark in [false, true] {
            write(section(model), dark: dark, to: directory.appendingPathComponent("agentintel-forms-\(dark ? "dark" : "light").png"))
        }
        model.settings.agentsExpanded = false
        PreviewRenderer.publish(model, speed: model.settings.redline * 0.4)
        for dark in [false, true] {
            write(DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now), dark: dark,
                  to: directory.appendingPathComponent("agentintel-dashboard-\(dark ? "dark" : "light").png"))
        }
    }

    private static func section(_ model: AppModel) -> some View {
        let width = DashboardController.width - 2 * DashboardLayout.padding
        return AgentsSection(model: model, rowWidth: width)
            .padding(DashboardLayout.padding)
            .frame(width: DashboardController.width)
            .appEnvironment(model)
    }

    private static func write<V: View>(_ view: V, dark: Bool, to url: URL) {
        PreviewSnapshot.writeHosted(view, dark: dark, to: url)
    }

    /// Every form and placement of the column.
    static func snapshot() -> SpeedSnapshot {
        var snapshot = PreviewData.snapshot(speed: 420, redline: 2000, agents: .crowd(0))
        let now = snapshot.timestamp
        snapshot.agents = [
            agent("rocket", .codex, "agent-speed", "gpt-6.1-sol", .generating, 142, effort: "xhigh", index: 132.8, now: now),
            agent("rocket-review", .codex, "agent-speed", "gpt-6.1-sol-mini", .generating, 64, parent: "rocket",
                  effort: "high", tier: "fast", index: 99.4, now: now),
            agent("rocket-scout", .codex, "agent-speed", "gpt-6.1-sol-mini", .generating, 38, parent: "rocket-review",
                  effort: "ultracode", tier: "ultrafast", index: 107, now: now),
            agent("atlas", .claudeCode, "intelligence-column-with-a-very-long-project-name", "claude-opus-5-5", .idle, 0,
                  effort: "xhigh", now: now.addingTimeInterval(-140)),
            agent("halo", .gemini, "halo", nil, .generating, 0, now: now),
            agent("quasar", .deepseek, "quasar", "deepseek-v4-pro", .generating, 51, effort: "high", index: 78, now: now),
            agent("lumen", .claudeCode, "lumen", "claude-sonnet-5-5", .tools, 0, effort: "medium", now: now.addingTimeInterval(-4)),
        ]
        snapshot.intelligence = snapshot.agents.reduce(into: IntelligenceSums()) { $0.add($1.intelligence) }
        return snapshot
    }

    /// Live, resting and none on rows whose shown value is the same (124), so the digits of a form change can be
    /// compared in place, plus a live value under its grade (the forms are never mixed).
    static func steadySnapshot() -> SpeedSnapshot {
        var snapshot = PreviewData.snapshot(speed: 420, redline: 2000, agents: .crowd(0))
        let now = snapshot.timestamp
        snapshot.agents = [
            agent("live", .claudeCode, "live-124", "claude-opus-5-5", .generating, 90, effort: "xhigh", index: 124, now: now),
            agent("resting", .claudeCode, "resting-124", "claude-opus-5-5", .idle, 0, effort: "xhigh", now: now),
            agent("under", .claudeCode, "live-under-grade", "claude-opus-5-5", .generating, 40, effort: "max", index: 108, now: now),
            agent("none", .gemini, "none", nil, .idle, 0, now: now),
        ]
        return snapshot
    }

    private static func agent(_ id: String, _ source: SourceKind, _ project: String, _ model: String?, _ phase: AgentPhase,
                              _ live: Double, parent: String? = nil, effort: String? = nil, tier: String? = nil,
                              index: Double? = nil, now: Date) -> AgentStatus {
        var modes: [ExecutionModeFact] = []
        if let effort, let fact = ExecutionModeFact(kind: .effort, rawValue: effort, evidence: .recorded, provider: nil, scope: .turn) {
            modes.append(fact)
        }
        if let tier, let fact = ExecutionModeFact(kind: .serviceTier, rawValue: tier, evidence: .served, provider: "openai", scope: .response) {
            modes.append(fact)
        }
        let share = source.recordsReasoningApart ? 0.3 : 0
        let score = index ?? PreviewData.previewScore(model, source: source)
        let sums = PreviewData.intelligence(output: live, score: score, id: "/preview/\(id).jsonl", idle: phase == .idle,
                                            thinkingShare: share, source: source)
        return AgentStatus(
            id: "/preview/\(id).jsonl", source: source, project: project, model: model, phase: phase,
            isSubagent: parent != nil, live: Rate(output: live, total: live * 26), recentSpeed: live > 0 ? live : nil,
            lastActivity: now, parentId: parent.map { "/preview/\($0).jsonl" }, modes: ExecutionModes(modes),
            intelligence: sums
        )
    }
}
