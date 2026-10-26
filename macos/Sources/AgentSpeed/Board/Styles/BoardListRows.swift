import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// Glass and neon ranking rows (the prototypes' `.row` grid: 16 pt rank, 10 pt badge, name, capsule column, 46 pt
/// value, 8 pt gaps, 6 pt padding), with the share underline from the name to the value.
@MainActor
struct BoardListRows {
    let style: BoardStyle
    let size: BoardSize
    let palette: BoardPalette
    let capsules: BoardCapsuleDrawing

    private var neon: Bool { style == .neon }
    var compact: Bool { size == .small }

    /// The row's frame inside the board: the rows area spans the board less `pad − 6` on each side.
    func frame(_ row: BoardPlannedRow, rowsTop: CGFloat, pad: CGFloat) -> CGRect {
        let height = row.height - (isGroup(row) ? 2 : 0)
        return CGRect(x: pad - 6, y: rowsTop + CGFloat(row.y), width: CGFloat(size.side) - 2 * pad + 12, height: height)
    }

    /// The center of the rank, for the effects (board points).
    func rankCenter(_ frame: CGRect) -> CGPoint { CGPoint(x: frame.minX + 14, y: frame.midY) }

    /// The row's items: its body, its rank (groups) and its value, each cached apart so a new value or rank redraws
    /// only itself and can roll or flash; all three spring together.
    func items(_ model: BoardRowModel, key: String, frame: CGRect, column: CGFloat, accents: BoardRowAccents) -> [BoardContentItem] {
        let rows = self, look = AnyHashable([AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        var body = BoardContentItem(key: key, frame: frame, revision: AnyHashable([AnyHashable(model.body), AnyHashable(column), look])) { context, box in
            rows.draw(model, column: column, in: context, size: box)
        }
        guard model.kind != .more else { return [body] }
        let sub = model.kind == .sub
        let source = model.source.map { palette.source($0) } ?? .white
        if let event = accents.event {
            body.accents.append(.decal(BoardRowDecals.sweep(width: frame.width, height: frame.height, color: source,
                                                           leader: event.kind == .newLeader, radius: neon ? 9 : 10)))
        }
        var items = [body]
        if model.share > 0 {
            items.append(underline(model, sub: sub, key: key, frame: frame, look: look))
        }

        if let rank = model.rank {
            var item = BoardContentItem(key: key + "#rank", frame: CGRect(x: frame.minX + 6, y: frame.midY - 8, width: 16, height: 16),
                                        revision: AnyHashable([AnyHashable(rank), look])) { context, _ in
                rows.drawRank(rank, leader: rank == 1, midY: 8, x: 0, in: context)
            }
            item.clips = true
            if let old = accents.previousRank { item.accents.append(.roll(rising: rank < old)) }
            items.append(item)
        }

        let valueFrame = CGRect(x: frame.maxX - 76, y: frame.minY, width: 76, height: frame.height)
        var value = BoardContentItem(key: key + "#value", frame: valueFrame,
                                     revision: AnyHashable([AnyHashable(model.value), AnyHashable(model.idle), AnyHashable(model.heat), look])) { context, box in
            rows.drawValue(model, sub: sub, right: box.width - 6, midY: box.height / 2, hot: false, in: context)
        }
        value.hot = BoardContentDrawing { context, box in
            rows.drawValue(model, sub: sub, right: box.width - 6, midY: box.height / 2, hot: true, in: context)
        }
        switch accents.change {
        case .rise: value.accents.append(.warm)
        case .fall: value.accents.append(.dim)
        case .steady: break
        }
        if let event = accents.event, event.from > event.to {
            value.accents.append(.decal(BoardRowDecals.chip(style: style, climbed: event.from - event.to, rowRight: 76,
                                                            midY: frame.height / 2, palette: palette)))
        }
        items.append(value)
        return items
    }

    /// The share underline as an item of its own along the row's bottom, drawn at its full width and stretched to the
    /// share by its layer: a new share eases across without a redraw.
    private func underline(_ model: BoardRowModel, sub: Bool, key: String, frame: CGRect, look: AnyHashable) -> BoardContentItem {
        let rows = self, box = frame.size
        let reach = (neon ? 8 : 6) * 1.5 + 2
        let left: CGFloat = sub ? 46 : 40
        let rect = CGRect(x: left - reach, y: box.height - 3 - reach, width: box.width - left + reach, height: reach + 3)
        var item = BoardContentItem.part(key + "#line", of: frame, rect: rect, motion: .row,
                                         revision: AnyHashable([AnyHashable(model.heat), AnyHashable(sub), look])) { context in
            var full = model
            full.share = 200
            rows.drawUnderline(full, sub: sub, in: context, size: box)
        }
        item.stretch = model.shareFraction
        // The bar grows from its start, inside the part's frame (whole points, so the start sits a little in).
        item.stretchAnchor = frame.minX + left - item.frame.minX
        return item
    }

    // MARK: Drawing

    private func draw(_ model: BoardRowModel, column: CGFloat, in context: CGContext, size box: CGSize) {
        if model.kind == .more { return drawMore(model, in: context, size: box) }
        let sub = model.kind == .sub
        let midY = box.height / 2
        if model.isLeader { drawLeaderPlate(in: context, size: box) }

        if let source = model.source {
            BoardDrawing.badge(model.phase, color: palette.source(source), idle: palette.ink3, look: neon ? .glowDot : .dots,
                               glows: palette.glows, at: CGPoint(x: 30, y: midY - 5), in: context)
        }

        let valueWidth: CGFloat = 46
        let nameX: CGFloat = 48 + (sub ? 12 : 0)
        let capsuleX = box.width - 6 - valueWidth - 8 - column
        let nameWidth = max(0, capsuleX - 8 - nameX)
        if sub { BoardDrawing.connector(x: 50, midY: midY, color: palette.line, in: context) }

        let nameFont = sub ? BoardFont(size: 11.5, weight: .medium) : BoardFont(size: 12.5, weight: .semibold)
        let detailFont = neon ? BoardFont(family: .mono, size: 10, weight: .medium, tracking: 0.02) : BoardFont(size: 11)
        let name = BoardLabel(model.name, nameFont, color: sub ? palette.ink2 : palette.ink)
        let detail = BoardLabel(model.detail, detailFont, color: palette.ink3)
        let split = BoardContentRules.nameSplit(available: Double(nameWidth), name: Double(name.width))
        let baseline = name.baseline(centeredOn: midY)
        name.truncated(to: CGFloat(split.name)).draw(in: context, x: nameX, baseline: baseline)
        if !detail.isEmpty, split.detail > 4 {
            detail.truncated(to: CGFloat(split.detail)).draw(in: context, x: nameX + CGFloat(split.name) + 6, baseline: baseline)
        }

        if column > 0, let modes = model.modes {
            capsules.drawCapsule(for: modes, compact: compact, dark: palette.dark, highContrast: palette.highContrast, width: column,
                                 in: context, at: CGPoint(x: capsuleX, y: midY - EffortCapsule.height / 2))
        }
    }

    private func drawLeaderPlate(in context: CGContext, size box: CGSize) {
        let rect = CGRect(origin: .zero, size: box)
        if palette.highContrast {
            BoardDrawing.fill(rect, radius: neon ? 9 : 10, color: BoardPalette.gold.opacity(palette.dark ? 0.16 : 0.2), in: context)
            BoardDrawing.stroke(rect, radius: neon ? 9 : 10, width: 1, color: BoardPalette.gold, in: context)
            return
        }
        if neon {
            BoardDrawing.gradient(rect, radius: 9, stops: [(BoardPalette.gold.opacity(0.12), 0),
                                                            (RGBColor(hex: 0xE879F9, alpha: 0.07), 0.45),
                                                            (RGBColor(hex: 0xE879F9, alpha: 0), 0.75)],
                                  from: .zero, to: CGPoint(x: box.width, y: box.width * 0.18), in: context)
        } else {
            let gold = palette.dark ? BoardPalette.gold : RGBColor(hex: 0xE9A800)
            let alpha = palette.dark ? 0.11 : 0.12
            BoardDrawing.gradient(rect, radius: 10, stops: [(gold.opacity(alpha), 0), (gold.opacity(0.03), 0.55), (gold.opacity(0), 1)],
                                  from: .zero, to: CGPoint(x: box.width, y: 0), in: context)
        }
    }

    private func drawRank(_ rank: Int, leader: Bool, midY: CGFloat, x left: CGFloat, in context: CGContext) {
        let font = neon ? BoardFont(family: .mono, size: 12, weight: .bold) : BoardFont(size: 12, weight: .semibold, tabular: true)
        let label = BoardLabel("\(rank)", font, color: palette.ink3)
        let x = left + (16 - label.width) / 2
        let baseline = label.baseline(centeredOn: midY)
        guard leader else { return label.draw(in: context, x: x, baseline: baseline) }
        if palette.highContrast {
            return BoardLabel("\(rank)", font, color: palette.dark ? RGBColor(hex: 0xFFE58A) : RGBColor(hex: 0x8A5A00))
                .draw(in: context, x: x, baseline: baseline)
        }
        if neon {
            let color = palette.dark ? RGBColor(hex: 0xFFE58A) : RGBColor(hex: 0xB07800)
            BoardLabel("\(rank)", font, color: color).draw(in: context, x: x, baseline: baseline,
                                                         glow: palette.dark ? BoardPalette.gold.opacity(0.9) : nil, blur: 8)
        } else {
            let colors = palette.dark ? [RGBColor(hex: 0xFFE58A), RGBColor(hex: 0xE9A800)] : [RGBColor(hex: 0xE0A000), RGBColor(hex: 0x9A6500)]
            label.drawGradient(in: context, x: x, baseline: baseline, colors: colors, start: CGPoint(x: 0, y: midY - 8),
                               end: CGPoint(x: 0, y: midY + 8))
        }
    }

    private func drawUnderline(_ model: BoardRowModel, sub: Bool, in context: CGContext, size box: CGSize) {
        let left: CGFloat = sub ? 46 : 40
        let track = box.width - 6 - left
        let width = track * CGFloat(model.shareFraction)
        guard width > 0.5 else { return }
        let height: CGFloat = sub ? 1.5 : 2
        let rect = CGRect(x: left, y: box.height - 1 - height, width: width, height: height)
        let heat = palette.heat(max(0.12, model.heatFraction)).opacity(sub ? 0.7 : 1)
        if palette.highContrast { return BoardDrawing.fill(rect, radius: 1, color: heat.opacity(1), in: context) }
        let start = neon ? heat.opacity(0) : heat.opacity(heat.alpha * 0.35)
        BoardDrawing.glowing(heat.opacity(neon ? heat.alpha : heat.alpha * 0.7), blur: neon ? 8 : 6, in: context) {
            BoardDrawing.gradient(rect, radius: height, stops: [(start, 0), (heat, 1)], from: CGPoint(x: rect.minX, y: 0),
                                  to: CGPoint(x: rect.maxX, y: 0), in: $0)
        }
    }

    private func drawValue(_ model: BoardRowModel, sub: Bool, right: CGFloat, midY: CGFloat, hot: Bool, in context: CGContext) {
        let size: CGFloat = sub ? (neon ? 11 : 11.5) : 13
        let weight: NSFont.Weight = model.idle ? .medium : sub && !neon ? .semibold : neon ? .bold : .semibold
        let font = neon ? BoardFont(family: .mono, size: size, weight: weight, tracking: -0.02)
            : BoardFont(size: size, weight: weight, tabular: true)
        let color = model.idle ? palette.ink3
            : model.heatFraction > 0.45 ? palette.heatText(model.heatFraction)
            : sub ? palette.ink2 : palette.ink
        guard hot else {
            let label = BoardLabel(model.value, font, color: color)
            return label.draw(in: context, x: right - label.width, baseline: label.baseline(centeredOn: midY))
        }
        // Rising: white-hot with the row's heat around it (`valUp` / neon `vu`).
        let label = BoardLabel(model.value, font, color: palette.dark ? (neon ? .white : HeatPalette.whiteHot) : palette.ink)
        let glow = palette.glows ? palette.heat(max(0.12, model.heatFraction)) : nil
        label.draw(in: context, x: right - label.width, baseline: label.baseline(centeredOn: midY) - 2, glow: glow, blur: neon ? 12 : 10)
    }

    private func drawMore(_ model: BoardRowModel, in context: CGContext, size box: CGSize) {
        let midY = box.height / 2
        let colors = model.sources.map { palette.source($0) }
        let diameter: CGFloat = neon ? 5 : 6
        let ring = neon ? nil : Optional(BoardHostView.slabColor(style, dark: palette.dark).opacity(1))
        let origin = CGPoint(x: 60 - (neon ? 1 : 2), y: midY - diameter / 2 + (neon ? 0 : 0.5))
        let stack = BoardDrawing.stack(colors, diameter: diameter, overlap: neon ? 1 : 2, ring: ring, glows: palette.glows,
                                       at: origin, in: context)
        let font = neon ? BoardFont(family: .mono, size: 10, weight: .medium, tracking: 0.03) : BoardFont(size: 11)
        let label = BoardLabel(model.name, font, color: palette.ink3)
        let x = origin.x + stack + 6
        label.truncated(to: box.width - 6 - x).draw(in: context, x: x, baseline: label.baseline(centeredOn: midY))
    }

    private func isGroup(_ row: BoardPlannedRow) -> Bool {
        if case .group = row.kind { return true }
        return false
    }
}
