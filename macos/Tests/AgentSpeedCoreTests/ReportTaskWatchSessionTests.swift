import Foundation
import Testing
@testable import AgentSpeedCore

/// The monitor's side of METRICS 11.10: tasks are marked watched at their first event while the report watch is on,
/// complete when a later task settles or after 30 quiet minutes, and reach the snapshot's completion log once, under the
/// resolved id of their model and their agent's source. History (seeded, older than 2 minutes, before the watch) is
/// never counted, and a break unwatches everything.
@Suite("Task score watch in the monitor")
struct ReportTaskWatchSessionTests {
    typealias Lines = TaskSessionTests

    struct Fixture {
        let directory: TempDirectory
        let session: TranscriptSession
        let now: Int64
        let today: Int64

        init(seeded: [String] = []) throws {
            directory = try TempDirectory()
            now = Clock.milliseconds(Date()) / 1000 * 1000
            today = SessionTests.startOfToday()
            try directory.write(Lines.relative, seeded.isEmpty ? "" : Lines.text(seeded))
            let configuration = MonitorConfiguration(enabledSources: [.claudeCode],
                                                     claudeRoot: directory.file("claude/projects"))
            session = TranscriptSession(configuration: configuration)
            _ = session.seed(todayStartMs: today)
        }

        func append(_ lines: [String]) throws {
            try directory.append(Lines.relative, Lines.text(lines))
            _ = session.rediscover(todayStartMs: today)
        }

        func completions(at nowMs: Int64? = nil) -> [ReportTaskCompletion] {
            session.advance(nowMs: nowMs ?? now, todayStartMs: today, emitsEvents: false).output.report.task.completions.entries
        }
    }

    /// A task with an edit and a passing test.
    static func task(_ at: Int64, _ id: String) -> [String] {
        [Lines.prompt(at, "task \(id)")]
            + Lines.call("e\(id)", at + 1000, tool: "Edit",
                         input: #"{"file_path":"/w/atlas/\#(id).rs","old_string":"a","new_string":"b"}"#, result: "ok")
            + Lines.call("b\(id)", at + 3000, tool: "Bash", input: #"{"command":"cargo test"}"#,
                         result: "test result: ok. 12 passed; 0 failed")
    }

    static let opus = PriceBook.bundled.reportModelId(model: "claude-opus-5-5", provider: nil)

    @Test func aWatchedTaskCompletesOnceWhenTheNextPromptSettles() throws {
        let fixture = try Fixture()
        fixture.session.setReportWatch(3)
        try fixture.append(Self.task(fixture.now - 20_000, "a"))
        try fixture.append([Lines.prompt(fixture.now - 5000, "next")])
        #expect(fixture.completions().isEmpty, "the next prompt can still be withdrawn")
        try fixture.append(Lines.call("x1", fixture.now - 4000, tool: "Read", input: #"{"file_path":"/w/atlas/x.rs"}"#,
                                      result: "fn x() {}"))
        let entries = fixture.completions()
        #expect(entries.count == 1)
        let entry = try #require(entries.first)
        #expect(entry.epoch == 3 && entry.modelId == Self.opus && entry.source == .claudeCode)
        #expect(entry.row.completed == 1 && entry.row.scored == 1 && entry.row.successes == 1)
        #expect(entry.row.scoreBins.reduce(0, +) == 1 && entry.row.scoreSum >= 0)
        try fixture.append(Lines.call("x2", fixture.now - 3000, tool: "Read", input: #"{"file_path":"/w/atlas/y.rs"}"#,
                                      result: "fn y() {}"))
        #expect(fixture.completions().count == 1, "counted once")
    }

    @Test func historyIsNeverCounted() throws {
        let fixture = try Fixture(seeded: Self.task(Clock.milliseconds(Date()) - 30_000, "seeded"))
        fixture.session.setReportWatch(1)
        // The seeded task completes when this one settles: it was read while seeding.
        try fixture.append(Self.task(fixture.now - 10_000, "b"))
        #expect(fixture.completions().isEmpty)

        // A session found late: its first task began more than 2 minutes before it was read.
        let late = "claude/projects/-w-atlas/s-late.jsonl"
        try fixture.directory.write(late, Lines.text(Self.task(fixture.now - 200_000, "late")
            + [Lines.prompt(fixture.now - 2000, "then")]
            + Lines.call("l1", fixture.now - 1000, tool: "Read", input: #"{"file_path":"/w/atlas/l.rs"}"#, result: "x")))
        _ = fixture.session.rediscover(todayStartMs: fixture.today)
        #expect(fixture.completions().isEmpty)
    }

    @Test func aBreakUnwatchesEveryTask() throws {
        let fixture = try Fixture()
        fixture.session.setReportWatch(1)
        try fixture.append(Self.task(fixture.now - 20_000, "a"))
        fixture.session.setReportWatch(nil)
        fixture.session.setReportWatch(2)
        try fixture.append(Self.task(fixture.now - 8000, "b"))
        #expect(fixture.completions().isEmpty, "task a started before the new watch")
        try fixture.append(Self.task(fixture.now - 2000, "c"))
        let entries = fixture.completions()
        #expect(entries.map(\.epoch) == [2], "task b, watched in the new epoch")
    }

    @Test func aQuietAgentCompletesItsTaskAfterThirtyMinutes() throws {
        let fixture = try Fixture()
        fixture.session.setReportWatch(1)
        try fixture.append(Self.task(fixture.now - 20_000, "a"))
        let listed = fixture.session.advance(nowMs: fixture.now, todayStartMs: fixture.today, emitsEvents: false).output
        let lastLine = try #require(listed.agents.first?.agent.lastLineMs)
        #expect(fixture.completions(at: lastLine + 1_799_999).isEmpty)
        #expect(fixture.completions(at: lastLine + 1_800_000).count == 1)
        #expect(fixture.completions(at: lastLine + 1_900_000).count == 1, "once")
    }

    @Test func aTaskWithoutActionsAddsNothing() throws {
        let fixture = try Fixture()
        fixture.session.setReportWatch(1)
        try fixture.append([Lines.prompt(fixture.now - 20_000, "just a question")])
        try fixture.append(Self.task(fixture.now - 10_000, "b"))
        #expect(fixture.completions().isEmpty)
    }
}
