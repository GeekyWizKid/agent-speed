import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// 流光玻璃 (DESIGN 2c Styles; `board-glass.html`): calm type on the glass, a heat-tinted aurora behind the hero, the
/// 60 s sparkline, rows with glowing share underlines.
@MainActor
final class BoardGlassContent: BoardContentBuilding {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let rows: BoardListRows

    init(size: BoardSize, dark: Bool, highContrast: Bool, l10n: Localizer, capsules: BoardCapsuleDrawing) {
        self.size = size
        palette = BoardPalette(style: .glass, dark: dark, highContrast: highContrast)
        self.l10n = l10n
        rows = BoardListRows(style: .glass, size: size, palette: palette, capsules: capsules)
    }

    private var side: CGFloat { CGFloat(size.side) }
    private var pad: CGFloat { size == .small ? 14 : size == .medium ? 16 : 18 }
    private var heroHeight: CGFloat { size == .small ? 78 : size == .medium ? 86 : 94 }
    private var big: CGFloat { size == .small ? 34 : size == .medium ? 40 : 46 }
    private var rowsTop: CGFloat { pad + heroHeight + 4 }

    func layout(_ tick: BoardTick) -> BoardContentLayout {
        let models = BoardRowModels.make(tick, l10n: l10n)
        let column = BoardRowModels.capsuleColumn(models, compact: rows.compact, capsules: rows.capsules)
        var items = [aurora(tick)]
        let (hero, number) = self.hero(tick)
        items.append(hero)
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]
        for (row, model) in zip(tick.rows, models) {
            let frame = rows.frame(row, rowsTop: rowsTop, pad: pad)
            if case .group(let index) = row.kind { anchors[tick.groups[index].id] = (frame, rows.rankCenter(frame)) }
            items.append(rows.item(model, key: row.key, frame: frame, column: column))
        }
        items.append(footer(tick))
        return BoardContentLayout(items: items, groupRows: anchors, number: number)
    }

    /// Two soft lobes of heat light behind the hero (`.aurora`) and the rule above the rows.
    private func aurora(_ tick: BoardTick) -> BoardContentItem {
        let heat = (tick.heat * 50).rounded() / 50
        let palette = self.palette, side = self.side, pad = self.pad, rule = rowsTop
        let revision = AnyHashable([AnyHashable("aurora"), AnyHashable(heat), AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        return BoardContentItem(key: "ambience", frame: CGRect(x: 0, y: 0, width: side, height: side), motion: .none,
                                revision: revision) { context, _ in
            if !palette.highContrast {
                let strength = 0.18 + heat * 0.62
                let lobe = CGSize(width: side * 0.49, height: side * 0.495)
                BoardDrawing.radial(center: CGPoint(x: side * 0.29, y: side * 0.205), radius: lobe,
                                    color: palette.heat(max(0.06, heat)).opacity(strength * 0.9), in: context)
                BoardDrawing.radial(center: CGPoint(x: side * 0.78, y: side * 0.095), radius: lobe,
                                    color: palette.heat(min(1, heat + 0.18)).opacity(strength * 0.7 * 0.9), in: context)
            }
            BoardDrawing.fill(CGRect(x: pad, y: rule, width: side - 2 * pad, height: 1), color: palette.line, in: context)
        }
    }

    private func hero(_ tick: BoardTick) -> (BoardContentItem, CGRect) {
        let palette = self.palette, big = self.big, heroHeight = self.heroHeight
        let numFont = BoardFont(size: big, weight: .bold, tracking: -0.02, tabular: true)
        let number = BoardLabel(tick.valueText, numFont, color: palette.ink)
        let unit = BoardLabel(l10n("unit.tokS"), BoardFont(size: 13, weight: .medium), color: palette.ink2)
        let heatColor = palette.heatText(max(0.06, tick.heat))
        let title = BoardLabel(tick.title, BoardFont(size: 12, weight: .semibold, tracking: 0.03), color: heatColor)
        let counts = BoardLabel("· " + l10n("agents.context", tick.generating, tick.tools), BoardFont(size: 12), color: palette.ink2)
        let metaLine: CGFloat = 14.3
        let numTop = ((heroHeight - (big * 1.08 + 7 + metaLine)) / 2).rounded()
        let sparkWidth: CGFloat = size == .small ? 76 : 104
        let frame = CGRect(x: pad, y: pad - 2, width: side - 2 * pad, height: heroHeight)
        let numberRect = CGRect(x: frame.minX, y: frame.minY + numTop, width: number.width, height: big * 1.08)
        let heat = (tick.heat * 50).rounded() / 50
        let history = tick.history.map { ($0 * 10).rounded() / 10 }
        let revision = AnyHashable([AnyHashable(tick.valueText), AnyHashable(tick.title), AnyHashable(counts.width),
                                    AnyHashable(tick.generating), AnyHashable(tick.tools), AnyHashable(history), AnyHashable(heat),
                                    AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        let item = BoardContentItem(key: "hero", frame: frame, motion: .none, revision: revision) { context, box in
            let baseline = number.baseline(lineBoxTop: numTop)
            let glow = palette.glows && heat > 0 ? palette.heat(heat).opacity(0.55) : nil
            BoardDrawing.glowing(glow, blur: heat * 14, in: context) {
                number.draw(in: context, x: 0, baseline: baseline)
                unit.draw(in: context, x: number.width + 6, baseline: baseline)
            }
            let metaMid = numTop + big * 1.08 + 7 + metaLine / 2
            let metaBaseline = title.baseline(centeredOn: metaMid)
            let room = box.width - sparkWidth - 8
            let shownTitle = title.truncated(to: room)
            shownTitle.draw(in: context, x: 0, baseline: metaBaseline)
            counts.truncated(to: max(0, room - shownTitle.width - 6)).draw(in: context, x: shownTitle.width + 6, baseline: metaBaseline)
            let spark = CGRect(x: box.width - sparkWidth, y: (box.height - 40) / 2, width: sparkWidth, height: 40)
            BoardHeroHistory.sparkline(history, in: spark, color: palette.heat(max(0.06, heat)), context: context)
        }
        return (item, numberRect)
    }

    private func footer(_ tick: BoardTick) -> BoardContentItem {
        var parts = [BoardFooterPart("boards.today", value: tick.todayText, l10n: l10n)]
        if let cost = tick.costText { parts.append(BoardFooterPart(value: cost)) }
        if size != .small { parts.append(BoardFooterPart("boards.peak", value: tick.peakText, l10n: l10n)) }
        let palette = self.palette
        let frame = CGRect(x: pad, y: side - 12 - 18, width: side - 2 * pad, height: 18)
        let revision = AnyHashable([tick.todayText, tick.costText ?? "", tick.peakText, palette.dark ? "d" : "l",
                                    palette.highContrast ? "c" : "", l10n.language.rawValue])
        return BoardContentItem(key: "footer", frame: frame, motion: .none, revision: revision) { context, box in
            BoardFooterPart.draw(parts, label: BoardFont(size: 11, tabular: true), strong: BoardFont(size: 11, weight: .semibold, tabular: true),
                                 labelColor: palette.ink3, strongColor: palette.ink2, width: box.width, midY: box.height / 2, in: context)
        }
    }
}
