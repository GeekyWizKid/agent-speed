import AgentSpeedCore
import Foundation

/// Numbers, scales, the better rule and the words of the 任務表現 section (DESIGN 12, *Usage History: task
/// performance*; METRICS 6.11, *Comparison*).
@MainActor
enum TaskPerformanceText {
    /// The app's absent mark.
    static let absent = "–"
    private static let posix = Locale(identifier: "en_US_POSIX")

    // MARK: Numbers

    /// Binary noise removed before rounding (`0.145 × 100` is `14.4999…`), so a value that is exactly on a half in
    /// decimal rounds away from zero as written.
    nonisolated static func settled(_ value: Double) -> Double {
        (value * 1e9).rounded() / 1e9
    }

    /// A ratio as a whole percent, rounded half away from zero.
    nonisolated static func wholePercent(_ ratio: Double) -> Int {
        TaskScoreText.rounded(settled(ratio * 100))
    }

    nonisolated static func oneDecimal(_ value: Double) -> Double {
        Double(TaskScoreText.rounded(settled(value * 10))) / 10
    }

    /// A score as shown: an integer rounded half away from zero.
    nonisolated static func score(_ value: Double) -> Int {
        TaskScoreText.rounded(settled(value))
    }

    static func rate(_ ratio: Double) -> String {
        "\(wholePercent(ratio))%"
    }

    /// `1.4 次` / `1.4`
    static func attempts(_ value: Double, _ l10n: Localizer) -> String {
        l10n("history.tasks.attempts", String(format: "%.1f", locale: posix, oneDecimal(value)))
    }

    /// Seconds below a minute, whole minutes below an hour, then hours and minutes; each rounded half away from zero
    /// from the milliseconds, so 59.6 s reads `1 分鐘`.
    static func duration(ms: Double, _ l10n: Localizer) -> String {
        let ms = max(0, ms)
        let seconds = TaskScoreText.rounded(settled(ms / 1000))
        if seconds < 60 { return l10n("time.seconds", seconds) }
        let minutes = max(1, TaskScoreText.rounded(settled(ms / 60_000)))
        if minutes < 60 { return l10n("time.minutes", minutes) }
        let rest = minutes % 60
        return rest == 0 ? l10n("time.hours", minutes / 60) : l10n("time.hoursMinutes", minutes / 60, rest)
    }

    /// A metric's value as shown, nil when it is null or the side has no records.
    static func value(_ metric: TaskPerformanceMetric, _ stats: TaskPerformanceStats?, _ l10n: Localizer) -> String? {
        guard let stats, stats.tasks > 0, let value = metric.value(stats) else { return nil }
        switch metric {
        case .success, .stall, .fixFail: return rate(value)
        case .attempts: return attempts(value, l10n)
        case .firstPass: return duration(ms: value, l10n)
        }
    }

    static func shown(_ metric: TaskPerformanceMetric, _ stats: TaskPerformanceStats?, _ l10n: Localizer) -> String {
        value(metric, stats, l10n) ?? absent
    }

    /// The median score as shown, `–` without a scored task.
    static func median(_ stats: TaskPerformanceStats?) -> String {
        guard let stats, stats.tasks > 0, let median = stats.medianScore, median.isFinite else { return absent }
        return String(score(median))
    }

    // MARK: Scales and the better rule

    /// The value at a full track: rates 0–100 %; attempts and times the larger value × 1.25, at least 3 attempts or
    /// 5 minutes.
    nonisolated static func scale(_ metric: TaskPerformanceMetric, _ a: Double?, _ b: Double? = nil) -> Double {
        let larger = max(a ?? 0, b ?? 0)
        switch metric {
        case .success, .stall, .fixFail: return 1
        case .attempts: return max(3, larger * 1.25)
        case .firstPass: return max(300_000, larger * 1.25)
        }
    }

    nonisolated static func fraction(_ value: Double?, scale: Double) -> Double {
        guard let value, value.isFinite, scale > 0 else { return 0 }
        return (value / scale).clamped(to: 0...1)
    }

    /// The side marked better, or nil (DESIGN 12, *Better*): both sides with at least 10 scored tasks and values that
    /// differ by at least 1 percentage point (rates) or 5 % of the larger value (attempts, times). Two values that read
    /// the same once rounded for display are never told apart.
    static func better(_ metric: TaskPerformanceMetric, _ first: TaskPerformanceStats?, _ second: TaskPerformanceStats?,
                       _ l10n: Localizer) -> TaskPerformanceSide? {
        guard let first, let second,
              first.scoredTasks >= TaskPerformanceMetric.lowSample, second.scoredTasks >= TaskPerformanceMetric.lowSample,
              let a = metric.value(first), let b = metric.value(second) else { return nil }
        let reaches: Bool
        if metric.isRate {
            reaches = settled(abs(a - b) * 100) >= 1
        } else {
            let larger = max(a, b)
            reaches = larger > 0 && settled(abs(a - b) / larger) >= 0.05
        }
        guard reaches, value(metric, first, l10n) != value(metric, second, l10n) else { return nil }
        return (metric.lowerIsBetter ? a < b : a > b) ? .first : .second
    }

    // MARK: Tooltips

    /// A value's tooltip: a rate's counts; a null value's reason; `history.tasks.side.empty` for a side without
    /// records.
    static func tooltip(_ metric: TaskPerformanceMetric, _ stats: TaskPerformanceStats?, _ l10n: Localizer) -> String? {
        guard let stats, stats.tasks > 0 else { return l10n("history.tasks.side.empty") }
        switch metric {
        case .success:
            return stats.successes.map { l10n("history.tasks.rate.scored", $0, stats.scoredTasks) }
        case .stall:
            return stats.stalled.map { l10n("history.tasks.rate.tasks", $0, stats.tasks) }
        case .fixFail:
            guard let failures = stats.fixFailures, let attempts = stats.fixAttempts else { return nil }
            return l10n("history.tasks.rate.fixes", failures, attempts)
        case .attempts, .firstPass:
            guard metric.value(stats) == nil, let key = metric.noValueKey else { return nil }
            return l10n(key)
        }
    }

    /// A value as accessibility reads it: as shown, and `–` as its tooltip's text.
    static func spoken(_ metric: TaskPerformanceMetric, _ stats: TaskPerformanceStats?, _ l10n: Localizer) -> String {
        value(metric, stats, l10n) ?? tooltip(metric, stats, l10n) ?? absent
    }

    static func isLowSample(_ stats: TaskPerformanceStats) -> Bool {
        stats.scoredTasks < TaskPerformanceMetric.lowSample
    }

    /// The low-sample chip's tooltip.
    static func sampleHint(_ stats: TaskPerformanceStats, _ l10n: Localizer) -> String {
        l10n("history.tasks.sample.hint", stats.scoredTasks, TaskPerformanceMetric.lowSample)
    }

    /// `96 個已評分 · 共 128 個任務`
    static func counts(_ stats: TaskPerformanceStats, _ l10n: Localizer) -> String {
        l10n("history.tasks.counts", stats.scoredTasks, stats.tasks)
    }

    static func modelName(_ row: TaskPerformanceModelRow, _ l10n: Localizer) -> String {
        row.model.isEmpty ? l10n("cost.noModelName") : row.model
    }

    /// `來源：Codex、Claude Code`
    static func modelSources(_ sources: [SourceKind], _ l10n: Localizer) -> String {
        l10n("history.tasks.model.sources", HistoryText.sourceList(sources, l10n))
    }

    static func onlyOne(_ source: SourceKind, _ l10n: Localizer) -> String {
        l10n("history.tasks.onlyOne", l10n(source.titleKey))
    }

    // MARK: Accessibility

    /// The metric's name, wrapped in `a11y.history.tasks.lowerBetter` where lower is better.
    static func metricWords(_ metric: TaskPerformanceMetric, _ l10n: Localizer) -> String {
        let title = l10n(metric.titleKey)
        return metric.lowerIsBetter ? l10n("a11y.history.tasks.lowerBetter", title) : title
    }

    /// One comparison row: the metric, then each source with its value as shown, wrapped in
    /// `a11y.history.tasks.better` when a side is marked.
    static func compareWords(_ metric: TaskPerformanceMetric, pair: TaskPerformancePair,
                             first: TaskPerformanceStats?, second: TaskPerformanceStats?, _ l10n: Localizer) -> String {
        let words = l10n("a11y.history.tasks.compare", metricWords(metric, l10n),
                         l10n(pair.first.titleKey), spoken(metric, first, l10n),
                         l10n(pair.second.titleKey), spoken(metric, second, l10n))
        switch better(metric, first, second, l10n) {
        case .first: return l10n("a11y.history.tasks.better", words, l10n(pair.first.titleKey))
        case .second: return l10n("a11y.history.tasks.better", words, l10n(pair.second.titleKey))
        case nil: return words
        }
    }

    /// One bar of the single-source layout: `成功率：78%`.
    static func singleWords(_ metric: TaskPerformanceMetric, _ stats: TaskPerformanceStats?, _ l10n: Localizer) -> String {
        HistoryText.labelled(metricWords(metric, l10n), spoken(metric, stats, l10n), l10n)
    }

    /// A side header: `Codex：128 個任務，96 個已評分，中位數 71`, or the source with `history.tasks.side.empty`.
    static func sideWords(_ source: SourceKind, _ stats: TaskPerformanceStats?, _ l10n: Localizer) -> String {
        let name = l10n(source.titleKey)
        guard let stats, stats.tasks > 0 else { return HistoryText.labelled(name, l10n("history.tasks.side.empty"), l10n) }
        return l10n("a11y.history.tasks.side", name, stats.tasks, stats.scoredTasks, median(stats))
    }

    /// The distribution image's label with the quartiles and the median as shown integers.
    static func distributionWords(_ stats: TaskPerformanceStats, _ l10n: Localizer) -> String? {
        guard let q1 = stats.lowerQuartile, let median = stats.medianScore, let q3 = stats.upperQuartile else { return nil }
        return l10n("a11y.history.tasks.distribution", String(score(q1)), String(score(median)), String(score(q3)))
    }

    /// The median cell's strip: `中間一半的任務在 58 到 81 分之間`.
    static func iqrWords(_ stats: TaskPerformanceStats, _ l10n: Localizer) -> String? {
        guard let q1 = stats.lowerQuartile, let q3 = stats.upperQuartile else { return nil }
        return l10n("a11y.history.tasks.iqr", String(score(q1)), String(score(q3)))
    }

    // MARK: Distribution

    /// A score's bin: the number of the edges 10 … 90 it reaches, compared exactly (METRICS 6.11).
    nonisolated static func bin(of score: Double) -> Int {
        stride(from: 10.0, through: 90.0, by: 10).reduce(0) { $0 + (score >= $1 ? 1 : 0) }
    }

    /// The bin holding the median, drawn at full strength.
    nonisolated static func medianBin(_ stats: TaskPerformanceStats) -> Int? {
        stats.medianScore.map(bin(of:))
    }
}
