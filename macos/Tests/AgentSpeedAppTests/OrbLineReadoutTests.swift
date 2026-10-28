import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 10, Intelligence readout: a larger intelligence line over a smaller thinking line, each centred on the orb on
/// its own; the network readout keeps its fixed cells.
@Suite("Orb line readout")
@MainActor
struct OrbLineReadoutTests {
    static let sizes: [CGFloat] = Array(stride(from: 80, through: 280, by: 4))

    // MARK: Glyphs

    @Test func glyphsSpellIntelligenceAndThinking() {
        let expected: [AppLanguage: [String]] = [.zhHant: ["智力", "思考"], .zhHans: ["智力", "思考"], .en: ["IQ", "TH"], .ja: ["知", "思考"]]
        for (language, glyphs) in expected {
            let actual = OrbLineGlyphs(language)
            #expect([actual.intelligence, actual.thinking] == glyphs)
        }
    }

    // MARK: Baselines and stack shift

    @Test func aboveTheValueThinkingIsNearestAndIntelligenceSitsOnePitchAbove() {
        for placement in [OrbLinePlacement.led, .liquid, .minimal] {
            let t = placement.textSize(diameter: 148)
            let lines = placement.intelligenceBaselines(center: 200, diameter: 148)
            #expect(abs(lines.thinking - (200 + placement.nearestBaseline * 148)) < 1e-9)
            #expect(abs(lines.intelligence - (lines.thinking - 1.32 * t)) < 1e-9)
        }
    }

    @Test func reactorLowersTheThinkingLineSoTheIntelligenceLineClearsTheRim() {
        let placement = OrbLinePlacement.reactor
        let t = placement.textSize(diameter: 196)
        let lines = placement.intelligenceBaselines(center: 0, diameter: 196)
        #expect(abs(lines.thinking - -0.155 * 196) < 1e-9)
        #expect(abs(lines.intelligence - (lines.thinking - 1.32 * t)) < 1e-9)
        // The network readout keeps its baselines.
        #expect(abs(placement.baselines(center: 0, diameter: 196).nearest - -0.175 * 196) < 1e-9)
    }

    @Test func belowTheValueTheIntelligenceLineKeepsATextLinesCapTop() {
        let placement = OrbLinePlacement.needle
        for s in [148, 196] as [CGFloat] {
            let t = placement.textSize(diameter: s)
            let p = 1.25 * t
            let lines = placement.intelligenceBaselines(center: 0, diameter: s)
            #expect(abs((lines.intelligence - 0.70 * p) - (0.108 * s - 0.70 * t)) < 1e-9)
            #expect(abs(lines.thinking - (lines.intelligence + 1.32 * t)) < 1e-9)
        }
    }

    @Test func stackShiftMeasuresTheBlockFromTheLargerIntelligenceLine() {
        for style in [OrbStyleKind.led, .liquid, .minimal] {
            var layout = OrbStyles.make(style).readoutLayout
            layout.dots = nil
            let placement = OrbLinePlacement.of(style)
            for s in [112, 148, 196] as [CGFloat] {
                let t = placement.textSize(diameter: s)
                let network = placement.stackShift(layout: layout, diameter: s, format: .traffic(bits: false))
                let intelligence = placement.stackShift(layout: layout, diameter: s, format: .intelligence)
                #expect(network > 0)
                #expect(abs(intelligence - network - 0.70 * 0.25 * t / 2) < 1e-9)
            }
        }
        for style in [OrbStyleKind.reactor, .needle] {
            let shift = OrbLinePlacement.of(style).stackShift(layout: OrbStyles.make(style).readoutLayout, diameter: 196,
                                                              format: .intelligence)
            #expect(shift == 0)
        }
    }

    @Test func stackShiftStillStopsAboveTheAgentDots() {
        let layout = OrbStyles.make(.led).readoutLayout
        for s in Self.sizes {
            let shift = OrbLinePlacement.led.stackShift(layout: layout, diameter: s, format: .intelligence)
            let titleBottom = layout.titleOffset * s + shift + 0.22 * max(7, layout.titleSize * s)
            let dotsTop = (layout.dots ?? 1) * s - OrbAgentDotsLayout.dotDiameter(s) / 2
            #expect(shift == 0 || titleBottom <= dotsTop - 0.05 * s + 1e-9)
        }
    }

    // MARK: Fit

    @Test func widestLinesCarryTheirWidestText() {
        let t: CGFloat = 9
        let full = OrbLineMetrics.widest(size: t, language: .zhHant)
        let bare = OrbLineMetrics.widest(size: t, language: .zhHant, unit: false)
        #expect(full.intelligence == bare.intelligence)
        #expect(full.thinking > bare.thinking + 0.16 * t + 1.5 * t)
        #expect(full.line == max(full.intelligence, full.thinking))
        // 思考 is wider than TH; 智力 than 知.
        #expect(full.thinking > OrbLineMetrics.widest(size: t, language: .en).thinking)
        #expect(full.intelligence > OrbLineMetrics.widest(size: t, language: .ja).intelligence)
    }

    @Test func openStylesFitByWidthAlone() {
        for style in [OrbStyleKind.led, .liquid, .reactor, .minimal] {
            let placement = OrbLinePlacement.of(style)
            for language in AppLanguage.allCases {
                for s in Self.sizes {
                    let widest = OrbLineMetrics.widest(size: placement.textSize(diameter: s), language: language)
                    #expect(OrbLineMetrics.fits(placement, diameter: s, format: .intelligence, language: language)
                        == (widest.line <= placement.fit * s))
                }
            }
        }
    }

    @Test func needleKeepsBothLinesInsideItsHub() {
        let placement = OrbLinePlacement.needle
        var hubDecided = false
        for language in AppLanguage.allCases {
            for s in Self.sizes {
                let t = placement.textSize(diameter: s)
                let widest = OrbLineMetrics.widest(size: t, language: language, unit: false)
                let lines = placement.intelligenceBaselines(center: 0, diameter: s)
                let inside = hypot(widest.intelligence / 2, lines.intelligence) <= 0.223 * s
                    && hypot(widest.thinking / 2, lines.thinking) <= 0.223 * s
                let fits = OrbLineMetrics.fits(placement, diameter: s, format: .intelligence, language: language)
                #expect(fits == (inside && widest.line <= 0.285 * s))
                if widest.line <= 0.285 * s, !inside { hubDecided = true }
                // With `tok/s` the thinking line could never fit the hub in Chinese.
                if language == .zhHant {
                    #expect(OrbLineMetrics.widest(size: t, language: language).thinking > 0.285 * s)
                }
            }
        }
        // At the 7 pt floor the hub, not the width, hides the readout of small orbs.
        #expect(hubDecided)
    }

    @Test func everyStyleShowsTheReadoutAtTheDefaultSizeAndTheSmallOnesWhereTheNetworkDoes() {
        for language in AppLanguage.allCases {
            for style in OrbStyleKind.allCases {
                #expect(OrbLineMetrics.fits(.of(style), diameter: 148, format: .intelligence, language: language))
            }
            for style in [OrbStyleKind.led, .liquid, .minimal] {
                #expect(OrbLineMetrics.fits(.of(style), diameter: 112, format: .intelligence, language: language))
            }
            for style in [OrbStyleKind.reactor, .needle] {
                #expect(!OrbLineMetrics.fits(.of(style), diameter: 112, format: .intelligence, language: language))
                #expect(!OrbLineMetrics.fits(.of(style), diameter: 112, format: .traffic(bits: false), language: language))
            }
        }
    }

    @Test func networkReadoutFitsByItsCells() {
        for style in OrbStyleKind.allCases {
            let placement = OrbLinePlacement.of(style)
            for bits in [false, true] {
                for s in Self.sizes {
                    let t = placement.textSize(diameter: s)
                    let cells = OrbLineMetrics.cells(size: t, bits: bits)
                    #expect(cells.lead == 0.50 * t)
                    #expect(OrbLineMetrics.fits(placement, diameter: s, format: .traffic(bits: bits), language: .zhHant)
                        == (cells.lineWidth(size: t) <= placement.fit * s))
                }
            }
        }
    }

    // MARK: Drawing

    private struct Piece {
        var x: CGFloat
        var right: CGFloat
        var baseline: CGFloat
        var height: CGFloat
    }

    private static func frame(_ style: OrbStyleKind, diameter s: CGFloat, language: AppLanguage, format: OrbLineFormat,
                              _ content: OrbLineContent) -> OrbFrame {
        var c = OrbContent.placeholder(diameter: s)
        c.style = style
        c.language = language
        c.lines = OrbLines(format: format, reading: OrbLineReading(content: content, shownAt: -.infinity, quietAt: 1e9))
        let geometry = OrbGeometry(diameter: s, canvasSide: s * 1.5)
        return OrbFrame(time: 100, dt: 0, geometry: geometry, content: c,
                        effects: OrbEffects(level: .standard, reduceMotion: false, surface: .floating), heat: 0, overdrive: 0,
                        rawHeat: 0, rawOverdrive: 0, displayHeat: 0, tier: .idle, redline: 400, tierChangedAt: -.infinity,
                        titleChangedAt: -.infinity, flash: 0, breath: 1, breathPhase: 0, redlineAmount: 0, activity: 0,
                        signOfLife: nil, peakAge: .infinity, alpha: 1)
    }

    /// The readout's text pieces, grouped by baseline from top to bottom.
    private static func lines(_ frame: OrbFrame, placement: OrbLinePlacement) -> [[Piece]] {
        var scene = OrbScene(capacity: 32)
        OrbLineReadout().add(into: &scene, frame, placement: placement, palette: .standard(dark: true, increaseContrast: false))
        let pieces = scene.texts.map { draw in
            let rect = draw.instance.rect
            let x = CGFloat(rect.x) + draw.texture.padding
            return Piece(x: x, right: x + draw.texture.advance, baseline: CGFloat(rect.y) + draw.texture.baseline,
                         height: draw.texture.size.height)
        }
        var lines: [[Piece]] = []
        for piece in pieces.sorted(by: { $0.baseline < $1.baseline }) {
            if let last = lines.last?.first, abs(last.baseline - piece.baseline) < 0.01 {
                lines[lines.count - 1].append(piece)
            } else {
                lines.append([piece])
            }
        }
        return lines.map { $0.sorted { $0.x < $1.x } }
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func eachIntelligenceLineIsCentredOnItsOwnOnDevicePixels() throws {
        let cases: [(OrbStyleKind, AppLanguage, Int, String)] = [
            (.led, .zhHant, 133, "412"), (.liquid, .zhHant, 88, "9.8"), (.reactor, .en, 125, "10"),
            (.minimal, .ja, 125, "0"), (.needle, .zhHant, 133, "412"), (.needle, .en, 88, "0"),
        ]
        for (style, language, index, thinking) in cases {
            for s in [148, 196] as [CGFloat] {
                let placement = OrbLinePlacement.of(style)
                let frame = Self.frame(style, diameter: s, language: language, format: .intelligence,
                                       .intelligence(index: index, thinking: thinking))
                let lines = Self.lines(frame, placement: placement)
                try #require(lines.count == 2)
                let (intelligence, thinkingLine) = (lines[0], lines[1])
                let expected = placement.intelligenceBaselines(center: frame.center.y, diameter: s)
                #expect(abs(intelligence[0].baseline - expected.intelligence) < 0.01)
                #expect(abs(thinkingLine[0].baseline - expected.thinking) < 0.01)
                // Glyph, digits (and tok/s, except in needle).
                #expect(intelligence.count == 1 + String(index).count)
                #expect(thinkingLine.count == 1 + thinking.count + (placement.thinkingUnit ? 1 : 0))
                for line in lines {
                    let middle = (line.map(\.x).min()! + line.map(\.right).max()!) / 2
                    #expect(abs(middle - frame.center.x) <= 0.26, "\(style) \(s) \(language)")
                    for piece in line {
                        #expect(abs(piece.x * 2 - (piece.x * 2).rounded()) < 0.01)
                    }
                }
                // The intelligence digits are 1.25 × the thinking digits.
                let big = intelligence[1].right - intelligence[1].x
                let small = thinkingLine[1].right - thinkingLine[1].x
                #expect(abs(big / small - 1.25) < 0.06)
            }
        }
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func aLineMovesOnlyWhenItsDigitCountChanges() throws {
        func left(_ index: Int, _ thinking: String) throws -> (CGFloat, CGFloat) {
            let frame = Self.frame(.led, diameter: 148, language: .zhHant, format: .intelligence,
                                   .intelligence(index: index, thinking: thinking))
            let lines = Self.lines(frame, placement: .led)
            try #require(lines.count == 2)
            return (lines[0][0].x, lines[1][0].x)
        }
        #expect(try left(125, "412") == left(133, "987"))
        #expect(try left(88, "412").0 > left(125, "412").0)
        #expect(try left(125, "9.8").1 > left(125, "412").1)
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func nothingIsDrawnWhereTheLinesDoNotFit() {
        for style in [OrbStyleKind.reactor, .needle] {
            let frame = Self.frame(style, diameter: 112, language: .zhHant, format: .intelligence,
                                   .intelligence(index: 133, thinking: "412"))
            #expect(Self.lines(frame, placement: .of(style)).isEmpty)
        }
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func networkLinesShareTheirLeftEdgeInFixedCells() throws {
        let rx = TrafficFormat.rateParts(12_400_000)
        let tx = TrafficFormat.rateParts(186_000)
        for style in OrbStyleKind.allCases {
            let placement = OrbLinePlacement.of(style)
            let frame = Self.frame(style, diameter: 196, language: .zhHant, format: .traffic(bits: false), .traffic(rx: rx, tx: tx))
            let lines = Self.lines(frame, placement: placement)
            try #require(lines.count == 2)
            let t = placement.textSize(diameter: 196)
            let cells = OrbLineMetrics.cells(size: t, bits: false)
            let left = frame.center.x - cells.lineWidth(size: t) / 2
            let numberEnd = left + cells.lead + 0.16 * t + cells.number
            for line in lines {
                #expect(abs(line[0].x - left) < 0.01)
                #expect(abs(line[line.count - 2].right - numberEnd) < 0.01)
                #expect(abs(line[line.count - 1].x - (numberEnd + 0.16 * t)) < 0.01)
            }
            // Upload is nearest above the value, download nearest below it.
            let baselines = placement.baselines(center: frame.center.y, diameter: 196)
            let (top, bottom) = placement.below ? (baselines.nearest, baselines.other) : (baselines.other, baselines.nearest)
            #expect(abs(lines[0][0].baseline - top) < 0.01)
            #expect(abs(lines[1][0].baseline - bottom) < 0.01)
        }
    }
}
