import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeedOrbModel

/// DESIGN 2c Live metrics: the hero and ranking geometry per style and size, the rows kept, the sub-gauge tables.
@Suite("Board live metrics geometry")
struct BoardGeometryTests {
    static let sizes: [BoardSize] = [.small, .medium, .large]

    @Test func withEverySwitchOffTheBoardIsAsBefore() {
        for style in BoardStyle.allCases {
            for size in Self.sizes {
                let legacy = BoardGeometry(style, size, readouts: .none)
                #expect(legacy.isLegacy && !legacy.hasStrip && legacy.captionsHeight == 0)
                #expect(BoardLayoutMetrics(legacy) == BoardLayoutMetrics.of(style, size))
                // 每列費用 alone shows nothing at S.
                let rowCostOnly = BoardGeometry(style, .small, readouts: BoardReadouts(intelligence: false, thinking: false, cost: false, rowCost: true))
                #expect(rowCostOnly.isLegacy)
            }
        }
        #expect(BoardGeometry(.neon, .medium).bandHeight == 92)
        #expect(BoardGeometry(.neon, .medium, readouts: .all).bandHeight == 100)
    }

    @Test func theRankingFollowsTheHeroTable() {
        let expected: [BoardStyle: [Double]] = [.glass: [138, 186, 254], .timing: [139, 187, 257], .neon: [136, 186, 256]]
        let bands: [BoardStyle: [(Double, Double)]] = [.glass: [(12, 78), (14, 86), (16, 94)], .timing: [(14, 72), (16, 80), (18, 88)],
                                                       .neon: [(12, 92), (14, 100), (16, 108)]]
        for style in BoardStyle.allCases {
            for (index, size) in Self.sizes.enumerated() {
                let geometry = BoardGeometry(style, size, readouts: .all)
                #expect(geometry.rankingHeight == expected[style]![index], "\(style) \(size) ranking height")
                #expect(geometry.bandTop == bands[style]![index].0 && geometry.bandHeight == bands[style]![index].1, "\(style) \(size) band")
                #expect(geometry.firstRow == geometry.captionsHeight + 2)
            }
        }
        let glass = BoardGeometry(.glass, .medium, readouts: .all)
        #expect(glass.stripTop == 100 && glass.stripHeight == 16 && glass.rankingTop == 116)
        let timing = BoardGeometry(.timing, .medium, readouts: .all)
        #expect(timing.stripTop == 104 && timing.stripHeight == 12 && timing.rankingTop == 121)
        let neon = BoardGeometry(.neon, .large, readouts: .all)
        #expect(!neon.hasStrip && neon.rankingTop == 128 && neon.gaugeArea == 91)
        #expect(BoardGeometry(.timing, .small, readouts: .all).gaugeWidth == 110)
        #expect(BoardGeometry(.glass, .large, readouts: .all).gaugeWidth == 128)
    }

    @Test func rowColumnsFollowTheTable() {
        let glass = BoardGeometry(.glass, .medium, readouts: .all)
        #expect(glass.intelligenceColumn == 22 && glass.valueColumn == 44 && glass.groupRow == 38 && glass.groupGap == 0)
        #expect(glass.secondLineBaseline == 32 && BoardGeometry(.neon, .medium, readouts: .all).secondLineBaseline == 31)
        let timing = BoardGeometry(.timing, .large, readouts: .all)
        #expect(timing.intelligenceColumn == 26 && timing.valueColumn == 42 && timing.intervalColumn == 34 && timing.groupRow == 40)
        let small = BoardGeometry(.timing, .small, readouts: .all)
        #expect(small.valueColumn == 40 && small.intervalColumn == 0 && !small.twoLine && small.groupRow == 30)
        let noIntelligence = BoardGeometry(.glass, .medium, readouts: BoardReadouts(intelligence: false, thinking: true, cost: true, rowCost: true))
        #expect(noIntelligence.valueColumn == 46 && noIntelligence.intelligenceColumn == 0 && noIntelligence.firstRow == 6)
        #expect(BoardGeometry(.timing, .medium, readouts: BoardReadouts(intelligence: false, thinking: true, cost: true, rowCost: false))
            .intervalColumn == 38)
    }

    /// The `team` scenario's shape: five groups, the first three with 3, 2 and 4 working sub-agents.
    static let team: [BoardPlanGroup] = [("a0", 3), ("a1", 2), ("a2", 4), ("a3", 0), ("a4", 0)].map { id, subs in
        BoardPlanGroup(id: id, live: 150, subs: (0..<subs).map { BoardPlanSub(id: "\(id)s\($0)", live: Double(100 - $0)) })
    }

    static func kept(_ style: BoardStyle, _ size: BoardSize, rowCost: Bool) -> (groups: Int, subs: Int, more: Bool) {
        let readouts = BoardReadouts(intelligence: true, thinking: true, cost: true, rowCost: rowCost)
        let rows = BoardLayoutPlanner.plan(team, metrics: BoardLayoutMetrics(BoardGeometry(style, size, readouts: readouts)))
        var groups = 0, subs = 0, more = false
        for row in rows {
            switch row.kind {
            case .group: groups += 1
            case .sub: subs += 1
            case .moreGroups: more = true
            case .moreSubs: break
            }
        }
        return (groups, subs, more)
    }

    @Test func rowsKeptMatchTheTable() {
        for style in BoardStyle.allCases {
            for rowCost in [true, false] {
                let small = Self.kept(style, .small, rowCost: rowCost)
                #expect(small.groups == 3 && small.subs == 0 && small.more, "\(style) S rowCost \(rowCost)")
            }
            let medium = Self.kept(style, .medium, rowCost: true)
            #expect(medium.groups == 4 && medium.subs == 0 && medium.more, "\(style) M")
            let mediumOff = Self.kept(style, .medium, rowCost: false)
            #expect(mediumOff.groups == 5 && mediumOff.subs == 0, "\(style) M without rowCost")
            let large = Self.kept(style, .large, rowCost: true)
            #expect(large.groups == 5 && large.subs == (style == .timing ? 1 : 0), "\(style) L")
            let largeOff = Self.kept(style, .large, rowCost: false)
            #expect(largeOff.groups == 5 && largeOff.subs == 2, "\(style) L without rowCost")
        }
    }

    @Test func subGaugeStacksMatchTheTables() {
        // (height, hidden height, 思考 baseline, 費用 baseline) per size.
        let tables: [BoardStyle: [(Double, Double, Double, Double)]] = [
            .glass: [(75, 58, 51, 68), (84, 67, 62, 79), (90, 73, 69, 86)],
            .neon: [(74.5, 57.5, 50.5, 67.5), (83, 65, 59, 77), (87.5, 69.5, 64.5, 82.5)],
            .timing: [(51, 36, 32, 47), (58, 41, 36.5, 53.5), (65, 46, 41, 60)],
        ]
        for style in BoardStyle.allCases {
            for (index, size) in Self.sizes.enumerated() {
                let metrics = BoardGaugeMetrics(style, size)
                let (height, hidden, thinking, cost) = tables[style]![index]
                let all = metrics.stack(intelligence: true, thinking: true, cost: true)
                #expect(all.height == height, "\(style) \(size) height")
                #expect(all.row(.thinking)?.baseline == thinking && all.row(.cost)?.baseline == cost, "\(style) \(size) baselines")
                let folded = metrics.stack(intelligence: true, thinking: false, cost: true)
                #expect(folded.height == hidden, "\(style) \(size) hidden height")
                // 費用 takes 思考's baseline; the dial moves down and 費用 up by half the difference once re-centred.
                #expect(folded.row(.cost)?.baseline == thinking, "\(style) \(size) 費用 on 思考's baseline")
                // Folding: without 智力 the table (or the box's small rows) alone; with 智力 alone its part only.
                let table = metrics.stack(intelligence: false, thinking: true, cost: true)
                #expect(table.main == nil && table.rows.first?.top == 0)
                #expect(metrics.stack(intelligence: true, thinking: false, cost: false).height == (style == .timing ? metrics.mainRow : metrics.mainHeight))
            }
        }
        #expect(BoardGaugeMetrics(.timing, .medium).stack(intelligence: true, thinking: true, cost: true).mainBaseline == 17.5)
        #expect(BoardGaugeMetrics(.glass, .medium).mainNumberBaseline == 42.5 && BoardGaugeMetrics(.neon, .large).meterTop == 45.5)
        #expect(BoardGaugeMetrics(.timing, .medium).stack(intelligence: false, thinking: true, cost: true).rows.map(\.baseline) == [12, 29.5])
    }

    @Test func theArcAndTheMeterSpanTheIndexRange() {
        #expect(BoardIntelligenceScale.arc(123) == 63.0 / 96)
        #expect(BoardIntelligenceScale.arc(nil) == 0 && BoardIntelligenceScale.arc(50) == 0 && BoardIntelligenceScale.arc(170) == 1)
        #expect(BoardIntelligenceScale.segments(123) == 8)
        #expect(BoardIntelligenceScale.segments(156) == 12 && BoardIntelligenceScale.segments(nil) == 0)
        #expect(BoardIntelligenceScale.segments(64) == 1, "half a segment rounds away from zero")
    }

    @Test func theStripSparklineKeepsItsMargins() {
        let points = BoardHistoryShape.strip([0, 50, 100], width: 300, height: 16)
        #expect(points.first == SIMD2(0, 14.5))
        #expect(abs(points[2].y - (16 - 1.5 - 100 / 112 * 13)) < 1e-9)
        #expect(BoardHistoryShape.stripBars([0, 10], count: 4, height: 12) == [1.5, 1.5, 1.5, 12])
    }
}

/// METRICS 6.9 *Group intelligence* and 9.5 *Per group* as the board's grouping fills them.
@Suite("Board group sums")
struct BoardGroupSumsTests {
    static func agent(_ id: String, parent: String? = nil, source: SourceKind = .claudeCode, output: Double, grade: Int?,
                      cost: Double = 0, known: Double? = nil) -> AgentStatus {
        var sums = IntelligenceSums()
        sums.output = output
        sums.gradedOutput = output * Double(grade ?? 80)
        sums.thinking = output * 0.25
        sums.thinkingBase = output
        return AgentStatus(id: id, source: source, project: id, model: grade == nil ? nil : "m", phase: output > 0 ? .generating : .idle,
                           isSubagent: parent != nil, live: Rate(output: output, total: output), recentSpeed: nil,
                           lastActivity: Date(), parentId: parent,
                           liveCost: LiveCost(microUsdPerSecond: cost, knownOutput: known ?? output),
                           grade: grade.map { IntelligenceGrade(modelClass: .standard, level: ($0 - 80) / 6) }, intelligence: sums)
    }

    @Test func membersWeighByTheirTokens() throws {
        let agents = [Self.agent("r", output: 30, grade: 98, cost: 600), Self.agent("s", parent: "r", output: 10, grade: 80, cost: 0, known: 0)]
        let group = try #require(BoardGrouping.groups(agents, metric: .output).first)
        #expect(group.sums.intelligence.output == 40)
        // (30 × 98 + 10 × 80) / 40 + 20 × 0.25.
        #expect(group.intelligence == .live(93.5 + 5))
        #expect(group.sums.cost.microUsdPerSecond == 600 && group.sums.costOutput == 40)
        #expect(group.sums.costKnownShare == 0.75 && group.sums.costState == .partial)
    }

    @Test func atRestTheGroupShowsItsRootsGradeOrNone() throws {
        let resting = [Self.agent("r", output: 0, grade: 92), Self.agent("s", parent: "r", output: 0, grade: 110)]
        #expect(try #require(BoardGrouping.groups(resting, metric: .output).first).intelligence == .resting(IntelligenceGrade(modelClass: .standard, level: 2)))
        let unknownRoot = [Self.agent("r", output: 0, grade: nil), Self.agent("s", parent: "r", output: 0, grade: 110)]
        #expect(try #require(BoardGrouping.groups(unknownRoot, metric: .output).first).intelligence == .none)
    }

    @Test func excludedMembersAddNoCost() throws {
        let agents = [Self.agent("r", source: .codex, output: 20, grade: 92, cost: 300), Self.agent("x", source: .claudeCode, output: 5, grade: 92, cost: 100)]
        let groups = BoardGrouping.groups(agents, metric: .output, billable: SourceSet(.claudeCode))
        let codex = try #require(groups.first { $0.id == "r" })
        #expect(codex.sums.costState == .subscriptionOnly && codex.sums.cost == .zero && codex.sums.intelligence.output == 20)
        let claude = try #require(groups.first { $0.id == "x" })
        #expect(claude.sums.costState == .priced && claude.sums.cost.microUsdPerSecond == 100)
        let unpriced = [Self.agent("u", output: 5, grade: 92, cost: 0, known: 0)]
        #expect(BoardGrouping.groups(unpriced, metric: .output).first?.sums.costState == .nothingPriced)
    }

    /// The groups of `spec/fixtures/intelligence-cases.json` `groupWindows`: a root and all its descendants.
    @Test func theFixtureGroupsAreRootsWithTheirDescendants() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("spec/fixtures/intelligence-cases.json")
        let root = try #require(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        for spec in try #require(root["groupWindows"] as? [[String: Any]]) {
            let agents = try #require(spec["agents"] as? [[String: Any]]).map { item in
                AgentStatus(id: item["id"] as? String ?? "", source: SourceKind(rawValue: item["source"] as? String ?? "") ?? .codex,
                            project: "p", model: item["model"] as? String, phase: .idle, isSubagent: item["parentId"] is String,
                            live: .zero, recentSpeed: nil, lastActivity: Date(), parentId: item["parentId"] as? String)
            }
            let groups = BoardGrouping.groups(agents, metric: .output)
            for query in try #require(spec["queries"] as? [[String: Any]]) {
                for (id, expected) in try #require(query["groups"] as? [String: [String: Any]]) {
                    let group = try #require(groups.first { $0.id == id })
                    #expect(Set([group.root.id] + group.subs.map(\.id)) == Set(expected["members"] as? [String] ?? []), "\(id)")
                }
            }
        }
    }
}
