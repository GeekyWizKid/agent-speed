import AgentSpeedCore
import SwiftUI

/// Usage History 3c, 任務表現 (DESIGN 12, *Usage History: task performance*): two sources compared side by side — or
/// one source full width — with their models, over the window's range. It lays itself out by the width it is offered
/// (the history column's content width): wide from 860, medium from 600, narrow below; nothing scrolls sideways.
struct TaskPerformanceSection: View {
    let input: TaskPerformanceInput
    /// The remembered pair; the window persists it.
    @Binding var pair: TaskPerformancePair?
    let l10n: Localizer
    var reduceMotion = false

    @State private var width: CGFloat = 872
    @State private var sort = TaskModelSort.standard

    var body: some View {
        let resolution = TaskPerformanceResolution(input: input, remembered: pair)
        let mode = resolution.mode
        let layout = TaskPerformanceWidth(width: width)
        VStack(alignment: .leading, spacing: 0) {
            TaskPerformanceHeader(accessory: accessory(mode, resolution), width: layout, l10n: l10n) { side, source in
                guard case .pair(let shown) = mode else { return }
                pair = TaskPerformanceResolution.picking(source, for: side, in: shown)
            }
            content(mode, layout)
                .transition(.opacity)
                .id(ModeKind(mode))
        }
        .animation(TaskPerformanceMotion.fade, value: ModeKind(mode))
        .padding(.horizontal, TaskPerformanceStyle.horizontalPadding)
        .padding(.top, TaskPerformanceStyle.topPadding)
        .padding(.bottom, TaskPerformanceStyle.bottomPadding)
        // A zero minimum makes the measured width the offered one, never the content's, so a layout chosen for a
        // wider column cannot hold the section wide.
        .frame(minWidth: 0, maxWidth: .infinity, alignment: .leading)
        .historyCard()
        .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { measured in
            if abs(measured - width) > 0.5 { width = measured }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(l10n("history.tasks.title"))
        .environment(\.locale, l10n.locale)
    }

    private func accessory(_ mode: TaskPerformanceMode, _ resolution: TaskPerformanceResolution) -> TaskPerformanceHeader.Accessory {
        switch mode {
        case .analyzing, .empty: .none
        case .single(let source, let onlyOne): onlyOne ? .onlyOne(source) : .none
        case .pair(let shown):
            .pickers(shown, first: resolution.choices(for: .first, in: shown), second: resolution.choices(for: .second, in: shown))
        }
    }

    @ViewBuilder
    private func content(_ mode: TaskPerformanceMode, _ layout: TaskPerformanceWidth) -> some View {
        let comparison = input.comparison
        switch mode {
        case .analyzing:
            TaskPerformancePlaceholder(width: layout, l10n: l10n)
                .padding(.top, Spacing.m)
        case .empty:
            TaskPerformanceEmptyState(l10n: l10n)
                .padding(.top, Spacing.xs)
        case .single(let source, _):
            VStack(alignment: .leading, spacing: 0) {
                TaskSingleSource(source: source, stats: comparison.stats(for: source), width: layout, l10n: l10n,
                                 reduceMotion: reduceMotion)
                    .padding(.top, Spacing.m)
                models(comparison.byModel, layout)
                footnote
            }
        case .pair(let shown):
            VStack(alignment: .leading, spacing: 0) {
                Group {
                    if layout == .narrow {
                        TaskCompactComparison(pair: shown, comparison: comparison, l10n: l10n, reduceMotion: reduceMotion)
                            .padding(.top, Spacing.s)
                    } else {
                        TaskButterfly(pair: shown, comparison: comparison, width: layout, l10n: l10n,
                                      reduceMotion: reduceMotion)
                            .padding(.top, Spacing.m)
                    }
                }
                models(comparison.byModel, layout)
                footnote
            }
        }
    }

    @ViewBuilder
    private func models(_ rows: [TaskPerformanceModelRow], _ layout: TaskPerformanceWidth) -> some View {
        if !rows.isEmpty {
            Text(l10n("history.tasks.byModel"))
                .font(HistoryFont.sectionTitle)
                .accessibilityAddTraits(.isHeader)
                .padding(.top, Spacing.m)
                .padding(.bottom, layout == .narrow ? 2 : Spacing.xxs)
            if layout == .narrow {
                TaskModelListView(rows: rows, l10n: l10n)
            } else {
                TaskModelTableView(rows: rows, width: layout, sort: $sort, l10n: l10n)
            }
        }
    }

    private var footnote: some View {
        Text(l10n("history.tasks.footnote"))
            .font(TaskPerformanceFont.footnote)
            .foregroundStyle(.tertiary)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.top, Spacing.s)
    }
}

/// The mode without its data: a change of kind crossfades the section's content.
private enum ModeKind: Hashable {
    case analyzing, empty, single, pair

    init(_ mode: TaskPerformanceMode) {
        switch mode {
        case .analyzing: self = .analyzing
        case .empty: self = .empty
        case .single: self = .single
        case .pair: self = .pair
        }
    }
}
