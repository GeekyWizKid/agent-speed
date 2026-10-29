import Foundation

/// The five compared values of the 任務表現 section, in display order (DESIGN 12, *Comparison rows*).
enum TaskPerformanceMetric: String, CaseIterable, Sendable {
    case success, stall, fixFail, attempts, firstPass

    /// Fewer than this many scored tasks is a low sample: the chip shows and no side is marked better.
    static let lowSample = 10

    var titleKey: String { "history.tasks.metric.\(rawValue)" }
    var hintKey: String { "\(titleKey).hint" }

    /// The table header: the `.short` name where one exists, else the full name.
    var shortKey: String {
        switch self {
        case .attempts, .firstPass: "\(titleKey).short"
        case .success, .stall, .fixFail: titleKey
        }
    }

    var lowerIsBetter: Bool { self != .success }

    var isRate: Bool { self == .success || self == .stall || self == .fixFail }

    /// The unrounded value: rates as ratios, attempts as a mean, the first pass in milliseconds.
    func value(_ stats: TaskPerformanceStats) -> Double? {
        let value: Double? = switch self {
        case .success: stats.successRate
        case .stall: stats.stallRate
        case .fixFail: stats.fixFailureRate
        case .attempts: stats.recoverAttempts
        case .firstPass: stats.timeToFirstPassMs
        }
        guard let value, value.isFinite else { return nil }
        return value
    }

    /// The tooltip of a null value without its own counts (`history.tasks.noValue.*`).
    var noValueKey: String? {
        switch self {
        case .attempts: "history.tasks.noValue.attempts"
        case .firstPass: "history.tasks.noValue.firstPass"
        case .success, .stall, .fixFail: nil
        }
    }
}

/// The metrics the ⓘ popover defines, each with its `.hint`.
enum TaskPerformanceDefinition {
    static let keys = [
        "history.tasks.metric.scored", "history.tasks.metric.median", "history.tasks.metric.success",
        "history.tasks.metric.stall", "history.tasks.metric.fixFail", "history.tasks.metric.attempts",
        "history.tasks.metric.firstPass",
    ]
}
