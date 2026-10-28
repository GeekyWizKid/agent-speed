import Foundation
import Testing
@testable import AgentSpeedCore

/// Run with `AGENT_SPEED_BENCH=1 swift test -c release -Xswiftc -enable-testing --filter TaskScorePerformance`.
@Suite("Task score performance", .enabled(if: ProcessInfo.processInfo.environment["AGENT_SPEED_BENCH"] == "1"))
struct TaskScorePerformanceTests {
    static func seconds(_ body: () -> Void) -> Double {
        let start = DispatchTime.now().uptimeNanoseconds
        body()
        return Double(DispatchTime.now().uptimeNanoseconds - start) / 1e9
    }

    /// One long task evaluated as the live engine would after each of its last 200 events.
    @Test func evaluatingALongTask() {
        var random = TaskScoreModelTests.Random(state: 3)
        let events = [TaskEvent.prompt(0, continuation: false)]
            + TaskScoreModelTests.stream(&random, count: 5000).filter { !$0.isPrompt }
        var score = 0.0
        let elapsed = Self.seconds {
            for end in (events.count - 200)..<events.count {
                score += TaskEvaluation(events[..<end], now: nil).score ?? 0
            }
        }
        print("task score: 200 evaluations of a \(events.count)-event task in \(elapsed) s (\(score))")
    }

    /// The records of a long stream of many tasks, snapshots and fix rows included.
    @Test func recordingAWholeStream() {
        var random = TaskScoreModelTests.Random(state: 9)
        let events = TaskScoreModelTests.stream(&random, count: 100_000)
        var records: [TaskRecord] = []
        let elapsed = Self.seconds {
            records = TaskRecord.records(events: events, analysisMs: 0, lastLineMs: 0, models: [], agentId: "a",
                                         source: .codex, day: { _ in "" })
        }
        let rows = records.reduce(0) { $0 + $1.snapshots.count + $1.fixRows.count }
        print("task score: \(records.count) records (\(rows) rows) from \(events.count) events in \(elapsed) s")
        let calibration = Self.seconds { _ = TaskModels.calibrate(records + records + records) }
        print("task score: calibration over \(records.count * 3) records in \(calibration) s")
    }
}
