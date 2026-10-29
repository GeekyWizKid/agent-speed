import AgentSpeedCore
import AppKit
import SwiftUI

/// Wide and medium: the by-model table in the daily table's style — 30 pt rows, alternating tint, hover highlight,
/// sortable headers with the chevron, copy row as text (DESIGN 12, *By model*).
struct TaskModelTableView: View {
    let rows: [TaskPerformanceModelRow]
    let width: TaskPerformanceWidth
    @Binding var sort: TaskModelSort
    let l10n: Localizer

    @State private var hovered: String?

    static let inset = DailyTableRow.inset

    var body: some View {
        let columns = TaskModelColumn.visible(width)
        let sorted = TaskModelTable.sort(rows, by: sort)
        let dotsWidth = TaskModelDots.width(rows.map(\.sources.count).max() ?? 1)
        VStack(alignment: .leading, spacing: 0) {
            TaskModelTableHeader(columns: columns, width: width, sort: sort, l10n: l10n) {
                sort = sort.toggled($0)
            }
            ForEach(Array(sorted.enumerated()), id: \.element.id) { index, row in
                TaskModelTableRow(row: row, index: index, columns: columns, width: width, dotsWidth: dotsWidth,
                                  hovered: hovered == row.id, l10n: l10n)
                    .onHover { inside in
                        if inside { hovered = row.id } else if hovered == row.id { hovered = nil }
                    }
            }
        }
    }
}

private struct TaskModelTableHeader: View {
    let columns: [TaskModelColumn]
    let width: TaskPerformanceWidth
    let sort: TaskModelSort
    let l10n: Localizer
    let onSort: (TaskModelColumn) -> Void

    var body: some View {
        TaskModelColumnsLayout(columns: columns, width: width, alignment: .bottom) {
            ForEach(columns, id: \.self) { column in
                header(column)
            }
        }
        .padding(.horizontal, TaskModelTableView.inset)
        .padding(.top, 2)
        .padding(.bottom, 7)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .bottom) { Hairline() }
    }

    private func header(_ column: TaskModelColumn) -> some View {
        let title = l10n(column.headerKey)
        let full = l10n(column.fullKey)
        let active = sort.column == column
        // As in the daily table: only the sorted column carries its chevron, away from the alignment edge.
        let indicator = Image(systemName: sort.descending ? "chevron.down" : "chevron.up")
            .font(.system(size: 8, weight: .bold))
        return Button {
            onSort(column)
        } label: {
            HStack(alignment: .lastTextBaseline, spacing: 3) {
                if column.isNumeric {
                    Spacer(minLength: 0)
                    if active { indicator }
                }
                Text(title)
                    .lineLimit(2)
                    .multilineTextAlignment(column.isNumeric ? .trailing : .leading)
                    .fixedSize(horizontal: false, vertical: true)
                if !column.isNumeric {
                    if active { indicator }
                    Spacer(minLength: 0)
                }
            }
            .font(HistoryFont.header)
            .foregroundStyle(active ? AnyShapeStyle(.primary) : AnyShapeStyle(.historySecondary))
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(full)
        .accessibilityLabel(full)
        .accessibilityValue(active ? l10n(sort.descending ? "mac.history.sorted.desc" : "mac.history.sorted.asc") : "")
        .accessibilityAddTraits(active ? [.isHeader, .isSelected] : .isHeader)
    }
}

private struct TaskModelTableRow: View {
    let row: TaskPerformanceModelRow
    let index: Int
    let columns: [TaskModelColumn]
    let width: TaskPerformanceWidth
    let dotsWidth: CGFloat
    let hovered: Bool
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let dark = colorScheme == .dark
        let striped = !index.isMultiple(of: 2)
        TaskModelColumnsLayout(columns: columns, width: width, alignment: .center) {
            ForEach(columns, id: \.self) { column in
                cell(column)
            }
        }
        .padding(.horizontal, TaskModelTableView.inset)
        .padding(.vertical, 3)
        .frame(minHeight: HistoryLayout.rowHeight)
        .fixedSize(horizontal: false, vertical: true)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background {
            RoundedRectangle(cornerRadius: 6, style: .continuous)
                .fill(hovered ? HistoryPalette.hover(dark: dark) : (striped ? HistoryPalette.stripe(dark: dark) : .clear))
        }
        .contentShape(Rectangle())
        .contextMenu {
            Button(l10n("mac.history.copy")) { TaskModelCopy.copy([row], l10n) }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(TaskModelTable.accessibleLabel(row, l10n))
        .accessibilityValue(TaskModelTable.accessibleValue(row, columns: columns, l10n))
    }

    @ViewBuilder
    private func cell(_ column: TaskModelColumn) -> some View {
        let low = TaskPerformanceText.isLowSample(row.stats)
        switch column {
        case .model:
            TaskModelNameCell(row: row, low: low, l10n: l10n, dotsWidth: dotsWidth)
        case .median:
            HStack(spacing: 7) {
                Text(TaskModelTable.cell(row, .median, l10n))
                    .font(HistoryFont.cell)
                    .foregroundStyle(row.stats.medianScore == nil ? AnyShapeStyle(.tertiary)
                        : (low ? AnyShapeStyle(.historySecondary) : AnyShapeStyle(.primary)))
                    .lineLimit(1)
                    .fixedSize()
                TaskIQRStrip(stats: row.stats, source: row.sources.first, width: TaskModelColumnLayout.strip(width))
            }
        default:
            let text = TaskModelTable.cell(row, column, l10n)
            Text(text)
                .font(HistoryFont.cell)
                .foregroundStyle(text == TaskPerformanceText.absent ? AnyShapeStyle(.tertiary)
                    : (low ? AnyShapeStyle(.historySecondary) : AnyShapeStyle(.primary)))
                .lineLimit(1)
                .minimumScaleFactor(0.8)
                .fixedSize(horizontal: false, vertical: true)
                .help(TaskModelTable.tooltip(row, column, l10n) ?? "")
        }
    }
}

/// The model cell: the overlapping source dots, the id (middle truncation; an unknown model `cost.noModelName`) and
/// the low-sample chip, which moves under the id when the column is too narrow for both. The dots take the width of
/// the table's widest stack, so the ids line up.
struct TaskModelNameCell: View {
    let row: TaskPerformanceModelRow
    let low: Bool
    let l10n: Localizer
    var font = TaskPerformanceFont.modelID
    var dotsWidth: CGFloat?

    var body: some View {
        TaskChipWrapLayout(indent: (dotsWidth ?? TaskModelDots.width(row.sources.count)) + 7) {
            HStack(spacing: 7) {
                dots
                name
            }
            if low { TaskLowSampleChip(stats: row.stats, l10n: l10n) }
        }
        .help(TaskModelTable.tooltip(row, .model, l10n) ?? "")
    }

    private var dots: some View {
        TaskModelDots(sources: row.sources)
            .frame(width: dotsWidth ?? TaskModelDots.width(row.sources.count), alignment: .leading)
    }

    @ViewBuilder
    private var name: some View {
        if row.model.isEmpty {
            Text(l10n("cost.noModelName"))
                .font(font)
                .italic()
                .foregroundStyle(.historySecondary)
                .lineLimit(1)
                .truncationMode(.tail)
        } else {
            Text(row.model)
                .font(font)
                .foregroundStyle(low ? AnyShapeStyle(.historySecondary) : AnyShapeStyle(.primary))
                .lineLimit(1)
                .truncationMode(.middle)
        }
    }
}

@MainActor
enum TaskModelCopy {
    static func copy(_ rows: [TaskPerformanceModelRow], _ l10n: Localizer) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(TaskModelTable.copyText(rows, l10n), forType: .string)
    }
}

/// Lays a header's or a row's cells out in the table's columns over the width it is given, so the header and every row
/// share their columns without measuring the section: numbers in fixed columns aligned trailing, the model in the rest.
struct TaskModelColumnsLayout: Layout {
    let columns: [TaskModelColumn]
    let width: TaskPerformanceWidth
    var alignment: VerticalAlignment = .center

    private func widths(_ available: CGFloat) -> (widths: [CGFloat], gap: CGFloat) {
        TaskModelColumnLayout.layout(columns, width: width, available: available)
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let available = proposal.width ?? 640
        let layout = widths(available)
        let height = zip(subviews, layout.widths).map { subview, width in
            subview.sizeThatFits(ProposedViewSize(width: width, height: nil)).height
        }.max() ?? 0
        return CGSize(width: available, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let layout = widths(bounds.width)
        var x = bounds.minX
        for (index, subview) in subviews.enumerated() {
            let width = layout.widths[safe: index] ?? 0
            let size = subview.sizeThatFits(ProposedViewSize(width: width, height: nil))
            let numeric = columns[safe: index]?.isNumeric ?? true
            let cellX = numeric ? x + width - min(size.width, width) : x
            let y: CGFloat = switch alignment {
            case .bottom: bounds.maxY - size.height
            case .top: bounds.minY
            default: bounds.midY - size.height / 2
            }
            subview.place(at: CGPoint(x: cellX, y: y), proposal: ProposedViewSize(width: min(size.width, width), height: size.height))
            x += width + layout.gap
        }
    }
}

/// The model's name with the low-sample chip after it, or under it (indented to the id) when both do not fit on one
/// line; the name truncates only once it is alone on its line. Decided from widths alone, so a row measures the same
/// whatever height it is offered.
struct TaskChipWrapLayout: Layout {
    var indent: CGFloat = 0
    var spacing: CGFloat = 6
    var lineSpacing: CGFloat = 2

    private struct Arrangement {
        var head: CGSize
        var chip: CGSize?
        var wrapped: Bool
    }

    private func arrange(_ width: CGFloat?, _ subviews: Subviews) -> Arrangement {
        let head = subviews[0].sizeThatFits(.unspecified)
        guard subviews.count > 1 else {
            let fitted = width.map { subviews[0].sizeThatFits(ProposedViewSize(width: min(head.width, $0), height: nil)) } ?? head
            return Arrangement(head: fitted, chip: nil, wrapped: false)
        }
        let chip = subviews[1].sizeThatFits(.unspecified)
        guard let width, head.width + spacing + chip.width > width else {
            return Arrangement(head: head, chip: chip, wrapped: false)
        }
        let fitted = subviews[0].sizeThatFits(ProposedViewSize(width: min(head.width, width), height: nil))
        return Arrangement(head: fitted, chip: chip, wrapped: true)
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard !subviews.isEmpty else { return .zero }
        let arrangement = arrange(proposal.width, subviews)
        guard let chip = arrangement.chip else { return arrangement.head }
        if arrangement.wrapped {
            return CGSize(width: max(arrangement.head.width, indent + chip.width),
                          height: arrangement.head.height + lineSpacing + chip.height)
        }
        return CGSize(width: arrangement.head.width + spacing + chip.width, height: max(arrangement.head.height, chip.height))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        guard !subviews.isEmpty else { return }
        let arrangement = arrange(bounds.width, subviews)
        let head = arrangement.head
        guard let chip = arrangement.chip else {
            subviews[0].place(at: CGPoint(x: bounds.minX, y: bounds.midY - head.height / 2), proposal: ProposedViewSize(head))
            return
        }
        if arrangement.wrapped {
            subviews[0].place(at: CGPoint(x: bounds.minX, y: bounds.minY), proposal: ProposedViewSize(head))
            subviews[1].place(at: CGPoint(x: bounds.minX + indent, y: bounds.minY + head.height + lineSpacing),
                              proposal: ProposedViewSize(chip))
        } else {
            let height = max(head.height, chip.height)
            subviews[0].place(at: CGPoint(x: bounds.minX, y: bounds.minY + (height - head.height) / 2), proposal: ProposedViewSize(head))
            subviews[1].place(at: CGPoint(x: bounds.minX + head.width + spacing, y: bounds.minY + (height - chip.height) / 2),
                              proposal: ProposedViewSize(chip))
        }
    }
}
