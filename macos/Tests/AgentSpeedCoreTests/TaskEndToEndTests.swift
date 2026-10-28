import Foundation
import Testing
@testable import AgentSpeedCore

/// `spec/fixtures/task-e2e-expected.json` (METRICS 6.9 *Composing the stream*, *End to end*): the hand-written sessions
/// of `spec/fixtures/tasks/` read with the parsers and the task composer, in the fixture's order with one dedupe set;
/// every agent's events, the records, calibration, values, sets and comparison must match (numbers within 1e-9,
/// calibration within 1e-6).
@Suite("Task score end to end")
struct TaskEndToEndTests {
    static let file = "task-e2e-expected.json"

    static let fixture: JSONValue = {
        guard let data = try? Data(contentsOf: Fixtures.root.appendingPathComponent(file)),
              let document = try? JSONParser.parse(data) else { return .null }
        return document
    }()

    /// The fixture's files read like `reference.py`'s `task_states`.
    struct Corpus {
        var agents: [TaskValueReport.Agent] = []
        var composers: [String: TaskComposer] = [:]
        var records: [TaskRecord] = []
    }

    static func label(_ path: String) -> String {
        let root = Fixtures.root.path + "/"
        return path.hasPrefix(root) ? String(path.dropFirst(root.count)) : path
    }

    static func read(_ fixture: JSONValue, analysisMs: Int64) throws -> Corpus {
        var deduper = Deduper()
        var nextFileID: FileID = 0
        var parsers: [(path: String, parser: TranscriptParser)] = []
        var windows: [OpenClawTaskWindows.Window] = []
        let roots = fixture["roots"]?.object ?? [:]
        for item in fixture["order"]?.array ?? [] {
            let source = try #require(SourceKind(rawValue: item["source"]?.string ?? ""))
            let path = Fixtures.root.appendingPathComponent(item["path"]?.string ?? "").path
            if source == .openclaw {
                windows += OpenClawTaskWindows.read(path: path, deduper: &deduper) { _ in
                    defer { nextFileID += 1 }
                    return nextFileID
                } ?? []
                continue
            }
            let family = try #require(TranscriptFamily(source))
            let root = roots[source.rawValue]?.string.map { Fixtures.root.appendingPathComponent($0).path }
            parsers.append((path, TaskTranscriptReading.read(path: path, family: family, root: root, fileID: nextFileID,
                                                             deduper: &deduper)))
            nextFileID += 1
        }
        let members = parsers.map { AgentParents.Member(id: $0.path, family: $0.parser.family, thread: $0.parser.state.thread) }
        let parents = AgentParents.resolve(members)
        var corpus = Corpus()
        for (path, parser) in parsers {
            let state = parser.state
            let tasks = try #require(parser.tasks)
            corpus.agents.append(TaskValueReport.Agent(id: path, source: state.source, phase: state.phase,
                                                       lastLineMs: state.lastLineMs, parentId: parents[path],
                                                       events: tasks.events))
            corpus.composers[path] = tasks
            corpus.records += TaskTranscriptReading.records(of: tasks, agentId: path, source: state.source,
                                                            lastLineMs: state.lastLineMs, analysisMs: analysisMs,
                                                            timeZone: TimeZone(identifier: "UTC")!)
        }
        for window in windows {
            let state = window.parser.state
            corpus.agents.append(TaskValueReport.Agent(id: window.agentId, source: .openclaw, phase: state.phase,
                                                       lastLineMs: state.lastLineMs, parentId: window.parentId,
                                                       events: window.tasks.events))
            corpus.composers[window.agentId] = window.tasks
            corpus.records += TaskTranscriptReading.records(of: window.tasks, agentId: window.agentId, source: .openclaw,
                                                            lastLineMs: state.lastLineMs, analysisMs: analysisMs,
                                                            timeZone: TimeZone(identifier: "UTC")!)
        }
        corpus.records.sort {
            $0.agentId == $1.agentId ? $0.ordinal < $1.ordinal : AgentParents.ordinal($0.agentId, precedes: $1.agentId)
        }
        return corpus
    }

    static func parse(_ text: String) -> JSONValue {
        (try? JSONParser.parse(text)) ?? .null
    }

    @Test func everyValueMatchesTheReference() throws {
        let fixture = Self.fixture
        try #require(fixture != .null, "missing \(Self.file)")
        let now = try #require(fixture["nowMs"]?.int64)
        let analysisMs = try #require(fixture["analysisMs"]?.int64)
        let corpus = try Self.read(fixture, analysisMs: analysisMs)
        var comparison = TaskTreeComparison()

        // Agents: events and stitched sessions.
        let expectedAgents = fixture["agents"]?.object ?? [:]
        let gotLabels = Set(corpus.composers.keys.map(Self.label))
        #expect(gotLabels == Set(expectedAgents.keys), "agents \(gotLabels.sorted()) vs \(expectedAgents.keys.sorted())")
        for (path, tasks) in corpus.composers {
            let label = Self.label(path)
            guard let want = expectedAgents[label] else { continue }
            let source = corpus.agents.first { $0.id == path }?.source.rawValue ?? ""
            let got: JSONValue = .object([
                "source": .string(source),
                "stitched": .number(JSONLiteral(String(tasks.stitched))),
                "events": .array(tasks.events.map { Self.parse(TaskDumpJSON.event($0)) }),
            ])
            comparison.compare(got, want, "agents.\(label)")
        }

        // Records, calibration, comparison.
        let records = corpus.records.map { Self.parse(TaskDumpJSON.record($0, agentId: Self.label($0.agentId))) }
        comparison.compare(.array(records), fixture["records"] ?? .null, "records")
        var calibration: [SourceKind: TaskModels] = [:]
        var calibrationJSON: [String: JSONValue] = [:]
        for source in SourceKind.allCases {
            let part = corpus.records.filter { $0.source == source }
            guard !part.isEmpty else { continue }
            let models = TaskModels.calibrate(part)
            calibration[source] = models
            calibrationJSON[source.rawValue] = Self.parse(TaskDumpJSON.models(models))
        }
        var loose = TaskTreeComparison()
        loose.tolerance = 1e-6
        loose.compare(.object(calibrationJSON), fixture["calibration"] ?? .null, "calibration")
        comparison.absorb(loose)
        let range = fixture["comparisonRange"]
        let compared = TaskComparison.compare(corpus.records, first: range?["first"]?.string ?? "",
                                              last: range?["last"]?.string ?? "", source: nil)
        let (bySource, byModel) = TaskDumpJSON.comparison(compared)
        comparison.compare(.object(["bySource": Self.parse(bySource), "byModel": Self.parse(byModel)]),
                           fixture["comparison"] ?? .null, "comparison")

        // Values at nowMs, sets and groups.
        let sets = (fixture["sets"]?.array ?? []).map { value -> [SourceKind]? in
            value.array.map { $0.compactMap { $0.string.flatMap(SourceKind.init(rawValue:)) } }
        }
        let report = TaskValueReport(agents: corpus.agents, now: now, models: calibration, sets: sets)
        let rows = report.rows
            .sorted { AgentParents.ordinal(Self.label($0.agent.id), precedes: Self.label($1.agent.id)) }
            .map { Self.parse(TaskDumpJSON.task($0, parentId: $0.agent.parentId, label: Self.label)) }
        let wantRows = (fixture["values"]?.array ?? []).sorted {
            AgentParents.ordinal($0["agentId"]?.string ?? "", precedes: $1["agentId"]?.string ?? "")
        }
        comparison.compare(.array(rows), .array(wantRows), "values")
        comparison.compare(Self.parse(TaskDumpJSON.taskSets(report, label: Self.label)), fixture["taskSets"] ?? .null,
                           "taskSets")

        let report2 = "\(comparison.mismatches.count) differences:\n" + comparison.mismatches.prefix(60).joined(separator: "\n")
        #expect(comparison.mismatches.isEmpty, Comment(rawValue: report2))
        print("task e2e: \(corpus.composers.count) agents, \(corpus.records.count) records, \(comparison.numbers) numbers "
              + "(\(comparison.exactNumbers) exact, largest difference \(comparison.largestDifference)), "
              + "\(comparison.mismatches.count) differences")
    }
}
