import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// Timing tower rows (`board-timing.html` `.row` grid: 24 pt position block, 3 pt team stripe in the source color,
/// name, capsule column, 38 pt interval, 46 pt value, 7 pt gaps, 4 pt padding; S drops the interval).
@MainActor
struct BoardTimingRows {
    let size: BoardSize
    let palette: BoardPalette
    let capsules: BoardCapsuleDrawing

    var compact: Bool { size == .small }

    func frame(_ row: BoardPlannedRow, rowsTop: CGFloat, pad: CGFloat) -> CGRect {
        CGRect(x: pad - 4, y: rowsTop + CGFloat(row.y), width: CGFloat(size.side) - 2 * pad + 8, height: CGFloat(row.height))
    }

    func rankCenter(_ frame: CGRect) -> CGPoint { CGPoint(x: frame.minX + 16, y: frame.midY) }

    /// The row's body, position number and value as items of their own (see `BoardListRows.items`).
    func items(_ model: BoardRowModel, key: String, frame: CGRect, column: CGFloat, accents: BoardRowAccents) -> [BoardContentItem] {
        let rows = self, look = AnyHashable([AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        var body = BoardContentItem(key: key, frame: frame, revision: AnyHashable([AnyHashable(model.body), AnyHashable(column), look])) { context, box in
            rows.draw(model, column: column, in: context, size: box)
        }
        guard model.kind != .more else { return [body] }
        let sub = model.kind == .sub
        if let event = accents.event {
            let color = model.source.map { palette.source($0) } ?? .white
            body.accents.append(.decal(BoardRowDecals.sweep(width: frame.width, height: frame.height, color: color,
                                                           leader: event.kind == .newLeader, radius: 0)))
        }
        var items = [body]
        if intervalWidth > 0, !model.interval.isEmpty, !sub {
            items.append(interval(model, key: key, frame: frame, look: look))
        }

        if let rank = model.rank {
            let block = CGRect(x: frame.minX + 4, y: frame.midY - 11, width: 24, height: 22)
            var item = BoardContentItem(key: key + "#rank", frame: block, revision: AnyHashable([AnyHashable(rank), look])) { context, box in
                rows.drawPositionNumber(rank, leader: rank == 1, in: context, size: box)
            }
            item.clips = true
            if let old = accents.previousRank { item.accents.append(.roll(rising: rank < old)) }
            if accents.event != nil {
                item.accents.append(.decal(BoardDecal(frame: CGRect(origin: .zero, size: block.size), motion: .fade(seconds: 0.9)) { context, box in
                    BoardDrawing.fill(CGRect(origin: .zero, size: box), radius: 5, color: BoardPalette.green, in: context)
                    let label = BoardLabel("\(rank)", BoardFont(family: .mono, size: 12, weight: .heavy), color: RGBColor(hex: 0x04130A))
                    label.draw(in: context, x: (box.width - label.width) / 2, baseline: label.baseline(centeredOn: box.height / 2))
                }))
            }
            items.append(item)
        }

        let valueFrame = CGRect(x: frame.maxX - 70, y: frame.minY, width: 70, height: frame.height)
        var value = BoardContentItem(key: key + "#value", frame: valueFrame,
                                     revision: AnyHashable([AnyHashable(model.value), AnyHashable(model.idle), look])) { context, box in
            rows.drawValue(model, sub: sub, hot: false, in: context, size: box)
        }
        value.hot = BoardContentDrawing { context, box in rows.drawValue(model, sub: sub, hot: true, in: context, size: box) }
        switch accents.change {
        case .rise: value.accents.append(.warm)
        case .fall: value.accents.append(.dim)
        case .steady: break
        }
        if let event = accents.event, event.from > event.to {
            value.accents.append(.decal(BoardRowDecals.chip(style: .timing, climbed: event.from - event.to, rowRight: 70,
                                                            midY: frame.height / 2, palette: palette)))
        }
        items.append(value)
        return items
    }

    private var intervalWidth: CGFloat { compact ? 0 : 38 }
    private var valueWidth: CGFloat { compact ? 44 : 46 }

    private func draw(_ model: BoardRowModel, column: CGFloat, in context: CGContext, size box: CGSize) {
        let midY = box.height / 2
        let nameX: CGFloat = 4 + 24 + 7 + 3 + 7
        let mono = BoardFont.Family.mono
        if model.kind == .more {
            let label = BoardLabel(model.name, BoardFont(family: mono, size: 10, weight: .semibold, tracking: 0.03),
                                   color: palette.ink3)
            let x = nameX + 10
            label.truncated(to: box.width - 4 - x).draw(in: context, x: x, baseline: label.baseline(centeredOn: midY))
            return
        }
        let sub = model.kind == .sub
        if !sub { BoardDrawing.fill(CGRect(x: 0, y: box.height - 1, width: box.width, height: 1), color: palette.line, in: context) }
        if model.rank != nil { drawPositionBlock(leader: model.isLeader, midY: midY, in: context) }
        if let source = model.source { drawStripe(palette.source(source), sub: sub, midY: midY, in: context) }

        let valueX = box.width - 4 - valueWidth
        let intervalX = valueX - 7 - intervalWidth
        let capsuleX = intervalX - 7 - column
        let start = nameX + (sub ? 10 : 0)
        let nameWidth = max(0, capsuleX - 7 - start)
        if sub {
            BoardDrawing.fill(CGRect(x: nameX, y: (midY - 0.5).rounded(), width: 6, height: 1), color: palette.ink3.opacity(palette.ink3.alpha * 0.6),
                              in: context)
        }
        let name = BoardLabel(model.name, sub ? BoardFont(size: 11, weight: .semibold) : BoardFont(size: 12.5, weight: .bold, tracking: 0.005),
                              color: sub ? palette.ink2 : palette.ink)
        let detail = BoardLabel(model.detail, BoardFont(family: mono, size: 10, weight: .semibold, tracking: 0.02), color: palette.ink3)
        let split = BoardContentRules.nameSplit(available: Double(nameWidth), name: Double(name.width))
        let baseline = name.baseline(centeredOn: midY)
        name.truncated(to: CGFloat(split.name)).draw(in: context, x: start, baseline: baseline)
        if !detail.isEmpty, split.detail > 4 {
            detail.truncated(to: CGFloat(split.detail)).draw(in: context, x: start + CGFloat(split.name) + 6, baseline: baseline)
        }

        if column > 0, let modes = model.modes {
            capsules.drawCapsule(for: modes, compact: compact, dark: palette.dark, highContrast: palette.highContrast, width: column,
                                 in: context, at: CGPoint(x: capsuleX, y: midY - EffortCapsule.height / 2))
        }
    }

    /// The interval column as an item of its own (it changes on most ticks; the body stays cached).
    private func interval(_ model: BoardRowModel, key: String, frame: CGRect, look: AnyHashable) -> BoardContentItem {
        let box = frame.size, intervalWidth = self.intervalWidth, palette = self.palette
        let intervalX = box.width - 4 - valueWidth - 7 - intervalWidth
        let rect = CGRect(x: intervalX - 24, y: 0, width: intervalWidth + 28, height: box.height)
        return .part(key + "#gap", of: frame, rect: rect, motion: .row,
                     revision: AnyHashable([AnyHashable(model.interval), AnyHashable(model.isLeader), look])) { context in
            let leader = model.isLeader
            let font = leader ? BoardFont(family: .mono, size: 9.5, weight: .heavy, tracking: 0.08)
                : BoardFont(family: .mono, size: 10.5, weight: .semibold)
            let gold = palette.dark ? BoardPalette.gold : RGBColor(hex: 0x9A6B00)
            let label = BoardLabel(model.interval, font, color: leader ? gold : palette.ink3)
            label.draw(in: context, x: intervalX + intervalWidth - label.width, baseline: label.baseline(centeredOn: box.height / 2))
        }
    }

    /// The value; rising, it flashes green (`tvu`) with a glow.
    private func drawValue(_ model: BoardRowModel, sub: Bool, hot: Bool, in context: CGContext, size box: CGSize) {
        let font = BoardFont(family: .mono, size: sub ? 11 : 13, weight: model.idle ? .semibold : sub ? .bold : .heavy, tracking: -0.02)
        let color = hot ? BoardPalette.green : model.idle ? palette.ink3 : sub ? palette.ink2 : palette.ink
        let value = BoardLabel(model.value, font, color: color)
        let baseline = value.baseline(centeredOn: box.height / 2) - (hot ? 2 : 0)
        value.draw(in: context, x: box.width - 4 - value.width, baseline: baseline,
                   glow: hot && palette.glows ? BoardPalette.green.opacity(0.7) : nil, blur: 8)
    }

    private func drawPositionBlock(leader: Bool, midY: CGFloat, in context: CGContext) {
        let block = CGRect(x: 4, y: midY - 11, width: 24, height: 22)
        guard leader else {
            BoardDrawing.fill(block, radius: 5, color: palette.block, in: context)
            if palette.highContrast { BoardDrawing.stroke(block, radius: 5, width: 1, color: palette.ink3, in: context) }
            return
        }
        if palette.highContrast { return BoardDrawing.fill(block, radius: 5, color: BoardPalette.gold, in: context) }
        BoardDrawing.glowing(BoardPalette.gold.opacity(0.35), blur: 12, in: context) {
            BoardDrawing.fill(block, radius: 5, color: BoardPalette.gold, in: $0)
        }
        BoardDrawing.gradient(block, radius: 5, stops: [(RGBColor(hex: 0xFFF1A8), 0), (BoardPalette.gold, 0.45), (RGBColor(hex: 0xD49B00), 1)],
                              from: CGPoint(x: block.minX + 8, y: block.minY), to: CGPoint(x: block.maxX - 8, y: block.maxY), in: context)
    }

    private func drawPositionNumber(_ rank: Int, leader: Bool, in context: CGContext, size box: CGSize) {
        let label = BoardLabel("\(rank)", BoardFont(family: .mono, size: 12, weight: .heavy),
                               color: leader ? RGBColor(hex: 0x241A00) : palette.blockInk)
        label.draw(in: context, x: (box.width - label.width) / 2, baseline: label.baseline(centeredOn: box.height / 2))
    }

    private func drawStripe(_ color: RGBColor, sub: Bool, midY: CGFloat, in context: CGContext) {
        let x: CGFloat = 4 + 24 + 7
        let stripe = sub ? CGRect(x: x + 1, y: midY - 5, width: 2, height: 10) : CGRect(x: x, y: midY - 9, width: 3, height: 18)
        let tint = color.opacity(sub ? 0.7 : 1)
        BoardDrawing.glowing(palette.glows && !sub ? color.opacity(0.6) : nil, blur: 6, in: context) {
            BoardDrawing.fill(stripe, radius: 2, color: tint, in: $0)
        }
    }
}
