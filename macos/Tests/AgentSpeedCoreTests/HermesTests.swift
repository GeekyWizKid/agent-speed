import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Hermes")
struct HermesTests {
    private static let dayStart: Int64 = 1_792_454_400_000
    private static let end: Int64 = 1_792_497_604_000
    private static let utc = TimeZone(secondsFromGMT: 0)!

    @Test func sharedFixturesAndLocalTimestamps() throws {
        let expected = try Fixtures.json("hermes/expected.json")
        let parser = HermesLine(timeZone: Self.utc)
        var seen: Set<String> = []
        for file in try #require(expected["files"] as? [[String: Any]]) {
            let path = Fixtures.root.appendingPathComponent(try #require(file.string("path")))
            let directory = path.deletingLastPathComponent().path
            var records: [ResponseRecord] = []
            forEachLine(in: try Data(contentsOf: path)) { bytes in
                guard let record = parser.record(bytes, logDirectory: directory), seen.insert(record.key).inserted else { return }
                records.append(record)
            }
            let wanted = try #require(file["responses"] as? [[String: Any]]).map { row in
                var row = row
                row["key"] = row.string("key")?.replacingOccurrences(of: "@LOG_ROOT@", with: directory)
                return row
            }
            try HarnessFixtureTests.compare(records, wanted)
        }
        for row in try #require(expected["lineCases"] as? [[String: Any]]) {
            var line = try #require(row.string("line"))
            if let length = row.int64("padToBytes") { line += String(repeating: " ", count: max(0, Int(length) - line.utf8.count)) }
            let record = Data(line.utf8).withUnsafeBytes { parser.record($0, logDirectory: "fixture-root") }
            if let wanted = row["response"] as? [String: Any] {
                try HarnessFixtureTests.compare([try #require(record)], [wanted])
            } else {
                #expect(record == nil)
            }
        }
        for row in try #require(expected["timeZones"] as? [[String: Any]]) {
            let zoneName = try #require(row.string("zone"))
            let zone = try #require(TimeZone(identifier: zoneName))
            let parser = HermesLine(timeZone: zone)
            let line = try #require(row.string("line"))
            let record = Data(line.utf8).withUnsafeBytes { parser.record($0, logDirectory: "/logs") }
            #expect(record?.endMs == row.int64("endMs"))
        }
    }

    @Test func countsAreBoundedAndUnknownModelsRemainUnknown() throws {
        let parser = HermesLine(timeZone: Self.utc)
        let huge = String(repeating: "9", count: 40)
        let line = "2026-10-20 12:00:04,000 INFO[s] agent.turn_usage: API call #1: model=unknown provider=unknown in=\(huge) out=\(huge) total=\(huge) latency=0.0s cache=\(huge)/10 (100%) write=\(huge)"
        let record = try #require(Data(line.utf8).withUnsafeBytes { parser.record($0, logDirectory: "/logs") })
        #expect(record.model == nil && record.provider == nil && record.startMs == nil)
        #expect(record.usage.input == Int(TokenCount.ceiling) && record.usage.output == Int(TokenCount.ceiling))
        #expect(record.usage.cachedInput == record.usage.input && record.usage.cacheWriteInput == record.usage.input)
    }

    @Test func sourceDiscoversProfilesAndSeparatesSessions() throws {
        let root = Fixtures.root.appendingPathComponent("hermes")
        let source = HermesSource(root: root.path, fileID: -3, timeZone: Self.utc)
        #expect(source.refreshAvailability())
        var deduper = Deduper()
        let update = source.seed(todayStartMs: Self.dayStart, deduper: &deduper)
        let expected = try Fixtures.json("hermes/expected.json")
        let totals = try #require(expected["totals"] as? [String: Any])
        #expect(update.upserts.count == totals.int("requests"))
        #expect(update.agents.count >= 6)
        #expect(source.activeSessionCount(todayStartMs: Self.dayStart) == update.agents.count)
        #expect(source.refresh(deduper: &deduper).isEmpty)
        #expect(HermesSource.files(root: root.path).count == 3)
        #expect(HermesSource.files(root: root.appendingPathComponent("logs").path).count == 2)
        #expect(SourceSet(.hermes).rawValue == 1 << 13)
    }

    @Test func discoveryDoesNotFollowChildSymlinks() throws {
        let directory = try TempDirectory(), outside = try TempDirectory()
        try outside.write("logs/agent.log", Self.line(session: "outside", call: 1, end: Self.end, output: 200))
        try FileManager.default.createSymbolicLink(at: directory.file("logs"), withDestinationURL: outside.file("logs"))
        try FileManager.default.createDirectory(at: directory.file("profiles/linked-logs"), withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(at: directory.file("profiles/linked-logs/logs"), withDestinationURL: outside.file("logs"))
        try FileManager.default.createSymbolicLink(at: directory.file("profiles/linked-profile"), withDestinationURL: outside.url)
        #expect(HermesSource.files(root: directory.path).isEmpty)
        try FileManager.default.removeItem(at: directory.file("profiles"))
        try FileManager.default.createSymbolicLink(at: directory.file("profiles"), withDestinationURL: outside.url)
        #expect(HermesSource.files(root: directory.path).isEmpty)
    }

    @Test func completeLinesAndRotationProduceExactlyOneCompletion() throws {
        let directory = try TempDirectory()
        let configuration = MonitorConfiguration(enabledSources: [.hermes], hermesRoot: directory.url)
        let session = TranscriptSession(configuration: configuration, clock: { Self.end + 4_000 })
        #expect(!session.hasAvailableRoot)
        let first = Self.line(session: "a", call: 1, end: Self.end, output: 200)
        try directory.write("logs/agent.log", first)
        #expect(session.rediscover(todayStartMs: Self.dayStart) && session.hasAvailableRoot)
        #expect(session.responses.count == 1)
        _ = session.advance(nowMs: Self.end, todayStartMs: Self.dayStart, emitsEvents: false)
        let second = Self.line(session: "b", call: 1, end: Self.end + 4_000, output: 80)
        try directory.append("logs/agent.log", String(second.dropLast()))
        #expect(!session.pollRecentFiles(nowMs: Self.end + 4_000))
        try directory.append("logs/agent.log", "\n")
        #expect(session.pollRecentFiles(nowMs: Self.end + 4_000))
        let output = session.advance(nowMs: Self.end + 4_000, todayStartMs: Self.dayStart, emitsEvents: true)
        #expect(output.output.today.requests == 2 && output.output.today.output == 280)
        #expect(output.events.filter { if case .responseCompleted = $0 { true } else { false } }.count == 1)
        try FileManager.default.moveItem(at: directory.file("logs/agent.log"), to: directory.file("logs/agent.log.1"))
        try directory.write("logs/agent.log", second + Self.line(session: "a", call: 1, end: Self.end + 8_000, output: 40))
        #expect(session.pollRecentFiles(nowMs: Self.end + 8_000))
        #expect(session.responses.count == 3)
        let context = SnapshotContext(session: session, todayStartMs: Self.dayStart)
        #expect(context.sources[.hermes]?.available == true && context.sources[.hermes]?.sessionCount == 2)
        var moved = configuration
        moved.hermesRoot = directory.file("other")
        #expect(moved.requiresRescan(comparedTo: configuration))
    }

    @Test func historyAndLiveAgreeAndExpiredRotationsKeepHistory() throws {
        let directory = try TempDirectory()
        try directory.write("logs/agent.log", Self.line(session: "a", call: 1, end: Self.end, output: 200)
                            + Self.line(session: "b", call: 1, end: Self.end, output: 80))
        let configuration = MonitorConfiguration(enabledSources: [.hermes], hermesRoot: directory.url)
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: Self.dayStart)
        let live = session.advance(nowMs: Self.end + 500, todayStartMs: Self.dayStart, emitsEvents: false).output
        let store = directory.file("history.sqlite").path
        let first = try HistoryScan.run(configuration: configuration, timeZone: Self.utc, storePath: store)
        let day = try #require(first.days.first)
        #expect(day.totals == live.today && day.measuredMs == 8_000 && day.agents == 2)
        #expect(day.totals.cachedInput == 1000 && day.totals.cacheWriteInput == 200)
        let unchanged = try HistoryScan.run(configuration: configuration, timeZone: Self.utc, storePath: store)
        #expect(unchanged.filesAnalyzed == 0 && unchanged.days == first.days)
        try FileManager.default.moveItem(at: directory.file("logs/agent.log"), to: directory.file("logs/agent.log.1"))
        try directory.write("logs/agent.log", Self.line(session: "a", call: 1, end: Self.end + 8_000, output: 40))
        let rotated = try HistoryScan.run(configuration: configuration, timeZone: Self.utc, storePath: store)
        #expect(rotated.days.first?.totals.requests == 3 && rotated.days.first?.totals.output == 320 && rotated.days.first?.agents == 2)
        try FileManager.default.removeItem(at: directory.file("logs/agent.log.1"))
        let expired = try HistoryScan.run(configuration: configuration, timeZone: Self.utc, storePath: store)
        #expect(expired.days == rotated.days)
        var reports = ReportLedger()
        for response in session.responses { reports.count(response, modelId: "known") }
        #expect(reports.counters.total.output == 280 && reports.counters.byModel[ReportModelKey(modelId: "known", source: .hermes)]?.output == 280)
    }

    @Test func historyDefersRotationBetweenPlanningAndReading() throws {
        let directory = try TempDirectory()
        try directory.write("logs/agent.log", Self.line(session: "a", call: 1, end: Self.end, output: 200))
        let configuration = MonitorConfiguration(enabledSources: [.hermes], hermesRoot: directory.url)
        let database = try HistoryDatabase.inMemory()
        let analyzer = try HistoryAnalyzer(database: database)
        let options = HistoryAnalyzer.Options(timeZone: Self.utc)
        try analyzer.run(configuration: configuration, options: options)
        try directory.append("logs/agent.log", Self.line(session: "a", call: 2, end: Self.end + 4_000, output: 80))
        var rotated = false
        var rotationError: (any Error)?
        let raced = try analyzer.run(configuration: configuration, options: options, progress: { _ in
            guard !rotated else { return }
            rotated = true
            do {
                try FileManager.default.moveItem(at: directory.file("logs/agent.log"), to: directory.file("logs/agent.log.1"))
                try directory.write("logs/agent.log", Self.line(session: "b", call: 1, end: Self.end + 8_000, output: 40))
            } catch { rotationError = error }
        })
        if let rotationError { throw rotationError }
        #expect(raced.filesUnreadable == 1 && analyzer.days(sources: [.hermes]).first?.totals.output == 200)
        try analyzer.run(configuration: configuration, options: options)
        let day = try #require(analyzer.days(sources: [.hermes]).first)
        #expect(day.totals.requests == 3 && day.totals.output == 320 && day.agents == 2)
    }

    @Test func trafficMatchesHermesEntrypointsWithoutMatchingDevelopmentScripts() {
        let rules = AgentProcessRules(platform: TrafficPlatform(kind: .macos, home: "/Users/dev"))
        func source(_ executable: String, _ args: [String] = []) -> SourceKind? {
            rules.source(executable: executable, arguments: { args }, ancestorExecutables: [])
        }
        #expect(source("/usr/local/bin/hermes") == .hermes)
        #expect(rules.needsArguments(executable: "/usr/bin/python3.13", bundleIdentifier: { nil }))
        #expect(source("/usr/bin/python3", ["python3", "-u", "-m", "hermes_cli.main", "gateway", "ensure"]) == .hermes)
        #expect(source("/usr/bin/python3", ["python3", "/Users/dev/.hermes/hermes-agent/cli.py"]) == .hermes)
        #expect(source("/usr/bin/python3", ["python3", "/venv/bin/hermes"]) == .hermes)
        #expect(source("/usr/bin/python3", ["python3", "/projects/hermes-agent/test.py"]) == nil)
        #expect(source("/usr/bin/python3", ["python3", "other.py", "-m", "hermes_cli.main"]) == nil)
        #expect(source("/Applications/Hermes.app/Contents/MacOS/Hermes") == nil)
    }

    private static func line(session: String, call: Int, end: Int64, output: Int) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .current
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss,SSS"
        let date = formatter.string(from: Date(timeIntervalSince1970: Double(end) / 1000))
        return "\(date) INFO [\(session)] agent.conversation_loop: API call #\(call): model=claude-sonnet-4.6 provider=anthropic in=1200 out=\(output) total=\(1200 + output) latency=4.0s cache=500/1200 (42%) write=100\n"
    }
}
