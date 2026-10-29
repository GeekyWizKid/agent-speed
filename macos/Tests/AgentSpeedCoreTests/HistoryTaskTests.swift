import Foundation
import Testing
@testable import AgentSpeedCore

/// METRICS 6.11 *Store* and 8: the history analysis writes the task records of every scored file into `task_records`
/// (analysis version 8), they read back exactly as the end-to-end fixture states them, the calibration and the
/// comparison computed from the store match it too, and a file whose last task was open is analysed again once it
/// has been quiet for 30 minutes.
@Suite("History task records")
struct HistoryTaskTests {
    static let utc = TimeZone(identifier: "UTC")!

    /// The fixture tree copied into a scratch folder, its files' modification times rising in the fixture's order so
    /// the startup order (and so key ownership) is the fixture's.
    struct Corpus {
        let directory: TempDirectory
        let configuration: MonitorConfiguration
        let storePath: String
        let fixture: JSONValue

        init() throws {
            fixture = TaskEndToEndTests.fixture
            directory = try TempDirectory()
            let source = Fixtures.root.appendingPathComponent("tasks")
            try FileManager.default.copyItem(at: source, to: directory.file("tasks"))
            let base = Date(timeIntervalSince1970: 1_793_000_000)
            for (index, item) in (fixture["order"]?.array ?? []).enumerated() {
                let path = directory.file(item["path"]?.string ?? "").path
                try FileManager.default.setAttributes([.modificationDate: base.addingTimeInterval(Double(index))],
                                                      ofItemAtPath: path)
            }
            configuration = MonitorConfiguration(
                enabledSources: TaskScoring.sources,
                codexRoot: directory.file("tasks/codex/sessions"),
                claudeRoot: directory.file("tasks/claude/projects"),
                ompRoot: directory.file("tasks/omp/agent/sessions"),
                piRoot: directory.file("tasks/pi/agent/sessions"),
                openclawRoot: directory.file("tasks/openclaw/.openclaw"),
                droidRoot: directory.file("tasks/droid/.factory")
            )
            storePath = directory.file("store/history.sqlite").path
        }

        func analyzer() throws -> HistoryAnalyzer {
            try HistoryAnalyzer(database: HistoryDatabase(path: storePath))
        }

        func run(_ analyzer: HistoryAnalyzer, nowMs: Int64) throws -> HistoryAnalyzer.Report {
            var options = HistoryAnalyzer.Options(timeZone: HistoryTaskTests.utc)
            options.nowMs = nowMs
            return try analyzer.run(configuration: configuration, options: options)
        }

        func label(_ path: String) -> String {
            let root = directory.url.resolvingSymlinksInPath().path + "/"
            let resolved = URL(fileURLWithPath: path).resolvingSymlinksInPath().path
            for prefix in [directory.path + "/", root] where path.hasPrefix(prefix) || resolved.hasPrefix(prefix) {
                return String((path.hasPrefix(prefix) ? path : resolved).dropFirst(prefix.count))
            }
            return path
        }
    }

    @Test func storedRecordsCalibrationAndComparisonMatchTheFixture() throws {
        let corpus = try Corpus()
        let analysisMs = try #require(corpus.fixture["analysisMs"]?.int64)
        let analyzer = try corpus.analyzer()
        let report = try corpus.run(analyzer, nowMs: analysisMs)
        #expect(report.filesAnalyzed == 14 && report.sessionsAnalyzed >= 2)

        let database = try HistoryDatabase(path: corpus.storePath)
        let stored = try database.taskRecords(rows: true)
            .sorted { $0.agentId == $1.agentId ? $0.ordinal < $1.ordinal
                : AgentParents.ordinal(corpus.label($0.agentId), precedes: corpus.label($1.agentId)) }
        var comparison = TaskTreeComparison()
        let rows = stored.map { TaskEndToEndTests.parse(TaskDumpJSON.record($0, agentId: corpus.label($0.agentId))) }
        comparison.compare(.array(rows), corpus.fixture["records"] ?? .null, "records")

        var calibration: [String: JSONValue] = [:]
        for (source, models) in try database.taskCalibration() {
            calibration[source.rawValue] = TaskEndToEndTests.parse(TaskDumpJSON.models(models))
        }
        var loose = TaskTreeComparison()
        loose.tolerance = 1e-6
        loose.compare(.object(calibration), corpus.fixture["calibration"] ?? .null, "calibration")
        comparison.absorb(loose)

        let range = corpus.fixture["comparisonRange"]
        let compared = TaskComparison.compare(try analyzer.comparisonRecords(sources: TaskScoring.sources),
                                              first: range?["first"]?.string ?? "", last: range?["last"]?.string ?? "",
                                              source: nil)
        let (bySource, byModel) = TaskDumpJSON.comparison(compared)
        comparison.compare(.object(["bySource": TaskEndToEndTests.parse(bySource),
                                    "byModel": TaskEndToEndTests.parse(byModel)]),
                           corpus.fixture["comparison"] ?? .null, "comparison")
        #expect(comparison.mismatches.isEmpty, Comment(rawValue: comparison.mismatches.prefix(40).joined(separator: "\n")))
        print("history task records: \(stored.count) records, \(comparison.numbers) numbers, "
              + "\(comparison.mismatches.count) differences")

        // Unchanged files are read again only when they hold an open task and have been quiet for 30 minutes, once:
        // the transcripts' modification times are far in the past here; the OpenClaw windows' activity is recent.
        let open = Set(stored.filter { !$0.complete && !$0.agentId.contains("#") }.map(\.agentId)).count
        let again = try corpus.run(analyzer, nowMs: analysisMs + 60_000)
        #expect(again.filesAnalyzed == open && again.sessionsAnalyzed == 0)
        let third = try corpus.run(analyzer, nowMs: analysisMs + 120_000)
        #expect(third.filesAnalyzed + third.sessionsAnalyzed == 0)
    }

    @Test func aFileWithAnOpenTaskIsAnalysedAgainOnceItWasQuietFor30Minutes() throws {
        let directory = try TempDirectory()
        let lastLine = Date(timeIntervalSince1970: 1_793_000_000)
        try directory.write("claude/p/a.jsonl", Self.claudeTask(at: lastLine), modified: lastLine)
        let configuration = MonitorConfiguration(enabledSources: [.claudeCode], claudeRoot: directory.file("claude"))
        let storePath = directory.file("store/history.sqlite").path
        let analyzer = try HistoryAnalyzer(database: HistoryDatabase(path: storePath))
        func run(after minutes: Int64) throws -> HistoryAnalyzer.Report {
            var options = HistoryAnalyzer.Options(timeZone: Self.utc)
            options.nowMs = Clock.milliseconds(lastLine) + minutes * 60_000
            return try analyzer.run(configuration: configuration, options: options)
        }
        func complete() throws -> [Bool] {
            try HistoryDatabase(path: storePath).taskRecords(rows: false).map(\.complete)
        }

        #expect(try run(after: 5).filesAnalyzed == 1)
        #expect(try complete() == [false], "the last task is still open five minutes after the last line")
        #expect(try run(after: 20).filesAnalyzed == 0, "unchanged and not quiet for 30 minutes yet")
        #expect(try run(after: 31).filesAnalyzed == 1, "quiet for 30 minutes: analysed again")
        #expect(try complete() == [true])
        #expect(try run(after: 45).filesAnalyzed == 0)
        // A reopened store (a relaunch) knows the file's records are complete.
        let reopened = try HistoryDatabase(path: storePath).loadEntries()
        #expect(reopened.values.allSatisfy { !$0.hasOpenTask })
    }

    @Test func recordsOfAnotherAnalysisVersionAreNotRead() throws {
        let directory = try TempDirectory()
        let lastLine = Date(timeIntervalSince1970: 1_793_000_000)
        try directory.write("claude/p/a.jsonl", Self.claudeTask(at: lastLine), modified: lastLine)
        let configuration = MonitorConfiguration(enabledSources: [.claudeCode], claudeRoot: directory.file("claude"))
        let storePath = directory.file("store/history.sqlite").path
        var options = HistoryAnalyzer.Options(timeZone: Self.utc)
        options.nowMs = Clock.milliseconds(lastLine) + 3_600_000
        _ = try HistoryAnalyzer(database: HistoryDatabase(path: storePath)).run(configuration: configuration, options: options)
        #expect(try HistoryDatabase(path: storePath).taskRecords(rows: false).count == 1)
        let connection = try SQLiteConnection(path: storePath, mode: .readWriteCreate)
        try connection.execute("UPDATE files SET version = 7")
        connection.close()
        #expect(try HistoryDatabase(path: storePath).taskRecords(rows: false).isEmpty,
                "an older build analysed the file again: its rows are stale")
    }

    /// A prompt, an edit and a passing test run, the last line at `end`.
    static func claudeTask(at end: Date) -> String {
        func ts(_ seconds: Double) -> String {
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            return formatter.string(from: end.addingTimeInterval(seconds))
        }
        return [
            #"{"type":"user","timestamp":"\#(ts(-60))","cwd":"/w/p","message":{"role":"user","content":"fix the parser"}}"#,
            #"{"type":"assistant","timestamp":"\#(ts(-50))","message":{"id":"m1","model":"claude-opus-5-5","stop_reason":"tool_use","content":[{"type":"tool_use","id":"t1","name":"Edit","input":{"file_path":"/w/p/a.rs","old_string":"a","new_string":"b"}}],"usage":{"input_tokens":10,"output_tokens":5}}}"#,
            #"{"type":"user","timestamp":"\#(ts(-40))","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"ok"}]}}"#,
            #"{"type":"assistant","timestamp":"\#(ts(-30))","message":{"id":"m2","model":"claude-opus-5-5","stop_reason":"tool_use","content":[{"type":"tool_use","id":"t2","name":"Bash","input":{"command":"cargo test"}}],"usage":{"input_tokens":10,"output_tokens":5}}}"#,
            #"{"type":"user","timestamp":"\#(ts(0))","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t2","content":"test result: ok. 3 passed; 0 failed","is_error":false}]}}"#,
        ].joined(separator: "\n") + "\n"
    }
}
