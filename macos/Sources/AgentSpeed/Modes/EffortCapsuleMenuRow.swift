import AppKit
import SwiftUI

/// The disabled menu row under a header carrying the shared effort capsule, or `mode.mixed` in secondary text.
struct EffortCapsuleMenuRow: View {
    let shared: SharedEffortCapsule
    let l10n: Localizer
    var reduceMotion = false
    var shimmers = true

    static let height: CGFloat = 22
    /// Lines the content up with the item titles after the menu's check mark column.
    static let leadingInset: CGFloat = 21

    var body: some View {
        HStack(spacing: 0) {
            switch shared {
            case .capsule(let capsule):
                EffortCapsuleView(capsule: capsule, reduceMotion: reduceMotion, shimmers: shimmers)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(capsule.accessibilityText(l10n))
            case .mixed:
                Text(l10n("mode.mixed"))
                    .font(Font(NSFont.menuFont(ofSize: 0)))
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(.leading, Self.leadingInset)
        .padding(.trailing, 14)
        .frame(height: Self.height)
    }

    /// A disabled menu item hosting the row; its tooltip carries the capsule's lines.
    @MainActor
    static func menuItem(_ shared: SharedEffortCapsule, l10n: Localizer, reduceMotion: Bool, shimmers: Bool) -> NSMenuItem {
        let item = NSMenuItem(title: "", action: nil, keyEquivalent: "")
        item.isEnabled = false
        let host = NSHostingView(rootView: EffortCapsuleMenuRow(shared: shared, l10n: l10n, reduceMotion: reduceMotion, shimmers: shimmers))
        host.frame = NSRect(x: 0, y: 0, width: host.fittingSize.width, height: height)
        host.autoresizingMask = [.width]
        item.view = host
        switch shared {
        case .capsule(let capsule): item.toolTip = capsule.tooltipLines(l10n).joined(separator: "\n")
        case .mixed: item.title = l10n("mode.mixed")
        }
        return item
    }
}
