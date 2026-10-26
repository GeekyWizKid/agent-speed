import Foundation
import Testing
@testable import AgentSpeedCore

/// `spec/fixtures/intelligence-cases.json` (METRICS 6.9), as `spec/tools/intelligence.py` checks it.
@Suite("Intelligence fixture cases")
struct IntelligenceCaseTests {
    static func cases() throws -> [String: Any] { try Fixtures.json("intelligence-cases.json") }

    static func table() throws -> PriceTable {
        try PriceTableTests.table(try #require(cases()["table"] as? String))
    }

    static func effortModes(_ token: String?) -> ExecutionModes {
        ExecutionModes([token.flatMap { ExecutionModeAdapters.effort($0, evidence: .recorded, scope: .request) }].compactMap { $0 })
    }

    static var windowNames: [String] {
        ((try? cases()["windows"] as? [[String: Any]]) ?? []).compactMap { $0.string("name") }
    }

    @Test func grades() throws {
        let grader = IntelligenceGrader(table: try Self.table(), agentProviders: [:])
        for spec in try #require(Self.cases()["grades"] as? [[String: Any]]) {
            let name = spec.string("name") ?? ""
            let grade = grader.grade(model: spec.string("model"), provider: spec.string("provider"), effort: spec.string("effort"))
            let expected = try #require(spec["expect"] as? [String: Any])
            #expect(grade.modelClass.rawValue == expected.int("class"), "\(name) class")
            #expect(grade.level == expected.int("level"), "\(name) level")
            #expect(grade.value == expected.int("grade"), "\(name) grade")
        }
    }

    @Test(arguments: windowNames)
    func window(_ name: String) throws {
        let windows = try #require(Self.cases()["windows"] as? [[String: Any]])
        let spec = try #require(windows.first { $0.string("name") == name })
        var engine = SpeedEngine(pricing: PriceBook(tables: [try Self.table()]))
        for agent in try #require(spec["agents"] as? [[String: Any]]) {
            engine.setAgent(EngineAgent(
                id: try #require(agent.string("id")),
                source: try #require(SourceKind(rawValue: agent.string("source") ?? "")),
                project: "fixture",
                model: agent.string("model"),
                phase: try #require(AgentPhase(rawValue: agent.string("phase") ?? "")),
                inFlightStartMs: agent.int64("inFlightStartMs"),
                lastLineMs: agent.int64("lastLineMs"),
                modes: Self.effortModes(agent.string("effort"))
            ))
        }
        for response in try #require(spec["responses"] as? [[String: Any]]) {
            engine.upsert(ResponseRecord(
                key: try #require(response.string("key")),
                source: try #require(SourceKind(rawValue: response.string("source") ?? "")),
                agentId: try #require(response.string("agentId")),
                model: response.string("model"),
                startMs: response.int64("startMs"),
                endMs: try #require(response.int64("endMs")),
                usage: TokenUsage(input: response.int("input"), output: response.int("output"),
                                  reasoning: response.int("reasoning")),
                isFinal: response["final"] as? Bool ?? true,
                provider: response.string("provider"),
                modes: Self.effortModes(response.string("effort"))
            ), project: "fixture", isCompletion: true, emitsEvents: false)
        }
        for query in try #require(spec["queries"] as? [[String: Any]]) {
            let now = try #require(query.int64("nowMs"))
            let output = engine.advance(nowMs: now, todayStartMs: 0, emitsEvents: false)
            let snapshot = SnapshotBuilder.running(output, context: .fixture(todayStartMs: 0))
            let kinds = try (query["sources"] as? [String])?.map { try #require(SourceKind(rawValue: $0)) }
            let sums = OrbSubset.reading(snapshot, sources: kinds.map { SourceSet($0) }).intelligence
            let label = "\(name) @ \(now)\(kinds.map { " \($0)" } ?? "")"
            let expected = try #require(query["expect"] as? [String: Any])
            #expect(sums.shownIndex == (expected["shown"] as? NSNumber)?.intValue, "\(label) shown")
            EngineCaseTests.expectClose(sums.index, expected.double("intelligence"), "\(label) intelligence")
            EngineCaseTests.expectClose(sums.thinking, expected.double("liveThinking"), "\(label) liveThinking")
            EngineCaseTests.expectClose(sums.thinkingShare, expected.double("thinkingShare"), "\(label) thinkingShare")
        }
    }
}
