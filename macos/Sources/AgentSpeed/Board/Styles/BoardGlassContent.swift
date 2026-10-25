import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// 流光玻璃 (DESIGN 2c Styles; `board-glass.html`): calm type on the glass, a heat-tinted aurora drifting behind the
/// hero, the 60 s sparkline, rows with glowing share underlines.
@MainActor
final class BoardGlassContent: BoardContentBuilding {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let rows: BoardListRows
    private var memory = BoardTickMemory()

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
        memory.begin(tick)
        let models = BoardRowModels.make(tick, l10n: l10n)
        let lives = BoardRowModels.lives(tick)
        let column = BoardRowModels.capsuleColumn(models, compact: rows.compact, capsules: rows.capsules)
        var items = aurora(tick)
        let (hero, number) = self.hero(tick)
        items += hero
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]
        for (index, row) in tick.rows.enumerated() {
            let frame = rows.frame(row, rowsTop: rowsTop, pad: pad)
            if case .group(let group) = row.kind { anchors[tick.groups[group].id] = (frame, rows.rankCenter(frame)) }
            items += rows.items(models[index], key: row.key, frame: frame, column: column,
                                accents: BoardRowAccents(row, live: lives[index], tick: tick, memory: memory))
        }
        items.append(footer(tick))
        memory.end(tick, rows: zip(tick.rows, lives).map { ($0.key, $1) })
        return BoardContentLayout(items: items, groupRows: anchors, number: number)
    }

    /// Two soft lobes of heat light behind the hero (`.aurora`, drifting on 19 s and 23 s while agents work) and the
    /// rule above the rows.
    private func aurora(_ tick: BoardTick) -> [BoardContentItem] {
        let palette = self.palette, side = self.side
        var items: [BoardContentItem] = []
        if !palette.highContrast {
            let heat = (tick.heat * 25).rounded() / 25
            let strength = 0.18 + heat * 0.62
            let active = tick.generating > 0
            // The lobes' closest-side gradients under a 34 pt blur: a linear fade reaching a blur further.
            let radius = CGSize(width: side * 0.49 + 34, height: side * 0.495 + 34)
            let lobes: [(CGPoint, RGBColor, BoardContentAmbient)] = [
                (CGPoint(x: side * 0.29, y: side * 0.205), HeatPalette.color(max(0.06, heat)).opacity(strength * 0.9),
                 .drift(dx: side * 0.176, dy: side * 0.079, scale: 1.15, period: 19)),
                (CGPoint(x: side * 0.78, y: side * 0.095), HeatPalette.color(min(1, heat + 0.18)).opacity(strength * 0.63),
                 .drift(dx: -side * 0.137, dy: side * 0.119, scale: 0.92, period: 23)),
            ]
            for (index, (center, color, drift)) in lobes.enumerated() {
                let frame = CGRect(x: center.x - radius.width, y: center.y - radius.height, width: 2 * radius.width, height: 2 * radius.height)
                var lobe = BoardContentItem(key: "aurora\(index)", frame: frame, motion: .none,
                                            revision: AnyHashable([heat, palette.dark ? 1 : 0])) { context, box in
                    BoardDrawing.radial(center: CGPoint(x: box.width / 2, y: box.height / 2), radius: radius, color: color, in: context)
                }
                lobe.ambient = active ? drift : nil
                lobe.rasterScale = 0.5
                items.append(lobe)
            }
        }
        let rule = CGRect(x: pad, y: rowsTop, width: side - 2 * pad, height: 1)
        items.append(BoardContentItem(key: "rule", frame: rule, motion: .none, revision: AnyHashable([palette.dark, palette.highContrast])) { context, box in
            BoardDrawing.fill(CGRect(origin: .zero, size: box), color: palette.line, in: context)
        })
        return items
    }

    private func hero(_ tick: BoardTick) -> ([BoardContentItem], CGRect) {
        let palette = self.palette, big = self.big, heroHeight = self.heroHeight
        let heat = (tick.heat * 50).rounded() / 50
        let metaLine: CGFloat = 17
        let numTop = (heroHeight - (big * 1.08 + 7 + metaLine)) / 2
        let frame = CGRect(x: pad, y: pad - 2, width: side - 2 * pad, height: heroHeight)
        let look = BoardHeroNumber.Look(font: BoardFont(size: big, weight: .bold, tracking: -0.02, tabular: true), color: palette.ink,
                                        hot: palette.dark ? HeatPalette.whiteHot : palette.ink,
                                        hotGlow: palette.glows ? palette.heat(max(0.12, heat)) : nil, hotBlur: 14)
        let number = BoardHeroNumber(tick.valueText, value: tick.value, look: look, origin: CGPoint(x: frame.minX, y: frame.minY + numTop))
        let unit = BoardLabel(l10n("unit.tokS"), BoardFont(size: 13, weight: .medium), color: palette.ink2)
        let title = BoardLabel(tick.title, BoardFont(size: 12, weight: .semibold, tracking: 0.03), color: palette.heatText(max(0.06, tick.heat)))
        let counts = BoardLabel("· " + l10n("agents.context", tick.generating, tick.tools), BoardFont(size: 12), color: palette.ink2)
        let sparkWidth: CGFloat = size == .small ? 76 : 104
        let history = tick.history.map { ($0 * 10).rounded() / 10 }
        let revision = AnyHashable([AnyHashable(tick.valueText), AnyHashable(tick.title), AnyHashable(tick.generating),
                                    AnyHashable(tick.tools), AnyHashable(history), AnyHashable(heat), AnyHashable(palette.dark),
                                    AnyHashable(palette.highContrast), AnyHashable(l10n.language.rawValue)])
        let hero = BoardContentItem(key: "hero", frame: frame, motion: .none, revision: revision) { context, box in
            let baseline = numTop + number.baseline
            if palette.glows, heat > 0 {
                let glow = palette.heat(heat).opacity(0.55)
                number.drawGlow(in: context, x: 0, top: numTop, color: glow, blurs: [heat * 14])
                unit.drawGlowOnly(in: context, x: number.label.width + 6, baseline: baseline, glow: glow, blur: heat * 14)
            }
            unit.draw(in: context, x: number.label.width + 6, baseline: baseline)
            let metaMid = numTop + big * 1.08 + 7 + metaLine / 2
            let metaBaseline = title.baseline(centeredOn: metaMid)
            let room = box.width - sparkWidth - 8
            let shownTitle = title.truncated(to: room)
            shownTitle.draw(in: context, x: 0, baseline: metaBaseline)
            counts.truncated(to: max(0, room - shownTitle.width - 6)).draw(in: context, x: shownTitle.width + 6, baseline: metaBaseline)
            // The page's inline SVG sits atop a line box 3 pt taller than itself.
            let spark = CGRect(x: box.width - sparkWidth, y: (box.height - 43) / 2, width: sparkWidth, height: 40)
            BoardHeroHistory.sparkline(history, in: spark, color: palette.heat(max(0.06, heat)), context: context)
        }
        let decals = tick.newPeak ? [number.sheen(), BoardHeroNumber.peakChip(style: .glass, l10n: l10n, glows: palette.glows)] : []
        return ([hero] + number.items(memory: memory, decals: decals), number.rect)
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
