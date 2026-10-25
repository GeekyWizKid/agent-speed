import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The content builder of a board's style (DESIGN 2c Styles). Until the styles' own builders exist every style uses
/// `BoardMinimalContent`.
@MainActor
enum BoardContentBuilders {
    static func make(style: BoardStyle, size: BoardSize, dark: Bool, l10n: Localizer,
                     capsule: BoardCapsuleDrawing?) -> BoardContentBuilding {
        BoardMinimalContent(style: style, size: size, dark: dark)
    }
}

/// A plain stand-in that draws the value and one text line per row at the planned places.
@MainActor
struct BoardMinimalContent: BoardContentBuilding {
    let style: BoardStyle
    let size: BoardSize
    let dark: Bool

    func layout(_ tick: BoardTick) -> BoardContentLayout {
        let side = size.side
        let pad = size == .small ? 12.0 : size == .medium ? 14 : 16
        let metrics = BoardLayoutMetrics.of(style, size)
        let rowsTop = side - metrics.room - (style == .timing ? 38 : 46)
        let ink = dark ? NSColor(white: 1, alpha: 0.94) : NSColor(white: 0.07, alpha: 0.94)
        let ink2 = ink.withAlphaComponent(0.6)
        var items: [BoardContentItem] = []
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]

        let number = CGRect(x: pad + 4, y: pad + 8, width: (side - 2 * pad) * 0.55, height: 36)
        let hero = CGRect(x: pad, y: pad, width: side - 2 * pad, height: 70)
        let heroText = tick.valueText + "  " + tick.title
        items.append(BoardContentItem(key: "hero", frame: hero, motion: .none,
                                      revision: AnyHashable([heroText, dark ? "d" : "l"])) { _, _ in
            Self.text(heroText, at: CGPoint(x: 4, y: 8), size: 28, color: ink)
        })
        for row in tick.rows {
            let frame = CGRect(x: pad, y: rowsTop + row.y, width: side - 2 * pad, height: row.height)
            let line: String
            switch row.kind {
            case .group(let index):
                let group = tick.groups[index]
                anchors[group.id] = (frame, CGPoint(x: frame.minX + 13, y: frame.midY))
                line = "\(index + 1)  \(group.root.project)  \(SpeedFormat.speed(group.live))"
            case .sub(let index, let sub):
                let agent = tick.groups[index].subs[sub]
                line = "   └ \(agent.project)  \(SpeedFormat.speed(agent.live.value(for: tick.metric)))"
            case .moreSubs(_, let count, let live), .moreGroups(let count, let live):
                line = "   +\(count) · \(SpeedFormat.speed(live))"
            }
            items.append(BoardContentItem(key: row.key, frame: frame, revision: AnyHashable([line, dark ? "d" : "l"])) { _, box in
                Self.text(line, at: CGPoint(x: 6, y: box.height / 2 - 8), size: 12, color: ink2)
            })
        }
        let footer = [tick.todayText, tick.costText, tick.peakText].compactMap { $0 }.joined(separator: "  ·  ")
        items.append(BoardContentItem(key: "footer", frame: CGRect(x: pad, y: side - 30, width: side - 2 * pad, height: 20),
                                      motion: .none, revision: AnyHashable([footer, dark ? "d" : "l"])) { _, _ in
            Self.text(footer, at: CGPoint(x: 4, y: 3), size: 10.5, color: ink2)
        })
        return BoardContentLayout(items: items, groupRows: anchors, number: number)
    }

    private static func text(_ string: String, at point: CGPoint, size: CGFloat, color: NSColor) {
        let font = NSFont.monospacedDigitSystemFont(ofSize: size, weight: .medium)
        NSAttributedString(string: string, attributes: [.font: font, .foregroundColor: color]).draw(at: point)
    }
}
