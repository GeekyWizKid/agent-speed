import Foundation

/// What the monitor needs to mark a task watched (METRICS 11.10 *Watch*): the current watch's epoch and the time of
/// the read. nil while the watch is off or seeding runs.
public struct ReportTaskWatchContext: Equatable, Sendable {
    public var epoch: Int
    public var nowMs: Int64

    public init(epoch: Int, nowMs: Int64) {
        self.epoch = epoch
        self.nowMs = nowMs
    }
}

/// The watched tasks of one agent's stream (METRICS 11.10 *Watch*), by a key of each task, with the epoch it was
/// watched in. A task is marked only when its first event is read; a mark is never set later.
public struct ReportTaskWatchMarks<Key: Hashable & Sendable>: Equatable, Sendable {
    public private(set) var marks: [Key: Int] = [:]

    public init() {}

    public var isEmpty: Bool { marks.isEmpty }

    /// The first event of a task was read: watched while the watch is on and the event is at most 2 minutes old.
    public mutating func started(_ key: Key, firstEventMs t: Int64, context: ReportTaskWatchContext?) {
        guard let context, ReportTaskScoreRule.watches(firstEventMs: t, nowMs: context.nowMs) else { return }
        marks[key] = context.epoch
    }

    /// A withdrawn prompt, or a task whose agent went away: never counted.
    public mutating func unwatch(_ key: Key) {
        marks[key] = nil
    }

    /// The task completed: the epoch it was watched in, or nil when it was history. It is unwatched either way, so it
    /// is never counted again.
    public mutating func complete(_ key: Key) -> Int? {
        marks.removeValue(forKey: key)
    }

    /// A break: every task is unwatched.
    public mutating func clear() {
        marks.removeAll()
    }

    /// The keys moved (a withdrawn event before them): `transform` gives each one's new key, nil to drop it.
    public mutating func rekey(_ transform: (Key) -> Key?) {
        var moved: [Key: Int] = [:]
        for (key, epoch) in marks {
            if let new = transform(key) { moved[new] = epoch }
        }
        marks = moved
    }
}

/// The reporting side of METRICS 11.10: the watch, as an epoch the monitor marks tasks with; the completions taken from
/// the engine's log once each; and the task ledger, which only a build takes and only a break empties.
public struct ReportTaskTracker: Equatable, Sendable {
    public private(set) var ledger = ReportTaskLedger()
    /// The current watch's epoch; nil while the watch is off.
    public private(set) var epoch: Int?
    private var lastEpoch = 0
    /// What the watch is on for (the service); a change is a break followed by a new watch.
    private var watchKey: String?
    private var logId: UInt64?
    private var lastIndex = 0

    public init() {}

    /// The watch at a tick: `key` names the service it is on for, nil while it is off. Turning off, or on for another
    /// service, empties the ledger (a break). Returns true when the epoch changed, which the monitor must learn.
    @discardableResult
    public mutating func watch(_ key: String?) -> Bool {
        guard key != watchKey else { return false }
        if watchKey != nil { ledger.clear() }
        watchKey = key
        if key != nil {
            lastEpoch += 1
            epoch = lastEpoch
        } else {
            epoch = nil
        }
        return true
    }

    /// Takes the completions the log gained since the last call, those of the current watch only; true when the
    /// ledger received one (the changed rule of METRICS 11.10 *WebSocket*).
    @discardableResult
    public mutating func ingest(_ log: ReportTaskCompletionLog) -> Bool {
        if log.id != logId {
            logId = log.id
            lastIndex = 0
        }
        var received = false
        for entry in log.entries(after: lastIndex) {
            lastIndex = entry.index
            guard let epoch, entry.epoch == epoch else { continue }
            if ledger.add(entry.row, model: entry.modelId, source: entry.source) { received = true }
        }
        return received
    }

    /// A build with the extension active: the rows, and an empty ledger.
    public mutating func take() -> ReportTaskStats {
        ledger.take()
    }

    /// The rows the next build takes (the preview shows them without taking them).
    public var pending: ReportTaskStats { ledger.rows }
}
