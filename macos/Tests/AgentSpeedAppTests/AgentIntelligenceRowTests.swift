import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 4, 智力 column: the fixed cell, the name room it takes, the row's score words and its score card for every
/// form (DESIGN 12).
@Suite("Agent intelligence column")
@MainActor
struct AgentIntelligenceRowTests {
    /// `score`: the agent's task score (METRICS 6.9), live while it generates, resting otherwise.
    static func agent(_ source: SourceKind = .claudeCode, model: String? = "claude-opus-5-5", score: Double?,
                      output: Double = 0, thinkingShare: Double = 0, state: TaskState = .passing,
                      task: AgentTaskStatus? = nil) -> AgentStatus {
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
                                       state: state)
            sums.score = TaskSetValue(id: "a", lastLineMs: nil, value: value)
        }
        return AgentStatus(id: "a", source: source, project: "p", model: model, phase: output > 0 ? .generating : .idle,
                           isSubagent: false, live: Rate(output: output, total: output), recentSpeed: nil,
                           lastActivity: Date(), intelligence: sums, task: task)
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

    // MARK: Accessibility

    @Test func rowsSayTheirScoreWordsAfterTheLiveValue() {
        let l10n = Localizer(preference: .zhHant)
        let live = Self.agent(score: 92.5, output: 40, thinkingShare: 0.245)
        #expect(TaskSetReading(agent: live).shown == 93)
        #expect(AgentSignalText.intelligenceAccessibility(TaskSetReading(agent: live), l10n) == "智力 93")
        let resting = Self.agent(score: 73.75)
        #expect(TaskSetReading(agent: resting).isResting)
        #expect(AgentSignalText.intelligenceAccessibility(TaskSetReading(agent: resting), l10n) == "智力 74，閒置中")
    }

    @Test func unprovenScoresSayWhatWouldProveThem() {
        let l10n = Localizer(preference: .zhHant)
        let unverified = Self.agent(score: 9, output: 30, state: .unverified, task: Self.task(.unverified, 9))
        #expect(TaskSetReading(agent: unverified).isUnproven)
        #expect(AgentSignalText.intelligenceAccessibility(TaskSetReading(agent: unverified), l10n) == "智力 9，尚未驗證")
        let pending = Self.agent(score: 34, state: .pending, task: Self.task(.pending, 34))
        #expect(AgentSignalText.intelligenceAccessibility(TaskSetReading(agent: pending), Localizer(preference: .en))
            == "intelligence 34, re-check pending, idle")
    }

    @Test func rowsWithoutAScoreSayNothing() {
        let l10n = Localizer(preference: .zhHant)
        let quiet = Self.agent(model: nil, score: nil)
        #expect(TaskSetReading(agent: quiet) == .none)
        #expect(AgentSignalText.intelligenceAccessibility(TaskSetReading(agent: quiet), l10n) == nil)
        let deepseek = Self.agent(.deepseek, model: "deepseek-v4-pro", score: nil, output: 50)
        #expect(AgentSignalText.intelligenceAccessibility(TaskSetReading(agent: deepseek), l10n) == nil)
    }

    // MARK: Score card

    static func task(_ state: TaskState, _ score: Double?) -> AgentTaskStatus {
        var models = TaskModelFlags.prior
        models.success = TaskModelFlag(calibrated: true, tasks: 120)
        // An evaluation without events stands in for the shape; its facts are set below.
        var result = TaskResult(TaskEvaluation([], now: 0))
        result.state = state
        result.score = score
        result.shown = score.map(taskRoundHalfAway)
        result.dims = TaskDimensions(v: state == .passing ? 40 : 0, c: 20, r: 11, e: (score ?? 0) - (state == .passing ? 71 : 31), k: 0)
        if state == .pending {
            result.vHeld = 40
            result.lastPass = TaskLastPass(suite: "pytest", commandClass: .test, key: TaskDigest(value: 0), buildOnly: false)
        }
        if state == .failing {
            result.openFailure = TaskOpenFailure(suite: nil, commandClass: .build, key: TaskDigest(value: 0), errors: 3, peak: 9,
                                                 counted: true, edited: false)
        }
        return AgentTaskStatus(taskStartMs: 0, result: result, predictions: nil, models: models)
    }

    @Test func theCardMapsTheAgentsTask() {
        let failing = Self.agent(score: 41, output: 30, state: .failing, task: Self.task(.failing, 41))
        let card = TaskScoreCardModel(agent: failing, facts: ["推理強度：high"])
        guard case .scored(let score) = card.body else {
            Issue.record("a scored source has a scored card")
            return
        }
        #expect(score.form == .live)
        #expect(score.state == .failing)
        #expect(score.shown == 41)
        #expect(score.openFailure?.check == TaskCheckName(suite: nil, kind: .build))
        #expect(TaskScoreCardText.status(score, Localizer(preference: .zhHant)).plain == "建置：還有 3 個錯誤（最多時 9 個）")
        #expect(card.facts == ["推理強度：high"])
        let pending = TaskScoreCardModel(agent: Self.agent(score: 34, state: .pending, task: Self.task(.pending, 34)), facts: [])
        #expect(pending.score?.form == .resting)
        #expect(pending.score?.heldCredit == 40)
        #expect(pending.score?.lastPass?.check.suite == "pytest")
    }

    @Test func aSourceWithoutScoresGetsTheUnsupportedCard() {
        let deepseek = Self.agent(.deepseek, model: "deepseek-v4-pro", score: nil, output: 50)
        #expect(TaskScoreCardModel(agent: deepseek, facts: []).body == .unsupported)
    }

    @Test func percentRoundsHalfAwayFromZero() {
        #expect(AgentSignalText.percent(0.245) == "25%")
        #expect(AgentSignalText.percent(0.004) == "0%")
        #expect(AgentSignalText.percent(1) == "100%")
    }
}
