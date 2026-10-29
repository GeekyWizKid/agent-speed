import AgentSpeedCore
import Foundation

/// Sample score cards in every state (DESIGN 12), modelled on the prototype's agents.
@MainActor
enum TaskScoreCardPreviewData {
    struct Case {
        var name: String
        var model: TaskScoreCardModel
    }

    static func cases(_ l10n: Localizer) -> [Case] {
        let effort = { (value: String) in l10n("effort.tooltip", value) }
        let speed = l10n("speed.tooltip", l10n("speed.value.fast"))
        let calibrated212 = TaskCalibration(calibrated: true, tasks: 212)
        let calibrated54 = TaskCalibration(calibrated: true, tasks: 54)
        let prior = { (tasks: Int) in TaskCalibration(calibrated: false, tasks: tasks) }
        func prediction(_ p: Double, _ calibration: TaskCalibration) -> TaskScorePrediction {
            TaskScorePrediction(probability: p, calibration: calibration)
        }
        func dims(_ v: Double, _ c: Double, _ r: Double, _ e: Double, _ k: Double) -> TaskScoreDimensions {
            TaskScoreDimensions(verify: v, converge: c, recover: r, efficiency: e, complete: k)
        }
        func header(_ source: SourceKind, _ project: String, _ model: String?, subs: Int = 0) -> TaskScoreCardModel.Header {
            .init(source: source, project: project, model: model, subAgents: subs)
        }

        var none = TaskScoreCardScore(form: .none, state: .none, score: nil)
        none.signals = .none

        var unverified = TaskScoreCardScore(form: .live, state: .unverified, score: 8.6, dimensions: dims(0, 0, 0, 8.6, 0))
        unverified.predictions = TaskScorePredictions(success: prediction(0.62, calibrated212),
                                                      stall: prediction(0.14, prior(64)), fixFails: nil)

        var failing = TaskScoreCardScore(form: .live, state: .failing, score: 41.2, dimensions: dims(0, 17.4, 9.3, 6.8, 7.7))
        failing.openFailure = TaskOpenFailureFacts(check: TaskCheckName(suite: "cargo test", kind: .test), errors: 3, peak: 9,
                                                   counted: true, edited: false)
        failing.predictions = TaskScorePredictions(success: prediction(0.38, calibrated54), stall: prediction(0.41, calibrated54),
                                                   fixFails: prediction(0.52, prior(23)))
        failing.signals = WorkSignals(looping: 4, novelty: 0.46, failed: 5, recovered: 1)
        failing.oscillations = 2

        var edited = TaskScoreCardScore(form: .live, state: .failing, score: 25.1, dimensions: dims(0, 14.2, 5, 5.9, 0))
        edited.openFailure = TaskOpenFailureFacts(check: TaskCheckName(suite: "go test", kind: .test), errors: 2, peak: 6,
                                                  counted: true, edited: true)
        edited.predictions = TaskScorePredictions(success: prediction(0.44, prior(12)), stall: prediction(0.18, prior(12)),
                                                  fixFails: prediction(0.35, prior(9)))
        edited.signals = WorkSignals(novelty: 0.71, failed: 3, recovered: 1)
        edited.oscillations = 1

        var passing = TaskScoreCardScore(form: .live, state: .passing, score: 85.7, dimensions: dims(40, 19.6, 11.2, 7.1, 8))
        passing.lastPass = TaskLastPassFacts(check: TaskCheckName(suite: "swift test", kind: .test), buildOnly: false)
        passing.predictions = TaskScorePredictions(success: prediction(0.96, calibrated212), stall: prediction(0.004, calibrated212),
                                                   fixFails: nil)
        passing.signals = WorkSignals(novelty: 0.82, failed: 2, recovered: 2)

        var buildOnly = TaskScoreCardScore(form: .live, state: .passing, score: 61.4, dimensions: dims(20, 25, 9.6, 6.8, 0))
        buildOnly.lastPass = TaskLastPassFacts(check: TaskCheckName(suite: nil, kind: .build), buildOnly: true)
        buildOnly.dimensions.complete = 0
        buildOnly.predictions = TaskScorePredictions(success: prediction(0.81, prior(31)), stall: prediction(0.07, prior(31)),
                                                     fixFails: nil)

        var pending = TaskScoreCardScore(form: .live, state: .pending, score: 34.3, dimensions: dims(0, 18.3, 10, 6, 0))
        pending.heldCredit = 40
        pending.lastPass = TaskLastPassFacts(check: TaskCheckName(suite: "pytest", kind: .test), buildOnly: false)
        pending.predictions = TaskScorePredictions(success: prediction(0.71, calibrated54), stall: prediction(0.09, calibrated54),
                                                   fixFails: nil)

        var resting = TaskScoreCardScore(form: .resting, state: .passing, score: 72.4, dimensions: dims(40, 14.4, 8, 6, 4))
        resting.topCount = 2
        resting.lastPass = TaskLastPassFacts(check: TaskCheckName(suite: "npm test", kind: .test), buildOnly: false)
        resting.signals = WorkSignals(novelty: 0.69, failed: 2, recovered: 1)

        return [
            Case(name: "none", model: .init(header: header(.omp, "lumen", "claude-opus-5-5"), body: .scored(none),
                                            facts: [effort("medium")])),
            Case(name: "unverified", model: .init(header: header(.claudeCode, "nova", "claude-sonnet-5-5"),
                                                  body: .scored(unverified), facts: [effort("medium")])),
            Case(name: "failing", model: .init(header: header(.codex, "rocket", "gpt-6.1-sol"), body: .scored(failing),
                                               facts: [effort("xhigh"), speed])),
            Case(name: "failing-edited", model: .init(header: header(.pi, "vega", "gpt-6.1-terra"), body: .scored(edited),
                                                      facts: [effort("high")])),
            Case(name: "passing", model: .init(header: header(.claudeCode, "agent-speed", "claude-opus-5-5", subs: 2),
                                               body: .scored(passing), facts: [effort("xhigh")])),
            Case(name: "build-only", model: .init(header: header(.claudeCode, "atlas-a-much-longer-project-name", "claude-sonnet-5-5"),
                                                  body: .scored(buildOnly), facts: [])),
            Case(name: "pending", model: .init(header: header(.codex, "quasar", "gpt-6.1-terra"), body: .scored(pending),
                                               facts: [effort("high"), speed])),
            Case(name: "resting", model: .init(header: header(.claudeDesktop, "orbit", "claude-fable-5-1"), body: .scored(resting),
                                               facts: [effort("medium")])),
            Case(name: "unsupported", model: .init(header: header(.deepseek, "tihu", "deepseek-v4-pro"), body: .unsupported,
                                                   facts: [effort("high")])),
        ]
    }
}
