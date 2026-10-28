import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 4, Intelligence column: the fixed cell, the name room it takes, and the row's tooltip and accessibility
/// lines for every form.
@Suite("Agent intelligence column")
@MainActor
struct AgentIntelligenceRowTests {
    /// `score`: the agent's task score (METRICS 6.9), live while it generates, resting otherwise.
    static func agent(_ source: SourceKind = .claudeCode, model: String? = "claude-opus-5-5", score: Double?,
                      output: Double = 0, thinkingShare: Double = 0) -> AgentStatus {
        var sums = IntelligenceSums()
        if output > 0 {
            sums.output = output
            if source.recordsReasoningApart {
                sums.thinking = output * thinkingShare
                sums.thinkingBase = output
            }
        }
        if let score {
            let value = TaskAgentValue(form: output > 0 ? .live : .resting, score: score, shown: taskRoundHalfAway(score),
                                       state: .passing)
            sums.score = TaskSetValue(id: "a", lastLineMs: nil, value: value)
        }
        return AgentStatus(id: "a", source: source, project: "p", model: model, phase: output > 0 ? .generating : .idle,
                           isSubagent: false, live: Rate(output: output, total: output), recentSpeed: nil,
                           lastActivity: Date(), intelligence: sums)
    }

    // MARK: Cell

    @Test func cellIsAsWideAsTheWiderOf888AndTheCaption() {
        // `888` in SF Pro 12 pt medium is 23.2 pt; 智力 at 10.5 pt is 20.9 pt.
        for preference in [LanguagePreference.zhHant, .zhHans, .en, .ja] {
            let caption = Localizer(preference: preference)("intel.glyph")
            #expect(AgentIntelligenceColumn.width(caption: caption) == 24, "\(caption)")
        }
        // A caption wider than the digits sets the width.
        #expect(AgentIntelligenceColumn.width(caption: "Intelligence") > 24)
    }

    @Test func theNameBlockGivesWayToTheCellAndItsGap() {
        // Rows 328: badge 16, two 12 pt row spacings, cell 24, 6, live value 48.
        #expect(AgentCapsuleColumn.nameWidth(rowWidth: 328, depth: 0, column: 0, intelligence: 24) == 210)
        #expect(AgentCapsuleColumn.nameWidth(rowWidth: 328, depth: 2, column: 0, intelligence: 24) == 186)
        #expect(AgentCapsuleColumn.nameWidth(rowWidth: 328, depth: 0, column: 65, intelligence: 24) == 137)
    }

    @Test func theWordsDropWhenTheCellLeavesTheNameUnder72() {
        let widest = EffortCapsule(effort: "ultracode", speed: "ultrafast")
        let full = widest.width(compact: false)
        let room = AgentCapsuleColumn.nameWidth(rowWidth: 328, depth: 2, column: full, intelligence: 24)
        let column = AgentCapsuleColumn(rows: [(widest, 2)], rowWidth: 328, intelligence: 24)
        #expect(column.compact == (room < AgentCapsuleColumn.minimumName))
        let shallow = AgentCapsuleColumn(rows: [(EffortCapsule(effort: "xhigh", speed: nil), 0)], rowWidth: 328, intelligence: 24)
        #expect(!shallow.compact)
    }

    // MARK: Tooltip and accessibility

    @Test func liveRowsSayTheScoreAndThinkingShare() {
        let l10n = Localizer(preference: .zhHant)
        let agent = Self.agent(score: 92.5, output: 40, thinkingShare: 0.245)
        #expect(agent.intelligenceForm.shown == 93)
        #expect(AgentSignalText.intelligence(agent, l10n) == ["智力 93 · 思考 25%"])
        #expect(AgentSignalText.intelligenceAccessibility(agent.intelligenceForm, l10n) == "智力 93")
    }

    @Test func aSourceWithoutATaskScoreSaysNothing() {
        let l10n = Localizer(preference: .en)
        let agent = Self.agent(.deepseek, model: "deepseek-v4-pro", score: nil, output: 50)
        #expect(AgentSignalText.intelligence(agent, l10n).isEmpty)
        #expect(AgentSignalText.intelligenceAccessibility(agent.intelligenceForm, l10n) == nil)
    }

    @Test func restingRowsKeepTheLastScoreWithoutTheLiveLine() {
        let l10n = Localizer(preference: .zhHant)
        let agent = Self.agent(score: 73.75)
        #expect(agent.intelligenceForm == .resting(73.75))
        #expect(agent.intelligenceForm.shown == 74)
        #expect(AgentSignalText.intelligence(agent, l10n).isEmpty)
    }

    @Test func rowsWithoutAScoreSayNothing() {
        let l10n = Localizer(preference: .zhHant)
        let quiet = Self.agent(model: nil, score: nil)
        #expect(AgentSignalText.intelligence(quiet, l10n).isEmpty)
        #expect(AgentSignalText.intelligenceAccessibility(quiet.intelligenceForm, l10n) == nil)
        let working = Self.agent(.gemini, model: nil, score: nil, output: 20, thinkingShare: 0.25)
        #expect(AgentSignalText.intelligence(working, l10n).isEmpty)
    }

    @Test func percentRoundsHalfAwayFromZero() {
        #expect(AgentSignalText.percent(0.245) == "25%")
        #expect(AgentSignalText.percent(0.004) == "0%")
        #expect(AgentSignalText.percent(1) == "100%")
    }
}
