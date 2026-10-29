import AgentSpeedCore
import CoreGraphics
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 12: the shown numbers, the score words, the card's texts, the motion rules and the proof mark's geometry.
@Suite("Task score text")
@MainActor
struct TaskScoreTextTests {
    let zh = Localizer(preference: .zhHant)
    let en = Localizer(preference: .en)

    static func score(_ state: TaskScoreState, _ total: Double?, form: TaskScoreForm = .live,
                      dims: TaskScoreDimensions = .zero) -> TaskScoreCardScore {
        TaskScoreCardScore(form: total == nil ? .none : form, state: state, score: total, dimensions: dims)
    }

    // MARK: Numbers

    @Test func scoresRoundHalfAwayFromZero() {
        #expect(TaskScoreText.rounded(85.5) == 86)
        #expect(TaskScoreText.rounded(85.49) == 85)
        #expect(TaskScoreText.rounded(0.5) == 1)
        #expect(TaskScoreText.rounded(0) == 0)
        #expect(TaskScoreText.rounded(-2.5) == -3)
    }

    @Test func dimensionsAddUpToTheShownScore() {
        // 40 + 17.6 + 9.5 + 7.3 + 8 = 82.4 → 82: floors 40 17 9 7 8 = 81, the missing point to C (0.6).
        let dims = TaskScoreDimensions(verify: 40, converge: 17.6, recover: 9.5, efficiency: 7.3, complete: 8)
        #expect(TaskScoreText.shownDimensions(dims, total: 82) == [40, 18, 9, 7, 8])
        // Ties go in the order V C R E K.
        let tied = TaskScoreDimensions(verify: 0, converge: 10.5, recover: 10.5, efficiency: 0, complete: 0)
        #expect(TaskScoreText.shownDimensions(tied, total: 21) == [0, 11, 10, 0, 0])
        // Nothing missing: plain floors.
        let whole = TaskScoreDimensions(verify: 20, converge: 25, recover: 15, efficiency: 6.2, complete: 4)
        #expect(TaskScoreText.shownDimensions(whole, total: 70) == [20, 25, 15, 6, 4])
        // Two points missing go to the two largest fractions.
        let split = TaskScoreDimensions(verify: 0, converge: 12.7, recover: 7.6, efficiency: 4.4, complete: 0)
        #expect(TaskScoreText.shownDimensions(split, total: 25) == [0, 13, 8, 4, 0])
    }

    @Test func probabilitiesClampAtTheEnds() {
        #expect(TaskScoreText.percent(0.004) == "<1%")
        #expect(TaskScoreText.percent(0.005) == "1%")
        #expect(TaskScoreText.percent(0.625) == "63%")
        #expect(TaskScoreText.percent(0.9949) == "99%")
        #expect(TaskScoreText.percent(0.995) == ">99%")
        #expect(TaskScoreText.percentWords(0.001, zh) == "不到 1%")
        #expect(TaskScoreText.percentWords(0.999, en) == "over 99%")
    }

    // MARK: Words

    @Test func scoreWordsWrapInDesignOrder() {
        #expect(TaskScoreText.words(shown: 86, state: .passing, form: .live, count: 4, zh) == "86，4 個 agent 中最高")
        #expect(TaskScoreText.words(shown: 9, state: .unverified, form: .live, count: 1, zh) == "9，尚未驗證")
        #expect(TaskScoreText.words(shown: 72, state: .passing, form: .resting, count: 2, zh) == "72，閒置中，2 個 agent 中最高")
        #expect(TaskScoreText.words(shown: 34, state: .pending, form: .resting, count: 1, en) == "34, re-check pending, idle")
        #expect(TaskScoreText.words(.none, zh) == nil)
    }

    // MARK: Card

    @Test func statusLinesNameTheCheck() {
        var failing = Self.score(.failing, 41)
        failing.openFailure = TaskOpenFailureFacts(check: TaskCheckName(suite: "cargo test", kind: .test), errors: 3,
                                                   peak: 9, counted: true, edited: false)
        let line = TaskScoreCardText.status(failing, zh)
        #expect(line.segments == [.check("cargo test"), .text("：還有 3 個錯誤（最多時 9 個）")])
        failing.openFailure?.counted = false
        #expect(TaskScoreCardText.status(failing, en).plain == "cargo test: failing")
        failing.openFailure?.edited = true
        failing.openFailure?.check = TaskCheckName(suite: nil, kind: .build)
        #expect(TaskScoreCardText.status(failing, zh).segments == [.text("建置：失敗後已修改，等待重新執行")])

        var passing = Self.score(.passing, 86)
        passing.lastPass = TaskLastPassFacts(check: TaskCheckName(suite: "swift test", kind: .test), buildOnly: false)
        #expect(TaskScoreCardText.status(passing, en).plain == "swift test: passed — the changes are verified")
        passing.lastPass?.buildOnly = true
        #expect(TaskScoreCardText.status(passing, en).plain == "swift test: passed, but no tests have run")
        #expect(TaskScoreCardText.status(Self.score(.none, nil), zh).plain == "還沒有修改，也還沒有執行測試或建置")
    }

    @Test func unverifiedRowsNeverShowAFakeZero() {
        let dims = TaskScoreDimensions(verify: 0, converge: 0, recover: 0, efficiency: 8.6, complete: 0)
        let rows = TaskScoreCardText.dimensionRows(Self.score(.unverified, 8.6, dims: dims))
        #expect(rows.map(\.value) == [.unverified, .unverified, .unverified, .value(9), .unverified])
        let none = TaskScoreCardText.dimensionRows(Self.score(.none, nil))
        #expect(none.map(\.value) == [.unverified, .unverified, .unverified, .absent, .unverified])
    }

    @Test func pendingHoldsTheVerificationCredit() {
        var pending = Self.score(.pending, 34, dims: TaskScoreDimensions(verify: 0, converge: 18, recover: 10, efficiency: 6.2, complete: 0))
        pending.heldCredit = 20
        let rows = TaskScoreCardText.dimensionRows(pending)
        #expect(rows[0].value == .pending(held: 20))
        #expect(rows[0].fraction == 0.5)
        #expect(rows[4].value == .value(0))
        let composition = TaskScoreCardText.composition(pending)
        #expect(composition.map(\.points) == [20, 18, 10, 6])
        #expect(composition.map(\.held) == [true, false, false, false])
    }

    @Test func predictionsShowOnlyWhileLiveAndFixOnlyWhileFailing() {
        let prior = TaskCalibration(calibrated: false, tasks: 12)
        let calibrated = TaskCalibration(calibrated: true, tasks: 212)
        var score = Self.score(.passing, 86)
        score.predictions = TaskScorePredictions(
            success: TaskScorePrediction(probability: 0.62, calibration: calibrated),
            stall: TaskScorePrediction(probability: 0.14, calibration: prior),
            fixFails: TaskScorePrediction(probability: 0.35, calibration: prior)
        )
        let cells = TaskScoreCardText.predictions(score, zh)
        #expect(cells.map(\.percent) == ["62%", "14%"])
        #expect(cells.map(\.flag) == ["依 212 個任務", "預設值 · 12/40"])
        #expect(cells[0].accessibility == "成功的機率 62%，依 212 個任務校準")
        #expect(cells[1].accessibility == "卡住的機率 14%，預設值")
        score.state = .failing
        #expect(TaskScoreCardText.predictions(score, zh).count == 3)
        score.form = .resting
        #expect(TaskScoreCardText.predictions(score, zh).isEmpty)
        #expect(TaskScoreCardText.flag(TaskCalibration(calibrated: false, tasks: 64), en) == "Prior")
    }

    @Test func eyebrowNamesRestingAndTheTopOfASet() {
        #expect(TaskScoreCardText.eyebrow(Self.score(.passing, 86), zh) == nil)
        #expect(TaskScoreCardText.eyebrow(Self.score(.passing, 72, form: .resting), zh) == "閒置")
        var hero = Self.score(.passing, 86)
        hero.topCount = 4
        #expect(TaskScoreCardText.eyebrow(hero, en) == "Highest of 4 agents")
    }

    @Test func accessibilityFollowsTheCardInOrder() {
        var score = Self.score(.unverified, 9, dims: TaskScoreDimensions(verify: 0, converge: 0, recover: 0, efficiency: 9, complete: 0))
        score.signals = WorkSignals(looping: 4, novelty: 0.46, failed: 5, recovered: 1)
        let model = TaskScoreCardModel(header: .init(source: .claudeCode, project: "nova", model: "claude-sonnet-5-5"),
                                       body: .scored(score), facts: ["推理強度：medium"])
        let lines = TaskScoreCardText.accessibilityLines(model, zh)
        // The state is spoken once.
        #expect(lines.first == "智力 9，尚未驗證")
        #expect(lines[1] == "已修改，但還沒有執行測試、建置或檢查")
        #expect(lines[2].hasPrefix("驗證成功，尚未驗證，"))
        #expect(lines[5].hasPrefix("行為效率 9 分，滿分 10，"))
        #expect(lines.contains("原地打轉：同一組動作已重複 4 次，且有失敗"))
        #expect(lines.suffix(2) == ["推理強度：medium", zh("task.card.footnote")])
        let unsupported = TaskScoreCardModel(header: .init(source: .deepseek, project: "tihu"), body: .unsupported)
        #expect(TaskScoreCardText.accessibilityLines(unsupported, zh) == [zh("task.unsupported")])
    }

    // MARK: Motion

    @Test func climbsCountUpAndFallsCrossfade() {
        #expect(TaskScoreMotion.climbSeconds(40) == 0.78)
        #expect(TaskScoreMotion.climbSeconds(60) == 0.8)
        #expect(TaskScoreMotion.change(from: 41, to: 81, style: .countUp, reduceMotion: false) == .countUp(seconds: 0.78))
        #expect(TaskScoreMotion.change(from: 41, to: 43, style: .countUp, reduceMotion: false) == .replace)
        #expect(TaskScoreMotion.change(from: 41, to: 60, style: .roll, reduceMotion: false) == .roll)
        #expect(TaskScoreMotion.change(from: 41, to: 60, style: .inPlace, reduceMotion: false) == .replace)
        #expect(TaskScoreMotion.change(from: 81, to: 34, style: .countUp, reduceMotion: false) == .crossfade(seconds: 0.2))
        #expect(TaskScoreMotion.change(from: nil, to: 9, style: .countUp, reduceMotion: false) == .crossfade(seconds: 0.2))
        #expect(TaskScoreMotion.change(from: 41, to: 81, style: .countUp, reduceMotion: true) == .crossfade(seconds: 0.15))
        #expect(TaskScoreMotion.change(from: 41, to: 41, style: .countUp, reduceMotion: false) == .none)
    }

    @Test func aCountUpPassesThroughEveryValueAndLands() {
        let values = stride(from: 0.0, through: 0.8, by: 1.0 / 60).map { TaskScoreMotion.countUpValue(from: 41, to: 81, elapsed: $0) }
        #expect(values.first == 41)
        #expect(values.last == 81)
        #expect(zip(values, values.dropFirst()).allSatisfy { $0 <= $1 })
        // Quick at first: past halfway within the first third of the time.
        #expect(TaskScoreMotion.countUpValue(from: 41, to: 81, elapsed: 0.26) > 61)
    }

    // MARK: Proof mark

    @Test func underlineDotsFollowTheFormula() {
        // 34 pt at 2×: d = 3.4 → 3.5 (device pixels), n over 40 pt = ⌊36.5 / 8.4⌋ + 1 = 5.
        let diameter = ProofMark.dotDiameter(textSize: 34, scale: 2, increasedContrast: false)
        #expect(diameter == 3.5)
        #expect(ProofMark.dotCount(width: 40, diameter: diameter) == 5)
        let centers = ProofMark.dotCenters(width: 40, diameter: diameter)
        #expect(centers.first == diameter / 2)
        #expect(abs((centers.last ?? 0) - (40 - diameter / 2)) < 1e-9)
        // The 7 pt floor at 1×: one device pixel; never fewer than two dots.
        #expect(ProofMark.dotDiameter(textSize: 7, scale: 1, increasedContrast: false) == 1)
        #expect(ProofMark.dotCount(width: 3, diameter: 1) == 2)
        // Increase Contrast: d + 0.5 pt.
        #expect(ProofMark.dotDiameter(textSize: 12, scale: 2, increasedContrast: true) == 1.5)
        #expect(ProofMark.dottedArcStyle(lineWidth: 3).dash == [0, 6])
    }
}
