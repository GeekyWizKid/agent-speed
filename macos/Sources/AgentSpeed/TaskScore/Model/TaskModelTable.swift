import AgentSpeedCore
import Foundation

/// A column of the by-model table (DESIGN 12, *By model*).
enum TaskModelColumn: String, CaseIterable, Sendable {
    case model, tasks, scored, scoredOfTasks, median, success, stall, fixFail, attempts, firstPass

    var metric: TaskPerformanceMetric? { TaskPerformanceMetric(rawValue: rawValue) }

    /// The header as shown: the `.short` names.
    var headerKey: String {
        switch self {
        case .model: "history.tasks.column.model"
        case .tasks: "history.tasks.metric.tasks"
        case .scored: "history.tasks.metric.scored.short"
        case .scoredOfTasks: "history.tasks.column.scoredOfTasks"
        case .median: "history.tasks.metric.median.short"
        case .success, .stall, .fixFail, .attempts, .firstPass: metric!.shortKey
        }
    }

    /// The full name: the header's tooltip and accessibility label.
    var fullKey: String {
        switch self {
        case .model: "history.tasks.column.model"
        case .tasks: "history.tasks.metric.tasks"
        case .scored: "history.tasks.metric.scored"
        case .scoredOfTasks: "history.tasks.column.scoredOfTasks"
        case .median: "history.tasks.metric.median"
        case .success, .stall, .fixFail, .attempts, .firstPass: metric!.titleKey
        }
    }

    var isNumeric: Bool { self != .model }

    /// A first click sorts numbers largest first and model ids A→Z.
    var defaultDescending: Bool { self != .model }

    /// Wide shows tasks and scored apart; medium merges them into `96 / 128`.
    static func visible(_ width: TaskPerformanceWidth) -> [TaskModelColumn] {
        width == .wide
            ? allCases.filter { $0 != .scoredOfTasks }
            : allCases.filter { $0 != .tasks && $0 != .scored }
    }

    /// The columns of copied text, whatever the layout.
    static let copied: [TaskModelColumn] = allCases.filter { $0 != .scoredOfTasks }
}

struct TaskModelSort: Equatable, Sendable {
    var column: TaskModelColumn = .tasks
    var descending = true

    static let standard = TaskModelSort()

    func toggled(_ column: TaskModelColumn) -> TaskModelSort {
        column == self.column
            ? TaskModelSort(column: column, descending: !descending)
            : TaskModelSort(column: column, descending: column.defaultDescending)
    }
}

/// Sorting, cell text, copied text and accessibility of the by-model table.
@MainActor
enum TaskModelTable {
    /// The default order (METRICS 6.11): tasks descending, then the id's UTF-8 bytes.
    nonisolated static func standardOrder(_ lhs: TaskPerformanceModelRow, _ rhs: TaskPerformanceModelRow) -> Bool {
        if lhs.stats.tasks != rhs.stats.tasks { return lhs.stats.tasks > rhs.stats.tasks }
        return lhs.model.utf8.lexicographicallyPrecedes(rhs.model.utf8)
    }

    /// Rows with a value in the column, in the chosen order with ties in the default order, then the rows without
    /// one (an unknown model's id, a null value) in the default order.
    nonisolated static func sort(_ rows: [TaskPerformanceModelRow], by sort: TaskModelSort) -> [TaskPerformanceModelRow] {
        if sort.column == .model {
            let present = rows.filter { !$0.model.isEmpty }.sorted { lhs, rhs in
                guard lhs.model != rhs.model else { return standardOrder(lhs, rhs) }
                let ascending = lhs.model.utf8.lexicographicallyPrecedes(rhs.model.utf8)
                return sort.descending ? !ascending : ascending
            }
            return present + rows.filter(\.model.isEmpty).sorted(by: standardOrder)
        }
        let keyed = rows.map { ($0, key($0, sort.column)) }
        let present = keyed.compactMap { row, key in key.map { (row, $0) } }.sorted { lhs, rhs in
            guard lhs.1 != rhs.1 else { return standardOrder(lhs.0, rhs.0) }
            let ascending = lhs.1.lexicographicallyPrecedes(rhs.1)
            return sort.descending ? !ascending : ascending
        }
        let missing = keyed.filter { $0.1 == nil }.map(\.0).sorted(by: standardOrder)
        return present.map(\.0) + missing
    }

    private nonisolated static func key(_ row: TaskPerformanceModelRow, _ column: TaskModelColumn) -> [Double]? {
        let stats = row.stats
        switch column {
        case .model: return nil
        case .tasks: return [Double(stats.tasks)]
        case .scored: return [Double(stats.scoredTasks)]
        case .scoredOfTasks: return [Double(stats.scoredTasks), Double(stats.tasks)]
        case .median: return stats.medianScore.map { [$0] }
        case .success, .stall, .fixFail, .attempts, .firstPass:
            return column.metric?.value(stats).map { [$0] }
        }
    }

    static func cell(_ row: TaskPerformanceModelRow, _ column: TaskModelColumn, _ l10n: Localizer) -> String {
        let stats = row.stats
        switch column {
        case .model: return TaskPerformanceText.modelName(row, l10n)
        case .tasks: return HistoryText.fullNumber(stats.tasks, l10n)
        case .scored: return HistoryText.fullNumber(stats.scoredTasks, l10n)
        case .scoredOfTasks:
            return "\(HistoryText.fullNumber(stats.scoredTasks, l10n)) / \(HistoryText.fullNumber(stats.tasks, l10n))"
        case .median: return TaskPerformanceText.median(stats)
        case .success, .stall, .fixFail, .attempts, .firstPass:
            return TaskPerformanceText.shown(column.metric!, stats, l10n)
        }
    }

    /// A cell's tooltip: the model's full id over its sources (a long id is truncated in the middle), a rate's counts,
    /// a null value's reason.
    static func tooltip(_ row: TaskPerformanceModelRow, _ column: TaskModelColumn, _ l10n: Localizer) -> String? {
        switch column {
        case .model: "\(TaskPerformanceText.modelName(row, l10n))\n\(TaskPerformanceText.modelSources(row.sources, l10n))"
        case .success, .stall, .fixFail, .attempts, .firstPass: TaskPerformanceText.tooltip(column.metric!, row.stats, l10n)
        case .tasks, .scored, .scoredOfTasks, .median: nil
        }
    }

    /// A cell as accessibility reads it: `–` as its tooltip, the median with its middle half.
    static func spoken(_ row: TaskPerformanceModelRow, _ column: TaskModelColumn, _ l10n: Localizer) -> String {
        switch column {
        case .median:
            let median = TaskPerformanceText.median(row.stats)
            guard median != TaskPerformanceText.absent else { return median }
            return HistoryText.join([median, TaskPerformanceText.iqrWords(row.stats, l10n) ?? ""], l10n)
        case .success, .stall, .fixFail, .attempts, .firstPass:
            return TaskPerformanceText.spoken(column.metric!, row.stats, l10n)
        default:
            return cell(row, column, l10n)
        }
    }

    /// The row's name for accessibility: the model, its sources and the low-sample chip.
    static func accessibleLabel(_ row: TaskPerformanceModelRow, _ l10n: Localizer) -> String {
        HistoryText.join([
            TaskPerformanceText.modelName(row, l10n),
            TaskPerformanceText.modelSources(row.sources, l10n),
            TaskPerformanceText.isLowSample(row.stats) ? l10n("history.tasks.sample.low") : "",
        ], l10n)
    }

    /// Every other column, each labelled with its full name.
    static func accessibleValue(_ row: TaskPerformanceModelRow, columns: [TaskModelColumn], _ l10n: Localizer) -> String {
        HistoryText.join(columns.filter { $0 != .model }.map { column in
            HistoryText.labelled(l10n(column.fullKey), spoken(row, column, l10n), l10n)
        }, l10n)
    }

    /// Rows as tab-separated text under a header line of the full names, as the daily table copies.
    static func copyText(_ rows: [TaskPerformanceModelRow], _ l10n: Localizer) -> String {
        let header = TaskModelColumn.copied.map { l10n($0.fullKey) }.joined(separator: "\t")
        let lines = rows.map { row in
            TaskModelColumn.copied.map { cell(row, $0, l10n) }.joined(separator: "\t")
        }
        return ([header] + lines).joined(separator: "\n")
    }
}

/// The by-model table's column widths over the width of a row's content (DESIGN 12, *By model*): numbers in fixed
/// columns, the model in the rest. When the model would get less than `minModel`, the gaps tighten and the number
/// columns shrink (at most to 85 %) before the model column gives way.
enum TaskModelColumnLayout {
    static let minModel: CGFloat = 140

    static func base(_ column: TaskModelColumn, _ width: TaskPerformanceWidth) -> CGFloat {
        let wide = width == .wide
        switch column {
        case .model: return 0
        case .tasks: return 50
        case .scored: return 54
        case .scoredOfTasks: return 62
        case .median: return wide ? 88 : 70
        case .success: return wide ? 60 : 54
        case .stall: return wide ? 56 : 48
        case .fixFail: return wide ? 72 : 62
        case .attempts: return wide ? 66 : 62
        case .firstPass: return wide ? 80 : 62
        }
    }

    /// The strip of the median cell: 48 pt wide, 36 pt in medium.
    static func strip(_ width: TaskPerformanceWidth) -> CGFloat {
        width == .wide ? 48 : 36
    }

    static func layout(_ columns: [TaskModelColumn], width: TaskPerformanceWidth,
                       available: CGFloat) -> (widths: [CGFloat], gap: CGFloat) {
        var gap: CGFloat = width == .wide ? 10 : 8
        let numbers = columns.filter(\.isNumeric).map { base($0, width) }
        let total = numbers.reduce(0, +)
        let gaps = { (gap: CGFloat) in gap * CGFloat(max(columns.count - 1, 0)) }
        var factor: CGFloat = 1
        if available - total - gaps(gap) < minModel {
            gap = 6
            factor = max(0.85, min(1, (available - gaps(gap) - minModel) / max(total, 1)))
        }
        var widths = columns.map { $0.isNumeric ? (base($0, width) * factor).rounded(.down) : 0 }
        let model = max(0, (available - widths.reduce(0, +) - gaps(gap)).rounded(.down))
        widths = zip(columns, widths).map { $0.isNumeric ? $1 : model }
        return (widths, gap)
    }
}
