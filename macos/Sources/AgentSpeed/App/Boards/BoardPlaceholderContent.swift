import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// Simple stand-ins for the styles' content (phase 2 draws the real hero, rows and footer): a value and title, one
/// line per planned row with rank, source dot, name, value and share bar, and a footer line. Laid out with the
/// production `BoardLayoutPlanner`, so row places, moves and the effects' anchors are the real ones.
@MainActor
struct BoardPlaceholderContent {
    let style: BoardStyle
    let size: BoardSize
    let dark: Bool

    struct Layout {
        var items: [BoardContentItem]
        /// Group id → its row frame (board points) and rank center, for the effects.
        var groupRows: [String: (row: CGRect, rank: CGPoint)]
        var number: CGRect
    }

    private var side: Double { size.side }
    private var pad: Double { size == .small ? 12 : size == .medium ? 14 : 16 }
    private var metrics: BoardLayoutMetrics { .of(style, size) }
    private var rowsTop: Double { side - metrics.room - (style == .timing ? 38 : 46) }
    private var ink: NSColor { dark ? NSColor(white: 1, alpha: 0.94) : NSColor(white: 0.07, alpha: 0.94) }
    private var ink2: NSColor { dark ? NSColor(white: 1, alpha: 0.6) : NSColor(white: 0.1, alpha: 0.6) }

    func layout(_ snapshot: BoardSimulation.Snapshot, redline: Double = 2000) -> Layout {
        let groups = snapshot.groups
        let planned = BoardLayoutPlanner.plan(
            groups.map { BoardPlanGroup(id: $0.id, live: $0.live, subs: $0.subs.map { BoardPlanSub(id: $0.id, live: $0.live) }) },
            metrics: metrics)
        let heat = HeatPalette.heat(value: snapshot.live, redline: redline)
        let total = max(snapshot.live, 0.0001)
        var items: [BoardContentItem] = []
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]

        let hero = CGRect(x: pad, y: pad, width: side - 2 * pad, height: size == .small ? 70 : size == .medium ? 78 : 86)
        let bigSize: CGFloat = size == .small ? 30 : size == .medium ? 36 : 42
        let number = CGRect(x: hero.minX + 4, y: hero.minY + 8, width: hero.width * 0.55, height: bigSize)
        let value = SpeedFormat.speed(snapshot.live)
        let title = HeatPalette.color(heat, dark: dark).nsColor
        let ink = self.ink, ink2 = self.ink2
        let plate = ink.withAlphaComponent(0.06).cgColor
        let counts = "\(snapshot.generating) · \(snapshot.tools)"
        items.append(BoardContentItem(key: "hero", frame: hero, motion: .none,
                                      revision: AnyHashable([value, counts, String(Int(heat * 20)), dark ? "d" : "l"])) { _, box in
            Self.text(value, at: CGPoint(x: 4, y: 6), size: bigSize, weight: .bold, color: ink, mono: true)
            Self.text("tok/s", at: CGPoint(x: 6, y: 10 + bigSize), size: 11, weight: .semibold, color: title)
            Self.text(counts, at: CGPoint(x: box.width - 70, y: 12), size: 11, weight: .medium, color: ink2)
        })

        for row in planned {
            let frame = CGRect(x: pad, y: rowsTop + row.y, width: side - 2 * pad,
                               height: row.height - (Self.isGroup(row) && style != .timing ? 2 : 0))
            switch row.kind {
            case .group(let index):
                let group = groups[index]
                let color = Self.sourceColor(group.root.source)
                let share = group.live / total
                let label = SpeedFormat.speed(group.live)
                anchors[group.id] = (frame, CGPoint(x: frame.minX + 13, y: frame.midY))
                items.append(BoardContentItem(key: row.key, frame: frame, plate: .init(color: plate, cornerRadius: 9),
                                              revision: AnyHashable([String(index), group.root.project, label,
                                                                     String(Int(share * 50)), dark ? "d" : "l"])) { context, box in
                    Self.text("\(index + 1)", at: CGPoint(x: 8, y: box.height / 2 - 8), size: 13, weight: .bold, color: ink, mono: true)
                    context.setFillColor(color.cgColor)
                    context.fillEllipse(in: CGRect(x: 30, y: box.height / 2 - 4, width: 8, height: 8))
                    Self.text(group.root.project, at: CGPoint(x: 44, y: box.height / 2 - 8), size: 12.5, weight: .semibold, color: ink)
                    Self.text(label, at: CGPoint(x: box.width - 58, y: box.height / 2 - 8), size: 13, weight: .bold, color: ink,
                              mono: true)
                    context.setFillColor(color.withAlphaComponent(0.8).cgColor)
                    context.fill(CGRect(x: 40, y: box.height - 3, width: (box.width - 46) * min(1, share), height: 2))
                })
            case .sub(let index, let sub):
                let agent = groups[index].subs[sub]
                let label = SpeedFormat.speed(agent.live)
                items.append(BoardContentItem(key: row.key, frame: frame,
                                              revision: AnyHashable([agent.label, label, dark ? "d" : "l"])) { _, box in
                    Self.text("└ " + agent.label, at: CGPoint(x: 30, y: box.height / 2 - 7), size: 11, weight: .medium, color: ink2)
                    Self.text(label, at: CGPoint(x: box.width - 58, y: box.height / 2 - 7), size: 11, weight: .semibold,
                              color: ink2, mono: true)
                })
            case .moreSubs(_, let count, let live), .moreGroups(let count, let live):
                let text = "+\(count) · \(SpeedFormat.speed(live))"
                items.append(BoardContentItem(key: row.key, frame: frame, revision: AnyHashable([text, dark ? "d" : "l"])) { _, box in
                    Self.text(text, at: CGPoint(x: 30, y: box.height / 2 - 7), size: 10.5, weight: .medium, color: ink2)
                })
            }
        }

        let footer = CGRect(x: pad, y: side - 30, width: side - 2 * pad, height: 20)
        let footerText = "\(SpeedFormat.tokens(Int(snapshot.today)))  ·  US$\(String(format: "%.2f", snapshot.cost))  ·  "
            + SpeedFormat.speed(snapshot.peak)
        items.append(BoardContentItem(key: "footer", frame: footer, motion: .none,
                                      revision: AnyHashable([footerText, dark ? "d" : "l"])) { _, _ in
            Self.text(footerText, at: CGPoint(x: 4, y: 3), size: 10.5, weight: .medium, color: ink2)
        })
        return Layout(items: items, groupRows: anchors, number: number)
    }

    private static func isGroup(_ row: BoardPlannedRow) -> Bool {
        if case .group = row.kind { return true }
        return false
    }

    static func sourceColor(_ source: String) -> NSColor {
        RGBColor(hex: BoardSimulation.sources[source] ?? 0xFFFFFF).nsColor
    }

    private static var fonts: [String: NSFont] = [:]

    private static func text(_ string: String, at point: CGPoint, size: CGFloat, weight: NSFont.Weight, color: NSColor,
                             mono: Bool = false) {
        let key = "\(size)-\(weight.rawValue)-\(mono)"
        let font = fonts[key] ?? {
            let font = mono ? NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight) : NSFont.systemFont(ofSize: size, weight: weight)
            fonts[key] = font
            return font
        }()
        NSAttributedString(string: string, attributes: [.font: font, .foregroundColor: color]).draw(at: point)
    }
}
