import AgentSpeedCore
import SwiftUI

/// The card's header: a 7 pt source dot, the project (13 pt semibold, tail truncation) and the state chip; line 2
/// is model · source (a group row adds its sub-agents) in 11 pt secondary.
struct TaskScoreCardHeader: View {
    let header: TaskScoreCardModel.Header
    /// Nil for a source without scores.
    let state: TaskScoreState?
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let source = l10n(header.source.titleKey)
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .center, spacing: 7) {
                Circle()
                    .fill(header.source.tint(dark: colorScheme == .dark))
                    .frame(width: TaskCardLayout.dotSize, height: TaskCardLayout.dotSize)
                Text(header.project.isEmpty ? source : header.project)
                    .font(TaskCardFont.project)
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if let state {
                    TaskStateChip(state: state, title: l10n(state.titleKey))
                        .padding(.leading, 4)
                }
            }
            .frame(minHeight: TaskStateChip.height)
            // A long model id gives way (middle truncation, as model ids are cut everywhere); the source and the
            // sub-agent count stay whole.
            HStack(spacing: 0) {
                if let model = header.model, !model.isEmpty {
                    Text(model)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Text(verbatim: " · ")
                        .fixedSize()
                }
                Text(trailingMeta(source))
                    .lineLimit(1)
                    .fixedSize()
            }
            .font(TaskCardFont.meta)
            .foregroundStyle(.secondary)
            .padding(.leading, TaskCardLayout.headerIndent)
        }
    }

    private func trailingMeta(_ source: String) -> String {
        header.subAgents > 0 ? source + " · " + l10n("boards.subCount", header.subAgents) : source
    }
}
