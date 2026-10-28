import AppKit
import SwiftUI

/// The score card's metrics (DESIGN 12, *Score card*; the prototype's sizes where the section gives none).
enum TaskCardLayout {
    static let width: CGFloat = 300
    static let padding: CGFloat = 14
    static let radius: CGFloat = 12
    static let blockSpacing: CGFloat = 12
    static let contentWidth = width - 2 * padding
    static let dotSize: CGFloat = 7
    /// Line 2 of the header and the facts line up with the project after the dot.
    static let headerIndent: CGFloat = dotSize + 7
    static let sectionTitleGap: CGFloat = 8
    static let dimensionSpacing: CGFloat = 10
    static let compositionHeight: CGFloat = 6
    static let barHeight: CGFloat = 4
}

enum TaskCardFont {
    static let project = Font.system(size: 13, weight: .semibold)
    static let meta = Font.system(size: 11)
    static let eyebrow = Font.system(size: 11, weight: .medium)
    static let number = Font.system(size: 34, weight: .semibold, design: .default)
    /// `–` while none: lighter, so the absent mark never reads as a value.
    static let numberAbsent = Font.system(size: 34, weight: .medium, design: .default)
    static let numberSize: CGFloat = 34
    static let outOf = Font.system(size: 13, weight: .medium).monospacedDigit()
    static let status = Font.system(size: 12)
    static let suite = Font.system(size: 10.8, weight: .medium, design: .monospaced)
    static let sectionTitle = Font.system(size: 11, weight: .semibold)
    static let dimensionLabel = Font.system(size: 12.5, weight: .semibold)
    static let dimensionValue = Font.system(size: 12, weight: .medium).monospacedDigit()
    static let dimensionWord = Font.system(size: 11.5, weight: .medium)
    static let meaning = Font.system(size: 10.5)
    static let percent = Font.system(size: 17, weight: .semibold).monospacedDigit()
    static let cellLabel = Font.system(size: 11)
    static let flag = Font.system(size: 10).monospacedDigit()
    static let signal = Font.system(size: 12)
    static let signalGlyph = Font.system(size: 13, weight: .medium)
    static let fact = Font.system(size: 11).monospacedDigit()
    static let footnote = Font.system(size: 10.5)
}

/// The quiet tone of the card's reading text (meanings, the prediction note, the footnote): the prototype's
/// description ink, between secondary and tertiary, so two-line text at 10.5 pt keeps 4.5:1 on the popover material
/// (DESIGN 7). Increase Contrast moves it to the secondary label color.
struct TaskCardDescriptionStyle: ShapeStyle {
    func resolve(in environment: EnvironmentValues) -> Color {
        if environment.colorSchemeContrast == .increased { return Color(nsColor: .secondaryLabelColor) }
        return environment.colorScheme == .dark
            ? Color(.sRGB, red: 235 / 255, green: 235 / 255, blue: 245 / 255, opacity: 0.56)
            : Color(.sRGB, red: 60 / 255, green: 60 / 255, blue: 67 / 255, opacity: 0.72)
    }
}

extension ShapeStyle where Self == TaskCardDescriptionStyle {
    static var taskCardDescription: TaskCardDescriptionStyle { TaskCardDescriptionStyle() }
}

/// A hairline in the separator color, one device pixel thick.
struct TaskCardHairline: View {
    @Environment(\.displayScale) private var scale

    var body: some View {
        Rectangle()
            .fill(Color(nsColor: .separatorColor))
            .frame(height: 1 / max(scale, 1))
            .accessibilityHidden(true)
    }
}

/// A section's small title (評分構成, 預測, 行為).
struct TaskCardSectionTitle: View {
    let title: String

    var body: some View {
        Text(title)
            .font(TaskCardFont.sectionTitle)
            .foregroundStyle(.tertiary)
            .padding(.bottom, TaskCardLayout.sectionTitleGap)
    }
}
