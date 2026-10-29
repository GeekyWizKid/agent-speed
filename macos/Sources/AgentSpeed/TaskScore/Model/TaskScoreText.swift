import AgentSpeedCore
import Foundation

/// Numbers and words of the task score shared by every surface (DESIGN 12, *Values and forms*, *Accessibility*).
@MainActor
enum TaskScoreText {
    /// Rounded half away from zero, as METRICS 6.9 *Score* shows a score (`int(value + 0.5)` above zero).
    nonisolated static func rounded(_ value: Double) -> Int {
        guard value.isFinite else { return 0 }
        return value >= 0 ? Int(value + 0.5) : -Int(-value + 0.5)
    }

    /// The dimensions as integers that add up to the shown score: each rounded down, then the points still missing go
    /// to the largest fractional parts, ties in the order V C R E K (DESIGN 12, *Score card*).
    nonisolated static func shownDimensions(_ dimensions: TaskScoreDimensions, total: Int) -> [Int] {
        let values = TaskScoreDimension.allCases.map { max(0, dimensions[$0]) }
        var shown = values.map { Int($0.rounded(.down)) }
        let missing = total - shown.reduce(0, +)
        guard missing > 0 else { return shown }
        let order = values.indices.sorted { lhs, rhs in
            let a = values[lhs] - values[lhs].rounded(.down)
            let b = values[rhs] - values[rhs].rounded(.down)
            return a != b ? a > b : lhs < rhs
        }
        for index in order.prefix(missing) { shown[index] += 1 }
        return shown
    }

    /// The score words (DESIGN 12, *Accessibility*): the number as shown, wrapped for an unproven state, then for
    /// resting, then for a set's top of N agents. Nil while none (`intel.none` replaces the words).
    static func words(_ value: TaskScoreValue, _ l10n: Localizer) -> String? {
        guard value.form != .none, let shown = value.shown else { return nil }
        return words(shown: shown, state: value.state, form: value.form, count: value.count, l10n)
    }

    static func words(shown: Int, state: TaskScoreState?, form: TaskScoreForm, count: Int, _ l10n: Localizer) -> String {
        var words = String(shown)
        if state == .unverified { words = l10n("a11y.task.unverified", words) }
        if state == .pending { words = l10n("a11y.task.pending", words) }
        if form == .resting { words = l10n("a11y.task.idle", words) }
        if count >= 2 { words = l10n("a11y.task.top", words, count) }
        return words
    }

    /// A probability as the card shows it: a whole percent, `<1%` below 0.5 %, `>99%` from 99.5 %.
    nonisolated static func percent(_ probability: Double) -> String {
        let value = probability * 100
        if !(value >= 0.5) { return "<1%" }
        if value >= 99.5 { return ">99%" }
        return "\(rounded(value))%"
    }

    /// The same, spoken: the clamps as `a11y.task.below` / `a11y.task.above`.
    static func percentWords(_ probability: Double, _ l10n: Localizer) -> String {
        let value = probability * 100
        if !(value >= 0.5) { return l10n("a11y.task.below", 1) }
        if value >= 99.5 { return l10n("a11y.task.above", 99) }
        return percent(probability)
    }

    /// A check's display name: its suite (drawn monospaced), else `task.check.*` by class.
    static func checkName(_ check: TaskCheckName, _ l10n: Localizer) -> TaskStatusLine.Segment {
        if let suite = check.suite { return .check(suite) }
        return .text(l10n(check.kind.titleKey))
    }
}

/// A line whose check name is drawn apart from the words around it (the card's status line).
struct TaskStatusLine: Equatable, Sendable {
    enum Segment: Equatable, Sendable {
        case text(String)
        /// A fixed suite name, drawn monospaced.
        case check(String)

        var string: String {
            switch self {
            case .text(let text), .check(let text): text
            }
        }
    }

    var segments: [Segment]

    var plain: String { segments.map(\.string).joined() }

    /// `key` formatted with `check` in place of its `%@` and `numbers` for its `%d`s, kept in segments.
    @MainActor
    static func formatted(_ key: String, check: Segment?, numbers: [Int] = [], _ l10n: Localizer) -> TaskStatusLine {
        let marker = "\u{E000}"
        let template = Localizer.string(key, language: l10n.language)
        var arguments: [any CVarArg] = []
        switch check {
        case .check: arguments.append(marker)
        case .text(let name): arguments.append(name)
        case nil: break
        }
        arguments += numbers
        let text = String(format: template, locale: l10n.locale, arguments: arguments)
        guard let check, case .check = check else { return TaskStatusLine(segments: [.text(text)]) }
        var segments: [Segment] = []
        let parts = text.components(separatedBy: marker)
        for (index, part) in parts.enumerated() {
            if !part.isEmpty { segments.append(.text(part)) }
            if index < parts.count - 1 { segments.append(check) }
        }
        return TaskStatusLine(segments: segments)
    }
}
