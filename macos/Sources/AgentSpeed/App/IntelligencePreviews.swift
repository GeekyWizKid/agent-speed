import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import SwiftUI

/// `--render-previews` group `intel`: the intelligence readout of every style at 112, 148 and 196 pt in four
/// situations, in Chinese and English, a state sheet and the smallest sizes per appearance, the thinking line hidden
/// at zero, the network and intelligence readouts side by side, and the dashboard with a looping agent (DESIGN 4,
/// Looping mark; DESIGN 10, Intelligence readout).
@MainActor
enum IntelligencePreviews {
    struct Scenario {
        var index: Int
        var thinking: Double
    }

    /// A flagship thinking hard, a flagship and a standard model thinking lightly, a flagship writing without
    /// reasoning (its thinking line long hidden): every width of both lines.
    static let scenarios = [
        Scenario(index: 133, thinking: 412),
        Scenario(index: 125, thinking: 10),
        Scenario(index: 88, thinking: 9.8),
        Scenario(index: 125, thinking: 0),
    ]

    static func content(_ scenario: Scenario) -> OrbLineContent {
        .intelligence(index: scenario.index, thinking: SpeedFormat.speed(scenario.thinking))
    }

    static func render(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        model.store.publishLive(PreviewData.snapshot(speed: redline * 0.3, redline: redline))
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            for language in [AppLanguage.zhHant, .en] {
                let rows = OrbStyleKind.allCases.map { style in
                    scenarios.flatMap { scenario in
                        SurfacePreviews.sizes.compactMap { size in
                            still(model: model, style: style, size: size, dark: dark) { stage in
                                stage.language = language
                                show(stage, scenario)
                            }
                        }
                    }
                }
                let name = language == .en ? "orb-intel-en-\(scheme).png" : "orb-intel-\(scheme).png"
                SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent(name))
            }
            writeStates(model: model, dark: dark, into: directory)
            writeSmallest(model: model, dark: dark, into: directory)
            writeAlone(model: model, dark: dark, into: directory)
            writeModeSwitch(model: model, dark: dark, into: directory)
            writeDashboard(model: model, dark: dark, into: directory)
        }
    }

    /// Columns: quiet (needle keeps its unit on the dial), steady, fading out, new-peak pill, Increase Contrast,
    /// hovered, English and Japanese glyphs, 112 pt.
    private static func writeStates(model: AppModel, dark: Bool, into directory: URL) {
        let busy = scenarios[0]
        let rows = OrbStyleKind.allCases.map { style -> [NSImage] in
            [
                still(model: model, style: style, size: 148, dark: dark, value: 0) { stage in
                    stage.feed.lines = OrbLines(format: .intelligence, reading: nil)
                },
                still(model: model, style: style, size: 148, dark: dark) { show($0, busy) },
                still(model: model, style: style, size: 148, dark: dark) { show($0, scenarios[1], quietIn: -0.3) },
                still(model: model, style: style, size: 148, dark: dark, peak: true) { show($0, busy) },
                still(model: model, style: style, size: 148, dark: dark) { stage in
                    stage.increaseContrast = true
                    show(stage, busy)
                },
                still(model: model, style: style, size: 148, dark: dark, hover: true) { show($0, busy) },
                still(model: model, style: style, size: 148, dark: dark) { stage in
                    stage.language = .en
                    show(stage, busy)
                },
                still(model: model, style: style, size: 148, dark: dark) { stage in
                    stage.language = .ja
                    show(stage, busy)
                },
                still(model: model, style: style, size: 112, dark: dark) { show($0, busy) },
            ].compactMap { $0 }
        }
        SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent("orb-intel-states-\(dark ? "dark" : "light").png"))
    }

    /// Every style at the smallest size that shows the readout, in Chinese, Japanese and English: the widest lines
    /// against the LED arc, the core's rim and the needle's hub.
    private static func writeSmallest(model: AppModel, dark: Bool, into directory: URL) {
        let widest = Scenario(index: 888, thinking: 888)
        let rows = OrbStyleKind.allCases.map { style -> [NSImage] in
            [AppLanguage.zhHant, .ja, .en].compactMap { language in
                let placement = OrbLinePlacement.of(style)
                guard let size = (64...240).first(where: {
                    OrbLineMetrics.fits(placement, diameter: CGFloat($0), format: .intelligence, language: language)
                }) else { return nil }
                return still(model: model, style: style, size: CGFloat(size), dark: dark) { stage in
                    stage.language = language
                    show(stage, widest)
                }
            }
        }
        SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent("orb-intel-smallest-\(dark ? "dark" : "light").png"))
    }

    /// Thinking hidden at zero, every style at 148 and 196 pt: thinking above 0, the zero within its hold, 0.12 s into
    /// the fade and the glide, and the intelligence line alone where the line next to the value would be.
    private static func writeAlone(model: AppModel, dark: Bool, into directory: URL) {
        let busy = Scenario(index: 125, thinking: 10)
        let zero = Scenario(index: 125, thinking: 0)
        let rows = OrbStyleKind.allCases.map { style -> [NSImage] in
            ([148, 196] as [CGFloat]).flatMap { size in
                [
                    still(model: model, style: style, size: size, dark: dark) { show($0, busy) },
                    still(model: model, style: style, size: size, dark: dark) { show($0, zero, zeroFor: 1.5) },
                    still(model: model, style: style, size: size, dark: dark) { show($0, zero, zeroFor: OrbThinkingLine.hold + 0.12) },
                    still(model: model, style: style, size: size, dark: dark) { show($0, zero) },
                ].compactMap { $0 }
            }
        }
        SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent("orb-intel-alone-\(dark ? "dark" : "light").png"))
    }

    /// Network speed, then intelligence, for every style at 196 pt: the value and the title keep their places.
    private static func writeModeSwitch(model: AppModel, dark: Bool, into directory: URL) {
        let rows = OrbStyleKind.allCases.map { style -> [NSImage] in
            [
                still(model: model, style: style, size: 196, dark: dark) { TrafficOrbPreviews.show($0, rx: 12_400_000, tx: 186_000) },
                still(model: model, style: style, size: 196, dark: dark) { show($0, scenarios[0]) },
            ].compactMap { $0 }
        }
        SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent("orb-intel-switch-\(dark ? "dark" : "light").png"))
    }

    /// The agents list with `nova` going in circles.
    private static func writeDashboard(model: AppModel, dark: Bool, into directory: URL) {
        var snapshot = PreviewData.snapshot(speed: model.settings.redline * 0.4, redline: model.settings.redline)
        if let index = snapshot.agents.firstIndex(where: { $0.project == "nova" }) {
            snapshot.agents[index].signals = WorkSignals(looping: 3, novelty: 0.4, failed: 3)
        }
        PreviewRenderer.publish(model, snapshot: snapshot)
        PreviewSnapshot.writeHosted(
            DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
            dark: dark,
            to: directory.appendingPathComponent("dashboard-looping-\(dark ? "dark" : "light").png")
        )
    }

    /// Shows `scenario` in steady state; a thinking rate that reads 0 has read 0 for `zeroFor` seconds when the still is
    /// taken.
    private static func show(_ stage: OrbPreviewStage, _ scenario: Scenario, quietIn: TimeInterval = 60, zeroFor: TimeInterval = 60) {
        let content = content(scenario)
        var thinking = OrbThinkingLine()
        thinking.update(zero: content.thinkingReadsZero, at: stage.time + TrafficOrbPreviews.settle - zeroFor)
        TrafficOrbPreviews.show(stage, content, format: .intelligence, quietIn: quietIn, thinking: thinking)
    }

    /// A still at a mid tier of the style (as the network readout's previews).
    private static func still(model: AppModel, style: OrbStyleKind, size: CGFloat, dark: Bool, value: Double? = nil,
                              peak: Bool = false, hover: Bool = false,
                              configure: (OrbPreviewStage) -> Void) -> NSImage? {
        let speed = value ?? TrafficOrbPreviews.value(style, redline: model.settings.redline)
        return TrafficOrbPreviews.still(model: model, style: style, size: size, dark: dark, value: speed, peak: peak,
                                        hover: hover, configure: configure).map(SurfacePreviews.pointImage)
    }
}
