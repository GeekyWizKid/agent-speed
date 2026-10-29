import Foundation

/// Result of a one-shot read of today's transcripts.
public struct TodayScanReport: Sendable {
    public var todayStartMs: Int64
    /// Responses ending today, sorted by end time then key.
    public var responses: [ResponseRecord]
    public var today: TokenTotals
    public var todayBySource: [SourceKind: TokenTotals]
    /// Today's unpriced facts per source (METRICS 9.10 *Live*); empty without `pricing`.
    public var todayUnpricedFacts: [SourceKind: [UnpricedFact]]
    /// Today's responses whose final usage was never written.
    public var unfinalized: Int
    public var seed: SeedSummary
    public var malformedLines: Int
    public var unreadableFiles: Int
    /// The `task` rows of the seeded agents of scored sources and the `taskSets` row (METRICS 6.9 *Dumps*), JSON
    /// lines; empty unless asked for.
    public var taskRows: [String] = []
    public var taskAgents = 0
    /// The instant the task values were evaluated at.
    public var taskNowMs: Int64 = 0
}

/// One-shot seeding without watching, used by diagnostics such as the probe.
public enum TranscriptScan {
    /// With `pricing`, the engine prices every response as the app does (METRICS 9.3, 9.7).
    public static func today(
        configuration: MonitorConfiguration = MonitorConfiguration(),
        now: Date = Date(),
        calendar: Calendar = .current,
        pricing: PriceBook? = nil,
        tasks: Bool = false,
        taskNowMs: Int64? = nil
    ) -> TodayScanReport {
        let nowMs = Clock.milliseconds(now)
        let todayStartMs = Clock.milliseconds(calendar.startOfDay(for: now))
        let session = TranscriptSession(configuration: configuration, pricing: pricing, taskMode: tasks ? .history : .live)
        let seed = session.seed(todayStartMs: todayStartMs)
        let responses = session.responses
            .filter { $0.endMs >= todayStartMs }
            .sorted { $0.endMs != $1.endMs ? $0.endMs < $1.endMs : $0.key.utf16.lexicographicallyPrecedes($1.key.utf16) }
        let output = session.advance(nowMs: nowMs, todayStartMs: todayStartMs, emitsEvents: false).output
        var taskRows: [String] = []
        var taskAgents = 0
        let evaluatedAt = taskNowMs ?? Clock.milliseconds(Date())
        if tasks {
            var agents = session.taskAgents()
            agents += openclawTaskAgents(configuration, since: todayStartMs - TranscriptSession.startupLookbackMs)
            taskAgents = agents.count
            taskRows = TaskDump.valueRows(agents, nowMs: evaluatedAt)
        }
        return TodayScanReport(
            todayStartMs: todayStartMs,
            responses: responses,
            today: output.today,
            todayBySource: output.todayBySource,
            todayUnpricedFacts: output.todayUnpricedFacts,
            unfinalized: output.unfinalizedToday,
            seed: seed,
            malformedLines: session.malformedLines,
            unreadableFiles: session.unreadableFileCount,
            taskRows: taskRows,
            taskAgents: taskAgents,
            taskNowMs: evaluatedAt
        )
    }

    /// Every window of the OpenClaw databases changed since `since`, read whole like `reference.py`'s seeding.
    private static func openclawTaskAgents(_ configuration: MonitorConfiguration, since: Int64) -> [TaskValueReport.Agent] {
        guard configuration.readsOpenclaw else { return [] }
        var deduper = Deduper(capacity: 1_000_000_000)
        var nextFileID: FileID = 0
        var agents: [TaskValueReport.Agent] = []
        let paths = OpenClawSource.startupOrder(configuration.resolvedOpenclawRoots.flatMap(OpenClawHome.databases(in:)))
        for path in paths {
            guard let stat = FileStat.lstat(path), stat.modifiedMs >= since else { continue }
            let windows = OpenClawTaskWindows.read(path: path, deduper: &deduper) { _ in
                defer { nextFileID &+= 1 }
                return nextFileID
            } ?? []
            for window in windows {
                let state = window.parser.state
                agents.append(TaskValueReport.Agent(id: window.agentId, source: .openclaw, phase: state.phase,
                                                    lastLineMs: state.lastLineMs, parentId: window.parentId,
                                                    events: window.tasks.events))
            }
        }
        return agents
    }
}

enum Clock {
    static func milliseconds(_ date: Date) -> Int64 {
        Int64((date.timeIntervalSince1970 * 1000).rounded(.down))
    }

    /// Milliseconds that keep counting while the Mac sleeps (live reporting's clock, METRICS 11.9).
    static func monotonicMs() -> Int64 {
        Int64(clock_gettime_nsec_np(CLOCK_MONOTONIC) / 1_000_000)
    }
}
