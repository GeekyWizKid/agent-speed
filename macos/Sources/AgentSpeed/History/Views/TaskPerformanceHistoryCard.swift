import AgentSpeedCore
import SwiftUI

/// 任務表現 in the Usage History window (DESIGN 12, 4b section 3c): the task records of the window's range, every
/// scored source or the one its source picker chose, compared by source and by model.
struct TaskPerformanceHistoryCard: View {
    let model: HistoryViewModel

    var body: some View {
        let range = model.content.range
        let first = range.start.description
        let last = range.end.description
        let store = model.store
        let comparison = TaskPerformanceComparison(store.taskComparison(first: first, last: last, source: model.source))
        let analyzing = if case .analyzing = store.state { true } else { false }
        TaskPerformanceSection(
            input: TaskPerformanceInput(comparison: comparison, narrowedTo: model.source, analyzing: analyzing),
            pair: Binding(get: { model.taskPair }, set: { model.setTaskPair($0) }),
            l10n: model.l10n,
            reduceMotion: model.accessibility.reduceMotion
        )
    }
}
