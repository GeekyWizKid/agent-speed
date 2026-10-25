import AgentSpeedCore
import SwiftUI

/// Colors of the effort capsule, its meter and bolts (DESIGN 4, Agents), in both appearances and with Increase Contrast.
struct EffortCapsulePalette {
    let dark: Bool
    let increasedContrast: Bool

    init(dark: Bool, increasedContrast: Bool) {
        self.dark = dark
        self.increasedContrast = increasedContrast
    }

    init(_ environment: EnvironmentValues) {
        self.init(dark: environment.colorScheme == .dark, increasedContrast: environment.colorSchemeContrast == .increased)
    }

    var fill: Color { increasedContrast ? .clear : Color.primary.opacity(dark ? 0.07 : 0.055) }
    var stroke: Color { increasedContrast ? .primary : Color.primary.opacity(0.08) }
    var strokeWidth: CGFloat { increasedContrast ? 1 : 0.5 }

    var unlit: Color {
        increasedContrast ? Color(nsColor: .disabledControlTextColor) : Color.primary.opacity(dark ? 0.18 : 0.14)
    }

    var secondary: Color { DashboardPalette.secondary(dark: dark, increasedContrast: increasedContrast) }

    var bolt: Color {
        increasedContrast ? .accentColor : RGBColor(hex: dark ? 0xF5A524 : 0xB86E00).color
    }

    /// Fill of a lit bar whose top sits at `top` (0–1) of the meter height; the top rung's gradient spans the meter.
    func litBar(_ tone: EffortTone, top: Double) -> LinearGradient {
        let colors: [Color]
        switch tone {
        case _ where increasedContrast: colors = [.primary, .primary]
        case .neutral: colors = [secondary, secondary]
        case .max: colors = Array(repeating: DashboardPalette.heatGraphic(0.82, dark: dark, increasedContrast: false).color, count: 2)
        case .topRung: colors = [heat(Self.topRungRange.lowerBound + topRungSpan * top), heat(Self.topRungRange.lowerBound)]
        }
        return LinearGradient(colors: colors, startPoint: .top, endPoint: .bottom)
    }

    /// Color of the word: secondary, heat 0.82 for `max`, the top rung's gradient running along the word.
    func word(_ tone: EffortTone) -> AnyShapeStyle {
        if increasedContrast { return AnyShapeStyle(Color.primary) }
        switch tone {
        case .neutral: return AnyShapeStyle(secondary)
        case .max: return AnyShapeStyle(DashboardPalette.heatText(0.82, dark: dark, increasedContrast: false))
        case .topRung:
            let range = Self.topRungRange
            return AnyShapeStyle(LinearGradient(
                colors: [
                    DashboardPalette.heatText(range.lowerBound, dark: dark, increasedContrast: false),
                    DashboardPalette.heatText(range.upperBound, dark: dark, increasedContrast: false),
                ],
                startPoint: .leading,
                endPoint: .trailing
            ))
        }
    }

    /// Share bar of a history row: neutral up to `xhigh`, heat 0.82 for `max`, the top rung's gradient.
    func shareBar(_ tone: EffortTone) -> AnyShapeStyle {
        if increasedContrast { return AnyShapeStyle(Color.primary) }
        switch tone {
        case .neutral: return AnyShapeStyle(Color.primary.opacity(dark ? 0.42 : 0.36))
        case .max: return AnyShapeStyle(heat(0.82))
        case .topRung:
            let range = Self.topRungRange
            return AnyShapeStyle(LinearGradient(colors: [heat(range.lowerBound), heat(range.upperBound)], startPoint: .leading, endPoint: .trailing))
        }
    }

    var unknownBar: Color { Color.primary.opacity(0.12) }

    static let topRungRange = 0.72...1.0
    private var topRungSpan: Double { Self.topRungRange.upperBound - Self.topRungRange.lowerBound }

    private func heat(_ h: Double) -> Color {
        DashboardPalette.heatGraphic(h, dark: dark, increasedContrast: false).color
    }
}

/// How an effort token is colored: neutral up to `xhigh`, `max`, and the top rung (`ultra`, `ultracode`).
enum EffortTone: Equatable, Sendable {
    case neutral, max, topRung

    init(_ effort: String?) {
        switch effort {
        case "max": self = .max
        case let value? where ModeDisplay.isTopRung(value): self = .topRung
        default: self = .neutral
        }
    }
}
