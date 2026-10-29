import AgentSpeedCore
import CoreGraphics

/// The section's layout by its width (DESIGN 12, *Widths*): the history column's content width, card included.
enum TaskPerformanceWidth: Equatable, Sendable {
    case wide, medium, narrow

    static let wideFrom: CGFloat = 860
    static let mediumFrom: CGFloat = 600

    init(width: CGFloat) {
        self = width >= Self.wideFrom ? .wide : width >= Self.mediumFrom ? .medium : .narrow
    }

    /// The butterfly's centre column.
    var centre: CGFloat { self == .wide ? 140 : 120 }

    /// The side headers' distribution width (narrow: the card's full width).
    var distribution: CGFloat { self == .wide ? 120 : 96 }
}

/// What the section shows (DESIGN 12, *States*, *One source*).
enum TaskPerformanceMode: Equatable, Sendable {
    /// Re-analysing and no record yet: static placeholders.
    case analyzing
    /// No record in the range for any scored source.
    case empty
    /// One source full width; `onlyOne` when it is the only source with records (`history.tasks.onlyOne` replaces
    /// the pickers), false when the window's source picker chose it.
    case single(SourceKind, onlyOne: Bool)
    /// Two sources side by side, `first` on the left.
    case pair(TaskPerformancePair)
}

/// Resolves the mode, the shown pair and the pickers' choices from the window's input and the remembered pair.
struct TaskPerformanceResolution: Equatable, Sendable {
    let input: TaskPerformanceInput
    let remembered: TaskPerformancePair?

    /// The sources with at least one record, in source order.
    var sourcesWithRecords: [SourceKind] {
        Self.sourceOrdered(input.comparison.bySource.filter { $0.stats.tasks > 0 }.map(\.source))
    }

    var mode: TaskPerformanceMode {
        let sources = sourcesWithRecords
        if sources.isEmpty { return input.analyzing ? .analyzing : .empty }
        if let narrowed = input.narrowedTo {
            return sources.contains(narrowed) ? .single(narrowed, onlyOne: false) : .empty
        }
        if sources.count == 1 { return .single(sources[0], onlyOne: true) }
        return .pair(pair(sources))
    }

    /// The remembered pair while one of its sources has records; else Codex and Claude Code when both have records;
    /// else the two with the most tasks (ties by source order), in source order.
    private func pair(_ sources: [SourceKind]) -> TaskPerformancePair {
        if let remembered, remembered.first != remembered.second,
           sources.contains(remembered.first) || sources.contains(remembered.second) {
            return remembered
        }
        if sources.contains(.codex), sources.contains(.claudeCode) {
            return TaskPerformancePair(first: .codex, second: .claudeCode)
        }
        let tasks = { (source: SourceKind) in self.input.comparison.stats(for: source)?.tasks ?? 0 }
        let top = sources.enumerated()
            .sorted { lhs, rhs in
                let (a, b) = (tasks(lhs.element), tasks(rhs.element))
                return a != b ? a > b : lhs.offset < rhs.offset
            }
            .prefix(2)
            .map(\.element)
        let ordered = Self.sourceOrdered(top)
        return TaskPerformancePair(first: ordered[0], second: ordered[1])
    }

    /// A picker's sources: those with records in source order, leaving out the other side's choice. The side's own
    /// choice is kept even without records, so the pop-up can show it.
    func choices(for side: TaskPerformanceSide, in pair: TaskPerformancePair) -> [SourceKind] {
        let own = side == .first ? pair.first : pair.second
        let other = side == .first ? pair.second : pair.first
        var sources = sourcesWithRecords.filter { $0 != other }
        if !sources.contains(own) { sources.append(own) }
        return Self.sourceOrdered(sources)
    }

    /// The pair after one side picks `source`.
    static func picking(_ source: SourceKind, for side: TaskPerformanceSide, in pair: TaskPerformancePair) -> TaskPerformancePair {
        side == .first
            ? TaskPerformancePair(first: source, second: pair.second)
            : TaskPerformancePair(first: pair.first, second: source)
    }

    static func sourceOrdered(_ sources: [SourceKind]) -> [SourceKind] {
        sources.sorted { order($0) < order($1) }
    }

    static func order(_ source: SourceKind) -> Int {
        SourceKind.allCases.firstIndex(of: source) ?? Int.max
    }
}

enum TaskPerformanceSide: Equatable, Sendable {
    case first, second
}
