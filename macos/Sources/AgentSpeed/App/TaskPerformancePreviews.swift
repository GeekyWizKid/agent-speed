import AgentSpeedCore
import AppKit
import SwiftUI

/// `--render-previews` group `taskhistory`: the Usage History 任務表現 section (DESIGN 12) inside a frame like the
/// history column — Codex against Claude Code at the section widths 872 (wide, the default window), 672 (medium, the
/// minimum window), 520 and 360 (narrow) in Chinese and English; the states (empty, re-analysing, one side empty, one
/// source chosen in the window, only one source, a low sample); Increase Contrast.
@MainActor
enum TaskPerformancePreviews {
    private typealias Data = TaskPerformancePreviewData

    struct Case {
        var name: String
        var input: TaskPerformanceInput
        var pair: TaskPerformancePair?
        var widths: [CGFloat]
    }

    static let widths: [CGFloat] = [872, 672, 520, 360]

    static var states: [Case] {
        [
            Case(name: "empty", input: TaskPerformanceInput(comparison: .empty), widths: [872, 360]),
            Case(name: "analyzing", input: TaskPerformanceInput(comparison: .empty, analyzing: true), widths: [872, 672, 360]),
            Case(name: "oneempty", input: TaskPerformanceInput(comparison: Data.comparison(Data.records.filter { $0.source != .pi })),
                 pair: TaskPerformancePair(first: .pi, second: .codex), widths: [872, 360]),
            Case(name: "narrowed", input: TaskPerformanceInput(comparison: Data.comparison(narrowedTo: .codex), narrowedTo: .codex),
                 widths: [872, 360]),
            Case(name: "onlyone", input: TaskPerformanceInput(comparison: Data.comparison(Data.records.filter { $0.source == .claudeCode })),
                 widths: [672]),
            Case(name: "lowsample", input: TaskPerformanceInput(comparison: Data.comparison()),
                 pair: TaskPerformancePair(first: .codex, second: .claudeDesktop), widths: [872, 360]),
        ]
    }

    static func render(model: AppModel, into directory: URL) {
        let saved = model.settings.language
        defer {
            model.settings.language = saved
            model.l10n.apply(saved)
        }
        let main = TaskPerformanceInput(comparison: Data.comparison())
        for language in [LanguagePreference.zhHant, .en] {
            use(language, model)
            for width in widths {
                for dark in [true, false] {
                    write(main, pair: nil, width: width, model: model, dark: dark,
                          name: "taskhistory-\(Int(width))-\(language.rawValue.lowercased())-\(appearance(dark))", into: directory)
                }
            }
        }
        use(.ja, model)
        write(main, pair: nil, width: 672, model: model, dark: true, name: "taskhistory-672-ja-dark", into: directory)
        use(.zhHant, model)
        for state in states {
            for width in state.widths {
                for dark in [true, false] {
                    write(state.input, pair: state.pair, width: width, model: model, dark: dark,
                          name: "taskhistory-\(state.name)-\(Int(width))-zh-hant-\(appearance(dark))", into: directory)
                }
            }
        }
        for dark in [true, false] {
            write(main, pair: nil, width: 872, model: model, dark: dark, contrast: true,
                  name: "taskhistory-contrast-872-zh-hant-\(appearance(dark))", into: directory)
        }
    }

    private static func use(_ language: LanguagePreference, _ model: AppModel) {
        model.settings.language = language
        model.l10n.apply(language)
    }

    private static func appearance(_ dark: Bool) -> String { dark ? "dark" : "light" }

    private static func write(_ input: TaskPerformanceInput, pair: TaskPerformancePair?, width: CGFloat, model: AppModel,
                              dark: Bool, contrast: Bool = false, name: String, into directory: URL) {
        let section = TaskPerformanceSection(input: input, pair: .constant(pair), l10n: model.l10n)
            .frame(width: width)
            .padding(HistoryLayout.outerPadding)
            .environment(\._colorSchemeContrast, contrast ? .increased : .standard)
        snapshot(section, dark: dark, to: directory.appendingPathComponent("\(name).png"))
    }

    /// `PreviewSnapshot.writeHosted` with the window resized to the content's fitting size again after the first layout
    /// settles, so text that wraps differently once laid out never leaves a band above or below the section.
    private static func snapshot<V: View>(_ view: V, dark: Bool, to url: URL) {
        let appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        let background = NSColor.windowBackgroundColor
        let host = NSHostingView(rootView: view
            .environment(\.colorScheme, dark ? .dark : .light)
            .background(Color(nsColor: background)))
        host.appearance = appearance
        host.frame = CGRect(origin: .zero, size: host.fittingSize)
        let window = NSWindow(contentRect: host.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.appearance = appearance
        window.backgroundColor = background
        window.contentView = host
        defer { PreviewSnapshot.release(window) }
        for _ in 0..<2 {
            host.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.08))
            let size = host.fittingSize
            if size != host.frame.size {
                window.setContentSize(size)
                host.frame = CGRect(origin: .zero, size: size)
            }
        }
        host.layoutSubtreeIfNeeded()
        host.displayIfNeeded()
        guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { return }
        host.cacheDisplay(in: host.bounds, to: rep)
        guard let image = rep.cgImage else { return }
        PreviewSnapshot.write(image, to: url)
    }
}
