import AgentSpeedCore
import SwiftUI

/// The agent row's lines about how it works (DESIGN 4, Looping mark) and how strong its model is (DESIGN 10,
/// Intelligence readout). Descriptive only: they never change the row's order or value.
@MainActor
enum AgentSignalText {
    /// `work.looping` while the agent goes in circles: the row's first tooltip line and part of its label.
    static func looping(_ signals: WorkSignals, _ l10n: Localizer) -> String? {
        signals.looping.map { l10n("work.looping", $0) }
    }

    /// The tooltip's closing lines: `work.repeating` while it repeats without failures, then `work.summary` once it has
    /// enough recent actions.
    static func closing(_ signals: WorkSignals, _ l10n: Localizer) -> [String] {
        var lines: [String] = []
        if let count = signals.repeating { lines.append(l10n("work.repeating", count)) }
        if let novelty = signals.novelty {
            lines.append(l10n("work.summary", HistoryText.percent(novelty), signals.failed, signals.recovered))
        }
        return lines
    }

    /// `intel.grade`: the grade of the agent's current model and effort, with its class.
    static func grade(_ grade: IntelligenceGrade, _ l10n: Localizer) -> String {
        l10n("intel.grade", String(grade.value), l10n(grade.modelClass.titleKey))
    }
}

extension ModelClass {
    var titleKey: String {
        switch self {
        case .flagship: "intel.class.flagship"
        case .standard: "intel.class.standard"
        case .light: "intel.class.light"
        }
    }
}

/// The looping mark (DESIGN 4): an amber `arrow.triangle.2.circlepath`, 11 pt, before the project name. The row's
/// label carries `work.looping`, so the glyph itself is decoration.
struct LoopingMark: View {
    static let fadeIn: TimeInterval = 0.25
    static let fadeOut: TimeInterval = 0.6
    static let gap: CGFloat = 4

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let dark = colorScheme == .dark
        Image(systemName: "arrow.triangle.2.circlepath")
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(DashboardPalette.legible(DashboardPalette.amber(dark: dark), dark: dark,
                                                      minimum: contrast == .increased ? 4.5 : 3).color)
            .accessibilityHidden(true)
    }

    /// Fades in quickly and out slowly; Reduce Motion shows and hides it at once.
    static func animation(appearing: Bool, reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : appearing ? .easeOut(duration: fadeIn) : .easeIn(duration: fadeOut)
    }
}
