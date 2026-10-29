import AppKit
import SwiftUI

/// The task score's colors (DESIGN 12, *Score card*, *Colors*): the muted slate-blue score ink, its track, the state
/// chips and the prediction cells. Text uses the system label colors.
enum TaskScorePalette {
    /// Bars, the composition and dotted outlines: a quiet indigo of the heat palette, distinct from every source color.
    static func ink(dark: Bool, increasedContrast: Bool) -> Color {
        if increasedContrast { return Color(nsColor: .labelColor) }
        return (dark ? RGBColor(hex: 0x93A4D6) : RGBColor(hex: 0x4B5FA6)).color
    }

    static func track(dark: Bool, increasedContrast: Bool) -> Color {
        if increasedContrast { return Color(nsColor: .labelColor).opacity(0.25) }
        return dark ? Color.white.opacity(0.09) : Color.black.opacity(0.07)
    }

    /// The dots of an unproven track: the tertiary label color, the label color at 100 % with Increase Contrast.
    static func dots(increasedContrast: Bool) -> Color {
        Color(nsColor: increasedContrast ? .labelColor : .tertiaryLabelColor)
    }

    /// The equal prediction cells: the label color at 5 % (dark 6 %).
    static func cellFill(dark: Bool) -> Color {
        dark ? Color.white.opacity(0.06) : Color.black.opacity(0.05)
    }

    /// The card's fill under Reduce Transparency.
    static func solidCard(dark: Bool) -> Color {
        dark ? RGBColor(hex: 0x26272B).color : Color.white
    }

    /// A state chip's text and fill; neutral chips have no fill and a dotted outline.
    struct Chip {
        var text: Color
        var fill: Color?
    }

    static func chip(_ state: TaskScoreState, dark: Bool) -> Chip {
        switch state {
        case .passing:
            dark ? Chip(text: RGBColor(hex: 0x6EE7B7).color, fill: Color(.sRGB, red: 52 / 255, green: 211 / 255, blue: 153 / 255, opacity: 0.14))
                : Chip(text: RGBColor(hex: 0x047857).color, fill: Color(.sRGB, red: 16 / 255, green: 185 / 255, blue: 129 / 255, opacity: 0.12))
        case .failing:
            dark ? Chip(text: RGBColor(hex: 0xFBBF24).color, fill: Color(.sRGB, red: 245 / 255, green: 165 / 255, blue: 36 / 255, opacity: 0.14))
                : Chip(text: RGBColor(hex: 0xB45309).color, fill: Color(.sRGB, red: 245 / 255, green: 158 / 255, blue: 11 / 255, opacity: 0.14))
        case .pending, .unverified, .none:
            Chip(text: Color(nsColor: .secondaryLabelColor), fill: nil)
        }
    }
}
