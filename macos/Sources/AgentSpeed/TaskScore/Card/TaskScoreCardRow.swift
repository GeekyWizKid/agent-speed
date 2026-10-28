import SwiftUI

/// Makes a SwiftUI row the card's owner (DESIGN 12, *Score card*): hover shows it after the presenter's delay, Space
/// or the custom action `a11y.task.card` shows it at once, it follows the focus while shown, Esc hides it. The card's
/// content is the row's extra accessibility content (VoiceOver's "more content"), in the card's order.
private struct TaskScoreCardRowModifier: ViewModifier {
    let presenter: TaskScoreCardPresenter
    let key: String
    let lines: [String]
    let focused: Bool
    let l10n: Localizer
    let provider: TaskScoreCardPresenter.Provider

    func body(content: Content) -> some View {
        lines.enumerated().reduce(AnyView(content)) { view, entry in
            AnyView(view.accessibilityCustomContent(
                AccessibilityCustomContentKey(Text(l10n("a11y.task.card")), id: "task.card.\(entry.offset)"),
                Text(entry.element),
                importance: entry.offset == 0 ? .high : .default
            ))
        }
        .onHover { inside in
            if inside { presenter.enter(key, provider: provider) } else { presenter.exit(key) }
        }
        .onKeyPress(.space) {
            guard focused else { return .ignored }
            presenter.showNow(key, provider: provider)
            return .handled
        }
        .onKeyPress(.escape) {
            guard focused, presenter.isShown else { return .ignored }
            presenter.dismiss()
            return .handled
        }
        .onChange(of: focused) { _, now in
            if now { presenter.focus(key, provider: provider) }
        }
        .accessibilityAction(named: Text(l10n("a11y.task.card"))) {
            presenter.showNow(key, provider: provider)
        }
    }
}

extension View {
    /// The score card of this row; `lines` from `TaskScoreCardText.accessibilityLines`.
    func taskScoreCard(_ presenter: TaskScoreCardPresenter, key: String, lines: [String], focused: Bool, l10n: Localizer,
                       provider: @escaping TaskScoreCardPresenter.Provider) -> some View {
        modifier(TaskScoreCardRowModifier(presenter: presenter, key: key, lines: lines, focused: focused, l10n: l10n,
                                          provider: provider))
    }
}
