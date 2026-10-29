import Foundation

/// One task of an agent's stream (METRICS 6.9 *Tasks*): its events, contiguous in the stream.
public struct TaskSpan: Equatable, Sendable {
    /// The task's first prompt, or its first event for the task before the first prompt.
    public var startMs: Int64
    public var events: ArraySlice<TaskEvent>

    public init(startMs: Int64, events: ArraySlice<TaskEvent>) {
        self.startMs = startMs
        self.events = events
    }
}

/// `split_tasks`, `current_task` and `task_end` of `task_score.py`.
public enum TaskSplit {
    /// A prompt starts a task, a continuation joins the current one, and events before the first prompt form a task
    /// of their own.
    public static func split(_ events: [TaskEvent]) -> [TaskSpan] {
        var spans: [TaskSpan] = []
        var start: Int?
        for index in events.indices {
            guard case .prompt(let continuation) = events[index].body, !(continuation && start != nil) else {
                if start == nil { start = index }
                continue
            }
            if let start { spans.append(TaskSpan(startMs: events[start].t, events: events[start..<index])) }
            start = index
        }
        if let start { spans.append(TaskSpan(startMs: events[start].t, events: events[start...])) }
        return spans
    }

    /// The latest task among the events with `t ≤ now` (all of them when `now` is nil).
    public static func current(_ events: [TaskEvent], now: Int64?) -> TaskSpan? {
        func visible(_ event: TaskEvent) -> Bool { now.map { event.t <= $0 } ?? true }
        var start: Int?
        var first: Int?
        for index in events.indices.reversed() where visible(events[index]) {
            first = index
            if case .prompt(continuation: false) = events[index].body {
                start = index
                break
            }
        }
        guard let begin = start ?? first else { return nil }
        let tail = events[begin...]
        if now == nil || tail.allSatisfy(visible) { return TaskSpan(startMs: events[begin].t, events: tail) }
        return TaskSpan(startMs: events[begin].t, events: ArraySlice(tail.filter(visible)))
    }

    /// The latest `t` of the task's events and of their outcomes.
    public static func end(_ span: TaskSpan) -> Int64 {
        var end = span.startMs
        for event in span.events {
            end = max(end, event.t)
            if let t = event.action?.outcome?.t { end = max(end, t) }
        }
        return end
    }
}
