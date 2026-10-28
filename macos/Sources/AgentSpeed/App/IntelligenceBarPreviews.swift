import AgentSpeedCore
import AppKit
import SwiftUI

/// `--render-previews` group `intelbar`: the menu bar item with the intelligence block (DESIGN 10, Menu bar and
/// taskbar) — quiet and live at every number width, with and without the money block and in value-only mode — in
/// Chinese, English and Japanese on dark and light bars; every `settings.bar.traffic` choice side by side; and
/// 選單列 and 網路流量 with the block chosen, measuring on and off. A red hairline marks the item's end, so a width
/// change between rows shows at once.
@MainActor
enum IntelligenceBarPreviews {
    struct Scenario {
        var name: String
        var index: Double?
        var thinking: Double
    }

    /// Quiet, a zero thinking rate, then every width of both numbers up to a thinking rate past the `8.88k` cell.
    static let scenarios = [
        Scenario(name: "quiet", index: nil, thinking: 0),
        Scenario(name: "125 / 0", index: 125, thinking: 0),
        Scenario(name: "60 / 4.2", index: 60, thinking: 4.2),
        Scenario(name: "125 / 10", index: 125, thinking: 10),
        Scenario(name: "133 / 412", index: 133, thinking: 412),
        Scenario(name: "136 / 1.28k", index: 136, thinking: 1_280),
        Scenario(name: "128 / 12.8k", index: 128, thinking: 12_800),
        Scenario(name: "136 / 1.20M", index: 136, thinking: 1_200_000),
    ]

    static func sums(_ scenario: Scenario) -> IntelligenceSums {
        var sums = IntelligenceSums()
        guard let index = scenario.index else { return sums }
        let output = max(scenario.thinking * 2.5, 40)
        let share = scenario.thinking / output
        sums.output = output
        sums.gradedOutput = output * (index - 20 * share)
        sums.thinking = scenario.thinking
        sums.thinkingBase = output
        return sums
    }

    static func render(model: AppModel, into directory: URL) {
        let settings = model.settings
        let saved = (settings.language, settings.barTraffic, settings.barMode, settings.barSecondary, settings.trafficEnabled)
        defer {
            settings.language = saved.0
            model.l10n.apply(saved.0)
            settings.barTraffic = saved.1
            settings.barMode = saved.2
            settings.barSecondary = saved.3
            settings.trafficEnabled = saved.4
        }
        settings.trafficEnabled = true
        TrafficPreviewData.publish(into: model.traffic)
        for language in [LanguagePreference.zhHant, .en, .ja] {
            settings.language = language
            model.l10n.apply(language)
            for dark in [false, true] {
                let scheme = dark ? "dark" : "light"
                settings.barTraffic = .intelligence
                var images: [NSImage] = []
                for (secondary, mode) in [(false, BarDisplayMode.glyphValue), (true, .glyphValue), (false, .value)] {
                    settings.barSecondary = secondary
                    settings.barMode = mode
                    images += scenarios.map { item(model, sums: sums($0), value: $0.index == nil ? 0 : 412, dark: dark) }
                }
                settings.barSecondary = false
                settings.barMode = .glyphValue
                SurfacePreviews.writeColumn(images, to: directory.appendingPathComponent("intelbar-\(language.rawValue)-\(scheme).png"))
                if language == .zhHant {
                    let choices = BarTraffic.allCases.map { choice -> NSImage in
                        settings.barTraffic = choice
                        return item(model, sums: sums(scenarios[4]), value: 412, dark: dark)
                    }
                    SurfacePreviews.writeColumn(choices, to: directory.appendingPathComponent("intelbar-choices-\(scheme).png"))
                }
            }
        }
        settings.language = .zhHant
        model.l10n.apply(.zhHant)
        settings.barTraffic = .intelligence
        for enabled in [true, false] {
            settings.trafficEnabled = enabled
            let state = enabled ? "on" : "off"
            for dark in [false, true] {
                for pane in [SettingsPane.menuBar, .traffic] {
                    PreviewSnapshot.writeHosted(
                        SettingsPaneView(pane: pane, model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
                        dark: dark,
                        to: directory.appendingPathComponent("intelbar-settings-\(pane.rawValue)-measuring-\(state)-\(dark ? "dark" : "light").png")
                    )
                }
            }
        }
        settings.trafficEnabled = true
        settings.language = .en
        model.l10n.apply(.en)
        PreviewSnapshot.writeHosted(
            SettingsPaneView(pane: .menuBar, model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
            dark: false, to: directory.appendingPathComponent("intelbar-settings-menuBar-en-light.png")
        )
    }

    /// The item at `value` tok/s with the block showing `sums`, on a plain bar, three times its size.
    private static func item(_ model: AppModel, sums: IntelligenceSums, value: Double, dark: Bool) -> NSImage {
        var snapshot = PreviewData.snapshot(speed: value, redline: model.settings.redline)
        snapshot.intelligence = sums
        model.store.publishLive(snapshot)
        PreviewRenderer.snapGauge(model, to: value, now: Clock.now - 3)
        let state = StatusItemState(model: model, time: Clock.now)
        let size = CGSize(width: 330, height: state.layout.height)
        let image = NSImage(size: size, flipped: true) { rect in
            (dark ? NSColor(white: 0.16, alpha: 1) : NSColor(white: 0.93, alpha: 1)).setFill()
            rect.fill()
            NSColor.systemRed.withAlphaComponent(0.35).setFill()
            CGRect(x: 10 + state.layout.length, y: 0, width: 1, height: rect.height).fill()
            NSGraphicsContext.current?.cgContext.translateBy(x: 10, y: 0)
            StatusItemArtist.draw(state, dark: dark, in: CGRect(origin: .zero, size: CGSize(width: state.layout.length,
                                                                                          height: state.layout.height)))
            return true
        }
        let scale: CGFloat = 3
        return NSImage(size: CGSize(width: size.width * scale, height: size.height * scale), flipped: false) { rect in
            image.draw(in: rect)
            return true
        }
    }
}
