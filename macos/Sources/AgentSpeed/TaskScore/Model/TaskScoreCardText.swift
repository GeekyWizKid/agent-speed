import AgentSpeedCore
import Foundation

/// One row of the card's 評分構成: what its trailing value shows and how far its bar fills.
struct TaskDimensionRow: Equatable, Sendable, Identifiable {
    enum Value: Equatable, Sendable {
        /// No verification result yet: `task.state.unverified` over a dotted track.
        case unverified
        /// `pending` V: `task.state.pending` and the dotted outline of the held credit.
        case pending(held: Double)
        /// E while none: `–` over a dotted track.
        case absent
        case value(Int)
    }

    var dimension: TaskScoreDimension
    var value: Value

    var id: Int { dimension.rawValue }

    /// The filled share of the bar (for `pending`, of the held outline).
    var fraction: Double {
        switch value {
        case .unverified, .absent: 0
        case .pending(let held): (held / Double(dimension.maximum)).clamped(to: 0...1)
        case .value(let value): (Double(value) / Double(dimension.maximum)).clamped(to: 0...1)
        }
    }

    /// A dotted track with no fill.
    var isUnproven: Bool { value == .unverified || value == .absent }
}

/// One segment of the composition bar, as long as its points.
struct TaskCompositionSegment: Equatable, Sendable {
    var dimension: TaskScoreDimension
    var points: Double
    /// The held V credit while `pending`, drawn as a dotted outline.
    var held: Bool
}

/// One line of the card's 行為 block.
struct TaskSignalLine: Equatable, Sendable, Identifiable {
    enum Kind: Sendable { case looping, repeating, oscillation, summary }

    var kind: Kind
    var text: String

    var id: String { "\(kind)" }
}

/// One prediction cell: the percentage, its label and the model's flag.
struct TaskPredictionCell: Equatable, Sendable, Identifiable {
    enum Kind: String, Sendable { case success, stall, fixFails }

    var kind: Kind
    var percent: String
    var label: String
    var flag: String
    var accessibility: String

    var id: String { kind.rawValue }
}

/// The card's texts (DESIGN 12, *Score card*), built from its model.
@MainActor
enum TaskScoreCardText {
    /// The eyebrow over the number: `phase.idle` while resting, `task.card.top` on the hero's card from N = 2.
    static func eyebrow(_ score: TaskScoreCardScore, _ l10n: Localizer) -> String? {
        var parts: [String] = []
        if score.form == .resting { parts.append(l10n("phase.idle")) }
        if score.topCount >= 2 { parts.append(l10n("task.card.top", score.topCount)) }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    /// The line under the number, by state.
    static func status(_ score: TaskScoreCardScore, _ l10n: Localizer) -> TaskStatusLine {
        switch score.state {
        case .none:
            return .formatted("task.status.none", check: nil, l10n)
        case .unverified:
            return .formatted("task.status.unverified", check: nil, l10n)
        case .failing:
            guard let failure = score.openFailure else { return .formatted("task.status.unverified", check: nil, l10n) }
            let check = TaskScoreText.checkName(failure.check, l10n)
            if failure.edited { return .formatted("task.status.failing.edited", check: check, l10n) }
            if failure.counted {
                return .formatted("task.status.failing", check: check, numbers: [failure.errors, failure.peak], l10n)
            }
            return .formatted("task.status.failing.noCount", check: check, l10n)
        case .passing:
            guard let pass = score.lastPass else { return TaskStatusLine(segments: []) }
            let check = TaskScoreText.checkName(pass.check, l10n)
            return .formatted(pass.buildOnly ? "task.status.passing.buildOnly" : "task.status.passing", check: check, l10n)
        case .pending:
            guard let pass = score.lastPass else { return TaskStatusLine(segments: []) }
            return .formatted("task.status.pending", check: TaskScoreText.checkName(pass.check, l10n), l10n)
        }
    }

    /// The five rows, values adding up to the shown score.
    nonisolated static func dimensionRows(_ score: TaskScoreCardScore) -> [TaskDimensionRow] {
        let shown = TaskScoreText.shownDimensions(score.dimensions, total: score.shown ?? 0)
        return TaskScoreDimension.allCases.map { dimension in
            let index = dimension.rawValue
            let value: TaskDimensionRow.Value
            if score.state.hasNoResult {
                if dimension == .efficiency {
                    value = score.state == .none ? .absent : .value(shown[index])
                } else {
                    value = .unverified
                }
            } else if dimension == .verify, score.state == .pending {
                value = .pending(held: score.heldCredit ?? Double(TaskScoreDimension.verify.maximum))
            } else {
                value = .value(shown[index])
            }
            return TaskDimensionRow(dimension: dimension, value: value)
        }
    }

    /// The composition bar's segments in the order V C R E K, empty ones left out.
    nonisolated static func composition(_ score: TaskScoreCardScore) -> [TaskCompositionSegment] {
        let shown = TaskScoreText.shownDimensions(score.dimensions, total: score.shown ?? 0)
        return TaskScoreDimension.allCases.compactMap { dimension in
            if dimension == .verify, score.state == .pending, let held = score.heldCredit, held > 0 {
                return TaskCompositionSegment(dimension: dimension, points: held, held: true)
            }
            let points = Double(shown[dimension.rawValue])
            return points > 0 ? TaskCompositionSegment(dimension: dimension, points: points, held: false) : nil
        }
    }

    /// The prediction cells while live with a score: success and stall, fix fails only while `failing`.
    static func predictions(_ score: TaskScoreCardScore, _ l10n: Localizer) -> [TaskPredictionCell] {
        guard score.showsPredictions, let predictions = score.predictions else { return [] }
        var cells = [
            cell(.success, predictions.success, l10n),
            cell(.stall, predictions.stall, l10n),
        ]
        if score.state == .failing, let fix = predictions.fixFails { cells.append(cell(.fixFails, fix, l10n)) }
        return cells
    }

    /// The model's flag under a percentage: calibrated with N, a prior with N/40, or a prior for another reason.
    static func flag(_ calibration: TaskCalibration, _ l10n: Localizer) -> String {
        if calibration.calibrated { return l10n("task.predict.cell.calibrated", calibration.tasks) }
        if calibration.tasks < TaskCalibration.minimumTasks {
            return l10n("task.predict.cell.prior", calibration.tasks, TaskCalibration.minimumTasks)
        }
        return l10n("task.predict.cell.priorOnly")
    }

    static func predictionNote(_ source: SourceKind, _ l10n: Localizer) -> String {
        l10n("task.predict.note", l10n(source.titleKey), TaskCalibration.minimumTasks)
    }

    private static func cell(_ kind: TaskPredictionCell.Kind, _ prediction: TaskScorePrediction,
                             _ l10n: Localizer) -> TaskPredictionCell {
        let spoken = l10n("a11y.task.predict.\(kind.rawValue)", TaskScoreText.percentWords(prediction.probability, l10n))
        let accessibility = prediction.calibration.calibrated
            ? l10n("a11y.task.predict.calibrated", spoken, prediction.calibration.tasks)
            : l10n("a11y.task.predict.prior", spoken)
        return TaskPredictionCell(kind: kind, percent: TaskScoreText.percent(prediction.probability),
                                  label: l10n("task.predict.\(kind.rawValue)"),
                                  flag: flag(prediction.calibration, l10n), accessibility: accessibility)
    }

    /// The 行為 lines: looping, repeating, oscillating edits, then the summary of recent actions.
    static func signals(_ score: TaskScoreCardScore, _ l10n: Localizer) -> [TaskSignalLine] {
        var lines: [TaskSignalLine] = []
        let signals = score.signals
        if let count = signals.looping { lines.append(TaskSignalLine(kind: .looping, text: l10n("work.looping", count))) }
        if let count = signals.repeating {
            lines.append(TaskSignalLine(kind: .repeating, text: l10n("work.repeating", count)))
        }
        if score.oscillations > 0 {
            lines.append(TaskSignalLine(kind: .oscillation, text: l10n("task.signal.oscillation", score.oscillations)))
        }
        if let novelty = signals.novelty {
            lines.append(TaskSignalLine(kind: .summary, text: l10n("work.summary", HistoryText.percent(novelty),
                                                                    signals.failed, signals.recovered)))
        }
        return lines
    }

    /// The card's content as the row's extra accessibility content, in DESIGN 12's order.
    static func accessibilityLines(_ model: TaskScoreCardModel, _ l10n: Localizer) -> [String] {
        guard let score = model.score else { return [l10n("task.unsupported")] + model.facts }
        var lines: [String] = []
        if let shown = score.shown, score.form != .none {
            let words = TaskScoreText.words(shown: shown, state: score.state, form: score.form, count: score.topCount, l10n)
            lines.append(l10n("a11y.task.score", words, l10n(score.state.titleKey)))
        } else {
            lines.append(l10n("intel.none"))
        }
        let status = status(score, l10n).plain
        if !status.isEmpty { lines.append(status) }
        for row in dimensionRows(score) {
            let label = l10n(row.dimension.titleKey)
            let spoken: String
            switch row.value {
            case .unverified: spoken = l10n("a11y.task.unverified", label)
            case .pending: spoken = l10n("a11y.task.pending", label)
            case .absent: spoken = l10n("mac.history.a11y.item", label, l10n("task.state.none"))
            case .value(let value): spoken = l10n("a11y.task.dim", label, value, row.dimension.maximum)
            }
            lines.append(spoken + l10n("mac.history.a11y.join") + l10n(row.dimension.meaningKey))
        }
        lines += predictions(score, l10n).map(\.accessibility)
        lines += signals(score, l10n).map(\.text)
        lines += model.facts
        lines.append(l10n("task.card.footnote"))
        return lines
    }
}
