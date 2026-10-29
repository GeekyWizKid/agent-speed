import SwiftUI

/// The score: the eyebrow when it applies, the number (34 pt, tabular) with `/100`, the proof mark under an unproven
/// number, the status line naming the check, and the composition bar.
struct TaskScoreCardScoreBlock: View {
    let score: TaskScoreCardScore
    let l10n: Localizer

    @Environment(\.taskReduceMotion) private var reduceMotion

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let eyebrow = TaskScoreCardText.eyebrow(score, l10n) {
                Text(eyebrow)
                    .font(TaskCardFont.eyebrow)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .padding(.bottom, 2)
                    .transition(.opacity)
            }
            HStack(alignment: .firstTextBaseline, spacing: 3) {
                TaskScoreNumber(value: score.shown, style: .countUp,
                                font: score.shown == nil ? TaskCardFont.numberAbsent : TaskCardFont.number)
                    .kerning(-0.6)
                    .foregroundStyle(numberStyle)
                    .proofMark(score.state.isUnproven && score.shown != nil, textSize: TaskCardFont.numberSize,
                               color: numberColor)
                    .animation(.easeOut(duration: TaskScoreMotion.formSeconds), value: score.form)
                Text(verbatim: "/100")
                    .font(TaskCardFont.outOf)
                    .foregroundStyle(.tertiary)
            }
            status
                .padding(.top, 6)
            TaskCompositionBar(segments: TaskScoreCardText.composition(score))
                .padding(.top, 10)
        }
    }

    private var numberStyle: AnyShapeStyle {
        if score.shown == nil { return AnyShapeStyle(.tertiary) }
        return score.form == .resting ? AnyShapeStyle(.secondary) : AnyShapeStyle(.primary)
    }

    private var numberColor: Color {
        Color(nsColor: score.form == .resting ? .secondaryLabelColor : .labelColor)
    }

    private var status: some View {
        let line = TaskScoreCardText.status(score, l10n)
        return line.segments.reduce(Text(verbatim: "")) { text, segment in
            switch segment {
            case .text(let words):
                text + Text(words)
            case .check(let suite):
                text + Text(suite).font(TaskCardFont.suite).foregroundStyle(.primary)
            }
        }
        .font(TaskCardFont.status)
        .foregroundStyle(.secondary)
        .lineLimit(2)
        .lineSpacing(1.5)
        .fixedSize(horizontal: false, vertical: true)
    }
}
