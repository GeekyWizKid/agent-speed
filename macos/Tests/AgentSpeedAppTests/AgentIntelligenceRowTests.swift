import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 4, Intelligence column: the fixed cell, the name room it takes, and the row's tooltip and accessibility
/// lines for every form.
@Suite("Agent intelligence column")
@MainActor
struct AgentIntelligenceRowTests {
    static func agent(_ source: SourceKind = .claudeCode, model: String? = "claude-opus-5-5", grade: IntelligenceGrade?,
                      output: Double = 0, index: Double = 0, thinkingShare: Double = 0) -> AgentStatus {
        var sums = IntelligenceSums()
        if output > 0 {
            sums.output = output
            sums.gradedOutput = output * (index - 20 * thinkingShare)
            if source.recordsReasoningApart {
                sums.thinking = output * thinkingShare
                sums.thinkingBase = output
            }
        }
        return AgentStatus(id: "a", source: source, project: "p", model: model, phase: output > 0 ? .generating : .idle,
                           isSubagent: false, live: Rate(output: output, total: output), recentSpeed: nil,
                           lastActivity: Date(), grade: grade, intelligence: sums)
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

    @Test func liveRowsSayTheValueAndThinkingShareThenTheGrade() {
        let l10n = Localizer(preference: .zhHant)
        let grade = IntelligenceGrade(modelClass: .standard, level: 4)
        let agent = Self.agent(grade: grade, output: 40, index: 105, thinkingShare: 0.245)
        #expect(agent.intelligenceForm.shown == 105)
        #expect(AgentSignalText.intelligence(agent, l10n) == ["智力 105 · 思考 25%", "等級 104 · 標準"])
        #expect(AgentSignalText.intelligenceAccessibility(agent.intelligenceForm, l10n) == "智力 105")
    }

    @Test func aSourceWithoutSeparateReasoningShowsADashForTheShare() {
        let l10n = Localizer(preference: .en)
        let agent = Self.agent(.deepseek, model: "deepseek-v4-pro", grade: IntelligenceGrade(modelClass: .light, level: 3),
                               output: 50, index: 78)
        #expect(AgentSignalText.intelligence(agent, l10n).first == "Intelligence 78 · thinking –")
    }

    @Test func restingRowsSayOnlyTheGrade() {
        let l10n = Localizer(preference: .zhHant)
        let grade = IntelligenceGrade(modelClass: .flagship, level: 4)
        let agent = Self.agent(grade: grade)
        #expect(AgentSignalText.intelligence(agent, l10n) == ["等級 124 · 旗艦"])
        #expect(AgentSignalText.intelligenceAccessibility(agent.intelligenceForm, l10n) == "等級 124")
        #expect(AgentSignalText.intelligenceAccessibility(agent.intelligenceForm, Localizer(preference: .en)) == "grade 124")
    }

    @Test func modelLessRowsSayNothingWhileQuietAndNoGradeWhileLive() {
        let l10n = Localizer(preference: .zhHant)
        let quiet = Self.agent(model: nil, grade: nil)
        #expect(AgentSignalText.intelligence(quiet, l10n).isEmpty)
        #expect(AgentSignalText.intelligenceAccessibility(quiet.intelligenceForm, l10n) == nil)
        let live = Self.agent(.gemini, model: nil, grade: nil, output: 20, index: 97, thinkingShare: 0.25)
        #expect(AgentSignalText.intelligence(live, l10n) == ["智力 97 · 思考 25%"])
    }

    @Test func percentRoundsHalfAwayFromZero() {
        #expect(AgentSignalText.percent(0.245) == "25%")
        #expect(AgentSignalText.percent(0.004) == "0%")
        #expect(AgentSignalText.percent(1) == "100%")
    }
}
