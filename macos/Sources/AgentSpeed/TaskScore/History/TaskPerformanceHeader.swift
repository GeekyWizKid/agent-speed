import AgentSpeedCore
import AppKit
import SwiftUI

/// The title with its ⓘ popover and, trailing, the two source pickers or `history.tasks.onlyOne`. Narrow, the
/// trailing part takes a line of its own under the title, the pickers sharing it equally.
struct TaskPerformanceHeader: View {
    enum Accessory: Equatable {
        case none
        case onlyOne(SourceKind)
        case pickers(TaskPerformancePair, first: [SourceKind], second: [SourceKind])
    }

    let accessory: Accessory
    let width: TaskPerformanceWidth
    let l10n: Localizer
    let onPick: (TaskPerformanceSide, SourceKind) -> Void

    var body: some View {
        if width == .narrow, accessory != .none {
            VStack(alignment: .leading, spacing: Spacing.xs) {
                titleRow
                trailing(fill: true)
            }
        } else {
            HStack(alignment: .center, spacing: Spacing.xs) {
                titleRow
                Spacer(minLength: Spacing.s)
                trailing(fill: false)
            }
        }
    }

    private var titleRow: some View {
        HStack(alignment: .center, spacing: 6) {
            Text(l10n("history.tasks.title"))
                .font(TaskPerformanceFont.title)
                .lineLimit(1)
                .accessibilityAddTraits(.isHeader)
            TaskPerformanceInfoButton(l10n: l10n)
        }
    }

    @ViewBuilder
    private func trailing(fill: Bool) -> some View {
        switch accessory {
        case .none:
            EmptyView()
        case .onlyOne(let source):
            Text(TaskPerformanceText.onlyOne(source, l10n))
                .font(TaskPerformanceFont.onlyOne)
                .foregroundStyle(.historySecondary)
                .lineLimit(fill ? nil : 1)
                .fixedSize(horizontal: false, vertical: true)
        case .pickers(let pair, let first, let second):
            HStack(spacing: 6) {
                TaskSourcePicker(selection: pair.first, choices: first, label: l10n("history.tasks.pickA.a11y"), l10n: l10n) {
                    onPick(.first, $0)
                }
                .frame(maxWidth: fill ? .infinity : nil)
                Text(l10n("history.tasks.vs"))
                    .font(TaskPerformanceFont.vs)
                    .foregroundStyle(.tertiary)
                    .fixedSize()
                    .accessibilityHidden(true)
                TaskSourcePicker(selection: pair.second, choices: second, label: l10n("history.tasks.pickB.a11y"), l10n: l10n) {
                    onPick(.second, $0)
                }
                .frame(maxWidth: fill ? .infinity : nil)
            }
            .fixedSize(horizontal: !fill, vertical: false)
        }
    }
}

/// A small bordered pop-up of sources, each with its 7 pt dot.
struct TaskSourcePicker: View {
    let selection: SourceKind
    let choices: [SourceKind]
    let label: String
    let l10n: Localizer
    let onPick: (SourceKind) -> Void

    var body: some View {
        Picker(label, selection: Binding(get: { selection }, set: { if $0 != selection { onPick($0) } })) {
            ForEach(choices) { source in
                Label {
                    Text(l10n(source.titleKey))
                } icon: {
                    Image(nsImage: TaskSourceSwatch.image(source))
                }
                .tag(source)
            }
        }
        .pickerStyle(.menu)
        .labelsHidden()
        .controlSize(.small)
        .help(label)
        .accessibilityLabel(label)
    }
}

/// The 7 pt source dot as an image for menus and pop-up buttons, drawn for the appearance it lands in.
@MainActor
enum TaskSourceSwatch {
    private static var cache: [SourceKind: NSImage] = [:]

    static func image(_ source: SourceKind) -> NSImage {
        if let image = cache[source] { return image }
        let image = NSImage(size: NSSize(width: 7, height: 7), flipped: false) { rect in
            let dark = NSAppearance.currentDrawing().bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
            source.tint.darkened(dark ? 0 : 0.14).nsColor.setFill()
            NSBezierPath(ovalIn: rect).fill()
            return true
        }
        image.isTemplate = false
        cache[source] = image
        return image
    }
}

/// ⓘ: a 340 pt popover with `history.tasks.info` and every metric's full name with its hint.
struct TaskPerformanceInfoButton: View {
    let l10n: Localizer

    @State private var shown = false

    var body: some View {
        Button {
            shown.toggle()
        } label: {
            Image(systemName: "info.circle")
                .font(.system(size: 13))
                .foregroundStyle(.historySecondary)
                .contentShape(Rectangle())
        }
        .buttonStyle(.borderless)
        .help(l10n("history.tasks.info.a11y"))
        .accessibilityLabel(l10n("history.tasks.info.a11y"))
        .popover(isPresented: $shown, arrowEdge: .bottom) {
            TaskPerformanceInfo(l10n: l10n)
        }
    }
}

struct TaskPerformanceInfo: View {
    static let width: CGFloat = 340

    let l10n: Localizer

    var body: some View {
        VStack(alignment: .leading, spacing: Spacing.s) {
            Text(l10n("history.tasks.info"))
                .font(.system(size: 12))
                .foregroundStyle(.historySecondary)
                .lineSpacing(2.5)
                .fixedSize(horizontal: false, vertical: true)
            Divider()
            VStack(alignment: .leading, spacing: 9) {
                ForEach(TaskPerformanceDefinition.keys, id: \.self) { key in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(l10n(key))
                            .font(.system(size: 12, weight: .semibold))
                        Text(l10n("\(key).hint"))
                            .font(.system(size: 11.5))
                            .foregroundStyle(.historySecondary)
                            .lineSpacing(1.5)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
        }
        .padding(Spacing.m)
        .frame(width: Self.width, alignment: .leading)
        .environment(\.locale, l10n.locale)
    }
}
