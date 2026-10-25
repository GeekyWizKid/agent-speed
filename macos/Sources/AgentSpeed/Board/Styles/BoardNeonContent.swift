import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// 霓虹座艙 (DESIGN 2c Styles; `board-neon.html`): a night cockpit in the LED orb's language: a grid deck lit by the
/// heat, glowing LED digits (chromatic fringes from h = 0.85), the 24-segment LED bar and a breathing neon tube.
@MainActor
final class BoardNeonContent: BoardContentBuilding {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let rows: BoardListRows

    init(size: BoardSize, dark: Bool, highContrast: Bool, l10n: Localizer, capsules: BoardCapsuleDrawing) {
        self.size = size
        palette = BoardPalette(style: .neon, dark: dark, highContrast: highContrast)
        self.l10n = l10n
        rows = BoardListRows(style: .neon, size: size, palette: palette, capsules: capsules)
    }

    private var side: CGFloat { CGFloat(size.side) }
    private var pad: CGFloat { size == .small ? 14 : size == .medium ? 16 : 18 }
    private var heroHeight: CGFloat { size == .small ? 82 : size == .medium ? 92 : 100 }
    private var big: CGFloat { size == .small ? 34 : size == .medium ? 40 : 46 }
    private var rowsTop: CGFloat { pad + heroHeight + 2 }

    func layout(_ tick: BoardTick) -> BoardContentLayout {
        let models = BoardRowModels.make(tick, l10n: l10n)
        let column = BoardRowModels.capsuleColumn(models, compact: rows.compact, capsules: rows.capsules)
        var items = [deck(tick)]
        let (hero, number) = self.hero(tick)
        items.append(hero)
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]
        for (row, model) in zip(tick.rows, models) {
            let frame = rows.frame(row, rowsTop: rowsTop, pad: pad)
            if case .group(let index) = row.kind { anchors[tick.groups[index].id] = (frame, rows.rankCenter(frame)) }
            items.append(rows.item(model, key: row.key, frame: frame, column: column))
        }
        items.append(footer(tick))
        items.append(tube(tick))
        return BoardContentLayout(items: items, groupRows: anchors, number: number)
    }

    /// The deck: a 22 pt grid and the heat's light from the top right.
    private func deck(_ tick: BoardTick) -> BoardContentItem {
        let heat = (tick.heat * 50).rounded() / 50
        let palette = self.palette, side = self.side
        let revision = AnyHashable([AnyHashable("deck"), AnyHashable(heat), AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        return BoardContentItem(key: "ambience", frame: CGRect(x: 0, y: 0, width: side, height: side), motion: .none,
                                revision: revision) { context, _ in
            if !palette.highContrast, heat > 0 {
                BoardDrawing.radial(center: CGPoint(x: side * 0.85, y: 0), radius: CGSize(width: side * 1.2 * 0.6, height: side * 0.8 * 0.6),
                                    color: palette.heat(heat).opacity(heat * 0.22 * 1.4), in: context)
            }
            let grid = palette.dark ? RGBColor(red: 120 / 255, green: 150 / 255, blue: 1, alpha: 0.05)
                : RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: 0.04)
            context.setFillColor(grid.cgColor)
            var offset: CGFloat = 0
            while offset < side {
                context.fill(CGRect(x: offset, y: 0, width: 1, height: side))
                context.fill(CGRect(x: 0, y: side - 1 - offset, width: side, height: 1))
                offset += 22
            }
        }
    }

    private func hero(_ tick: BoardTick) -> (BoardContentItem, CGRect) {
        let palette = self.palette, big = self.big, heroHeight = self.heroHeight
        let heat = (tick.heat * 50).rounded() / 50
        let heatColor = palette.heatText(max(0.06, heat))
        let numInk = palette.dark ? RGBColor(hex: 0xFFFDF5) : palette.ink
        let number = BoardLabel(tick.valueText, BoardFont(family: .mono, size: big, weight: .bold, tracking: -0.03), color: numInk)
        let unit = BoardLabel(l10n("unit.tokS"), BoardFont(family: .mono, size: 10.5, weight: .bold, tracking: 0.14), color: heatColor)
        let title = BoardLabel(tick.title, BoardFont(size: 12, weight: .bold, tracking: 0.06), color: heatColor)
        let counts = BoardLabel("· " + l10n("agents.context", tick.generating, tick.tools),
                                BoardFont(family: .mono, size: 10.5, weight: .semibold, tracking: 0.06), color: palette.ink3)
        let peakHeat = HeatPalette.heat(value: tick.peak, redline: tick.redline)
        let frame = CGRect(x: pad, y: pad - 2, width: side - 2 * pad, height: heroHeight)
        let numberRect = CGRect(x: frame.minX, y: frame.minY, width: number.width, height: big * 1.08)
        let revision = AnyHashable([AnyHashable(tick.valueText), AnyHashable(tick.title), AnyHashable(tick.generating),
                                    AnyHashable(tick.tools), AnyHashable(heat), AnyHashable((peakHeat * 48).rounded()),
                                    AnyHashable(palette.dark), AnyHashable(palette.highContrast), AnyHashable(l10n.language.rawValue)])
        let item = BoardContentItem(key: "hero", frame: frame, motion: .none, revision: revision) { context, box in
            let baseline = number.baseline(lineBoxTop: 0)
            let glow = palette.heat(max(0.06, heat))
            if palette.glows {
                if palette.dark {
                    if heat > 0.85 {
                        BoardLabel(tick.valueText, BoardFont(family: .mono, size: big, weight: .bold, tracking: -0.03),
                                   color: RGBColor(red: 1, green: 40 / 255, blue: 110 / 255, alpha: 0.75))
                            .draw(in: context, x: -1.5, baseline: baseline)
                        BoardLabel(tick.valueText, BoardFont(family: .mono, size: big, weight: .bold, tracking: -0.03),
                                   color: RGBColor(red: 0, green: 225 / 255, blue: 1, alpha: 0.75))
                            .draw(in: context, x: 1.5, baseline: baseline)
                    }
                    number.draw(in: context, x: 0, baseline: baseline, glow: glow, blur: 4 + heat * 16)
                    number.draw(in: context, x: 0, baseline: baseline, glow: glow, blur: 10)
                    number.draw(in: context, x: 0, baseline: baseline, glow: glow, blur: 3)
                } else {
                    number.draw(in: context, x: 0, baseline: baseline, glow: glow.opacity(0.55), blur: 8)
                }
            } else {
                number.draw(in: context, x: 0, baseline: baseline)
            }
            unit.draw(in: context, x: number.width + 7, baseline: baseline, glow: palette.glows ? glow.opacity(0.55) : nil, blur: 8)

            let metaMid = big * 1.08 + 8 + 6
            let metaBaseline = title.baseline(centeredOn: metaMid)
            let shownTitle = title.truncated(to: box.width)
            shownTitle.draw(in: context, x: 0, baseline: metaBaseline, glow: palette.glows ? glow.opacity(0.55) : nil, blur: 10)
            counts.truncated(to: max(0, box.width - shownTitle.width - 8))
                .draw(in: context, x: shownTitle.width + 8, baseline: counts.baseline(centeredOn: metaMid))

            BoardHeroHistory.led(heat: heat, peakHeat: peakHeat, in: CGRect(x: 0, y: box.height - 6 - 7, width: box.width, height: 7),
                                 palette: palette, context: context)
        }
        return (item, numberRect)
    }

    private func footer(_ tick: BoardTick) -> BoardContentItem {
        var parts = [BoardFooterPart("boards.today", value: tick.todayText, l10n: l10n)]
        if let cost = tick.costText { parts.append(BoardFooterPart(value: cost)) }
        if size != .small { parts.append(BoardFooterPart("boards.peak", value: tick.peakText, l10n: l10n)) }
        let palette = self.palette
        let frame = CGRect(x: pad, y: side - 11 - 18, width: side - 2 * pad, height: 18)
        let revision = AnyHashable([tick.todayText, tick.costText ?? "", tick.peakText, palette.dark ? "d" : "l",
                                    palette.highContrast ? "c" : "", l10n.language.rawValue])
        return BoardContentItem(key: "footer", frame: frame, motion: .none, revision: revision) { context, box in
            BoardFooterPart.draw(parts, label: BoardFont(family: .mono, size: 10, weight: .semibold, tracking: 0.06),
                                 strong: BoardFont(family: .mono, size: 10, weight: .bold, tracking: 0.06),
                                 labelColor: palette.ink3, strongColor: palette.ink2, width: box.width, midY: box.height / 2, in: context)
        }
    }

    /// The neon tube on the edge (`.panel::after`): always lit, brighter with heat. It breathes in the effects' clock
    /// only through the ring; the cached tube holds the mean of its breath.
    private func tube(_ tick: BoardTick) -> BoardContentItem {
        let heat = (tick.heat * 50).rounded() / 50
        let palette = self.palette, side = self.side
        let corner = CGFloat(BoardStyle.neon.cornerRadius)
        let revision = AnyHashable([AnyHashable("tube"), AnyHashable(heat), AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        return BoardContentItem(key: "tube", frame: CGRect(x: 0, y: 0, width: side, height: side), motion: .none,
                                revision: revision) { context, _ in
            let rect = CGRect(x: 0, y: 0, width: side, height: side)
            let base = palette.heat(max(0.06, heat))
            let opacity = palette.highContrast ? 1 : 0.375 + heat * 0.525
            let edge = base.mixOKLab(.white, 0.12).opacity(0.85 * opacity)
            if palette.glows {
                let shape = CGPath(roundedRect: rect, cornerWidth: corner, cornerHeight: corner, transform: nil)
                context.saveGState()
                context.addPath(shape)
                context.clip()
                context.setShadow(offset: .zero, blur: 6 + heat * 18, color: base.opacity(0.55 * opacity).cgColor)
                context.addRect(rect.insetBy(dx: -40, dy: -40))
                context.addPath(shape)
                context.setFillColor(RGBColor.black.cgColor)
                context.fillPath(using: .evenOdd)
                context.restoreGState()
            }
            BoardDrawing.stroke(rect, radius: corner, width: palette.highContrast ? 1 : 1.2, color: edge, in: context)
        }
    }
}
