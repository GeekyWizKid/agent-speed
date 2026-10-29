import AgentSpeedCore
import AppKit
import SwiftUI

/// `--render-previews` group `taskcard`: the score card in every state (DESIGN 12) on its solid surface (the Reduce
/// Transparency look; the material itself needs a real window, see `--screenshots` group `taskcard`). One sheet per
/// language and appearance, Increase Contrast, and each card alone in Chinese.
@MainActor
enum TaskScoreCardPreviews {
    static func render(model: AppModel, into directory: URL) {
        let saved = model.settings.language
        defer {
            model.settings.language = saved
            model.l10n.apply(saved)
        }
        for dark in [false, true] {
            write(TaskProofMarkSheet(), dark: dark,
                  to: directory.appendingPathComponent("taskcard-proofmarks-\(dark ? "dark" : "light").png"))
            write(TaskProofMarkSheet().environment(\._colorSchemeContrast, .increased), dark: dark,
                  to: directory.appendingPathComponent("taskcard-proofmarks-contrast-\(dark ? "dark" : "light").png"))
        }
        for language in [LanguagePreference.zhHant, .en, .ja] {
            model.settings.language = language
            model.l10n.apply(language)
            let cases = TaskScoreCardPreviewData.cases(model.l10n)
            for dark in language == .ja ? [true] : [false, true] {
                let suffix = "\(language.rawValue)-\(dark ? "dark" : "light")"
                write(Sheet(cases: cases, l10n: model.l10n, dark: dark), dark: dark,
                      to: directory.appendingPathComponent("taskcard-sheet-\(suffix).png"))
                if language == .zhHant, let failing = cases.first(where: { $0.name == "failing" }) {
                    // DESIGN 12, *Fit*: full, without the meanings, without the footnote too.
                    write(HStack(alignment: .top, spacing: 24) {
                        ForEach(TaskScoreCardFit.allCases, id: \.self) { fit in
                            TaskScoreCardView(model: failing.model, l10n: model.l10n, fit: fit)
                                .modifier(TaskScoreCardSolidSurface())
                        }
                    }.padding(28), dark: dark, to: directory.appendingPathComponent("taskcard-fit-\(suffix).png"))
                }
                if language == .zhHant {
                    write(Sheet(cases: cases, l10n: model.l10n, dark: dark).environment(\._colorSchemeContrast, .increased),
                          dark: dark, to: directory.appendingPathComponent("taskcard-sheet-contrast-\(suffix).png"))
                    for item in cases {
                        write(Single(item: item, l10n: model.l10n, dark: dark), dark: dark,
                              to: directory.appendingPathComponent("taskcard-\(item.name)-\(suffix).png"))
                    }
                }
            }
        }
    }

    private static func write<V: View>(_ view: V, dark: Bool, to url: URL) {
        PreviewSnapshot.writeHosted(view, dark: dark, background: Desk.color(dark: dark), to: url)
    }

    private enum Desk {
        static func color(dark: Bool) -> NSColor {
            dark ? NSColor(srgbRed: 0x12 / 255, green: 0x13 / 255, blue: 0x18 / 255, alpha: 1)
                : NSColor(srgbRed: 0xE6 / 255, green: 0xE8 / 255, blue: 0xEE / 255, alpha: 1)
        }
    }

    private struct Card: View {
        let model: TaskScoreCardModel
        let l10n: Localizer
        let dark: Bool

        var body: some View {
            TaskScoreCardView(model: model, l10n: l10n)
                .modifier(TaskScoreCardSolidSurface())
                .shadow(color: .black.opacity(dark ? 0.5 : 0.16), radius: 18, y: 10)
                .shadow(color: .black.opacity(dark ? 0.3 : 0.08), radius: 3, y: 1)
        }
    }

    private struct Single: View {
        let item: TaskScoreCardPreviewData.Case
        let l10n: Localizer
        let dark: Bool

        var body: some View {
            Card(model: item.model, l10n: l10n, dark: dark)
                .padding(28)
        }
    }

    private struct Sheet: View {
        let cases: [TaskScoreCardPreviewData.Case]
        let l10n: Localizer
        let dark: Bool

        var body: some View {
            let rows = stride(from: 0, to: cases.count, by: 3).map { Array(cases[$0..<min($0 + 3, cases.count)]) }
            Grid(alignment: .topLeading, horizontalSpacing: 24, verticalSpacing: 28) {
                ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                    GridRow(alignment: .top) {
                        ForEach(row, id: \.name) { item in
                            VStack(alignment: .leading, spacing: 8) {
                                Text(item.name.uppercased())
                                    .font(.system(size: 10.5, weight: .semibold))
                                    .kerning(0.8)
                                    .foregroundStyle(.secondary)
                                Card(model: item.model, l10n: l10n, dark: dark)
                            }
                        }
                    }
                }
            }
            .padding(32)
        }
    }
}
