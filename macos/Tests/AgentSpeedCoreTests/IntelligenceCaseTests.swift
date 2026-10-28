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

    static func names(_ group: String) -> [String] {
        ((try? cases()[group] as? [[String: Any]]) ?? []).compactMap { $0.string("name") }
    }

    static var windowNames: [String] { names("windows") }
    static var agentWindowNames: [String] { names("agentWindows") }
    static var groupWindowNames: [String] { names("groupWindows") }

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

    /// The case's agents and responses in an engine priced with the fixture table.
    static func engine(_ group: String, _ name: String) throws -> (SpeedEngine, [[String: Any]]) {
        let windows = try #require(cases()[group] as? [[String: Any]])
        let spec = try #require(windows.first { $0.string("name") == name })
        var engine = SpeedEngine(pricing: PriceBook(tables: [try table()]))
        for agent in try #require(spec["agents"] as? [[String: Any]]) {
            engine.setAgent(EngineAgent(
                id: try #require(agent.string("id")),
                source: try #require(SourceKind(rawValue: agent.string("source") ?? "")),
                project: "fixture",
                model: agent.string("model"),
                phase: try #require(AgentPhase(rawValue: agent.string("phase") ?? "")),
                inFlightStartMs: agent.int64("inFlightStartMs"),
                lastLineMs: agent.int64("lastLineMs"),
                parentId: agent.string("parentId"),
                modes: effortModes(agent.string("effort"))
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
                modes: effortModes(response.string("effort"))
            ), project: "fixture", isCompletion: true, emitsEvents: false)
        }
        return (engine, try #require(spec["queries"] as? [[String: Any]]))
    }

    static func expect(_ sums: IntelligenceSums, _ expected: [String: Any], _ label: String) {
        #expect(sums.shownIndex == (expected["shown"] as? NSNumber)?.intValue, "\(label) shown")
        EngineCaseTests.expectClose(sums.index, expected.double("intelligence"), "\(label) intelligence")
        EngineCaseTests.expectClose(sums.thinking, expected.double("liveThinking"), "\(label) liveThinking")
        EngineCaseTests.expectClose(sums.thinkingShare, expected.double("thinkingShare"), "\(label) thinkingShare")
    }

    @Test(arguments: windowNames)
    func window(_ name: String) throws {
        var (engine, queries) = try Self.engine("windows", name)
        for query in queries {
            let now = try #require(query.int64("nowMs"))
            let output = engine.advance(nowMs: now, todayStartMs: 0, emitsEvents: false)
            let snapshot = SnapshotBuilder.running(output, context: .fixture(todayStartMs: 0))
            let kinds = try (query["sources"] as? [String])?.map { try #require(SourceKind(rawValue: $0)) }
            let sums = OrbSubset.reading(snapshot, sources: kinds.map { SourceSet($0) }).intelligence
            Self.expect(sums, try #require(query["expect"] as? [String: Any]), "\(name) @ \(now)\(kinds.map { " \($0)" } ?? "")")
        }
    }

    /// METRICS 6.9, Agent intelligence: every agent's form, value and own sums, which add up to the whole set's.
    @Test(arguments: agentWindowNames)
    func agentWindow(_ name: String) throws {
        var (engine, queries) = try Self.engine("agentWindows", name)
        for query in queries {
            let now = try #require(query.int64("nowMs"))
            let output = engine.advance(nowMs: now, todayStartMs: 0, emitsEvents: false)
            let snapshot = SnapshotBuilder.running(output, context: .fixture(todayStartMs: 0))
            Self.expect(snapshot.intelligence, try #require(query["expect"] as? [String: Any]), "\(name) @ \(now)")
            let expectedAgents = try #require(query["agents"] as? [String: [String: Any]])
            var added = IntelligenceSums.zero
            for agent in snapshot.agents { added.add(agent.intelligence) }
            for (key, value) in [("output", \IntelligenceSums.output), ("graded", \.gradedOutput),
                                 ("thinking", \.thinking), ("thinkingBase", \.thinkingBase)] {
                EngineCaseTests.expectClose(added[keyPath: value], snapshot.intelligence[keyPath: value], "\(name) @ \(now) agents add up: \(key)")
            }
            #expect(snapshot.agents.count == expectedAgents.count, "\(name) @ \(now) listed agents")
            for (id, expected) in expectedAgents {
                let label = "\(name) @ \(now) \(id)"
                let agent = try #require(snapshot.agents.first { $0.id == id }, "\(label) listed")
                let form = agent.intelligenceForm
                let kind = switch form {
                case .live: "live"
                case .resting: "resting"
                case .none: "none"
                }
                #expect(kind == expected.string("form"), "\(label) form")
                EngineCaseTests.expectClose(form.value, expected.double("intelligence"), "\(label) intelligence")
                #expect(form.shown == (expected["shown"] as? NSNumber)?.intValue, "\(label) shown")
                EngineCaseTests.expectClose(agent.intelligence.thinking, expected.double("liveThinking"), "\(label) liveThinking")
                EngineCaseTests.expectClose(agent.intelligence.thinkingShare, expected.double("thinkingShare"), "\(label) thinkingShare")
                #expect(agent.grade?.value == (expected["grade"] as? NSNumber)?.intValue, "\(label) grade")
            }
        }
    }

    /// METRICS 6.9, Group intelligence: a group adds its members' agent sums (never an average of their values), rests
    /// on its root's grade, and the groups of a set add up to the set's sums.
    @Test(arguments: groupWindowNames)
    func groupWindow(_ name: String) throws {
        var (engine, queries) = try Self.engine("groupWindows", name)
        for query in queries {
            let now = try #require(query.int64("nowMs"))
            let output = engine.advance(nowMs: now, todayStartMs: 0, emitsEvents: false)
            let snapshot = SnapshotBuilder.running(output, context: .fixture(todayStartMs: 0))
            Self.expect(snapshot.intelligence, try #require(query["expect"] as? [String: Any]), "\(name) @ \(now)")
            let groups = try #require(query["groups"] as? [String: [String: Any]])
            var added = IntelligenceSums.zero
            for (root, expected) in groups {
                let label = "\(name) @ \(now) group \(root)"
                let members = try #require(expected["members"] as? [String])
                var sums = AgentGroupSums()
                for id in members {
                    sums.add(try #require(snapshot.agents.first { $0.id == id }, "\(label) \(id) listed"), billable: true)
                }
                added.add(sums.intelligence)
                let rootGrade = try #require(snapshot.agents.first { $0.id == root }).grade
                let form = sums.intelligence(rootGrade: rootGrade)
                let kind = switch form {
                case .live: "live"
                case .resting: "resting"
                case .none: "none"
                }
                #expect(kind == expected.string("form"), "\(label) form")
                EngineCaseTests.expectClose(form.value, expected.double("intelligence"), "\(label) intelligence")
                #expect(form.shown == (expected["shown"] as? NSNumber)?.intValue, "\(label) shown")
                EngineCaseTests.expectClose(sums.intelligence.thinking, expected.double("liveThinking"), "\(label) liveThinking")
                EngineCaseTests.expectClose(sums.intelligence.thinkingShare, expected.double("thinkingShare"), "\(label) thinkingShare")
                #expect(rootGrade?.value == (expected["grade"] as? NSNumber)?.intValue, "\(label) root grade")
            }
            for (key, value) in [("output", \IntelligenceSums.output), ("graded", \.gradedOutput), ("thinking", \.thinking),
                                 ("thinkingBase", \.thinkingBase)] {
                EngineCaseTests.expectClose(added[keyPath: value], snapshot.intelligence[keyPath: value], "\(name) @ \(now) groups add up: \(key)")
            }
        }
    }
}
