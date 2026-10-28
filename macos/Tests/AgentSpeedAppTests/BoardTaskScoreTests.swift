import AgentSpeedCore
import AgentSpeedOrbModel
import CoreGraphics
import Foundation
import Testing
@testable import AgentSpeed

/// The boards' 智力 (DESIGN 2c Live metrics; DESIGN 12): the forms of the hero and the rows, the proof mark following the
/// supplying agent, the motion of a change (count-up, crossfades, Reduce Motion), the dotted arc that never cuts a dot,
/// and the score cards with their facts and accessibility.
@Suite("Board task score")
@MainActor
struct BoardTaskScoreTests {
    static let l10n = Localizer(preference: .zhHant)

    static func reading(_ shown: Int?, _ form: TaskValueForm = .live, _ state: TaskState = .passing, count: Int = 1) -> TaskSetReading {
        TaskSetReading(form: shown == nil ? .none : form, shown: shown, state: shown == nil ? nil : state, count: count, agentId: "a")
    }

    /// The `team` simulation after 40 ticks with the scores of `scenario`.
    static func board(_ style: BoardStyle, _ scenario: BoardScorePreviewData.Scenario, size: BoardSize = .medium,
                      readouts: BoardReadouts = .all) -> (tick: BoardTick, layout: BoardContentLayout) {
        let feed = BoardSimFeed(simulation: BoardSimulation(scenario: "team"), style: style, size: size, dark: true, l10n: l10n,
                                metrics: .init(readouts: readouts))
        for _ in 0..<40 { feed.step() }
        let tick = BoardScorePreviewData.tick(feed.tick, scenario: scenario)
        let builder = BoardContentBuilders.make(style: style, size: size, dark: true, l10n: l10n, capsule: nil, highContrast: false)
        return (tick, builder.layout(tick))
    }

    static func item(_ layout: BoardContentLayout, _ key: String) -> BoardContentItem? {
        layout.items.first { $0.key == key }
    }

    // MARK: Forms

    @Test func theHeroShowsTheHighestLiveScoreElseTheHighestResting() {
        let live = Self.board(.glass, .live).tick.metrics.intelligence
        #expect(live.form == .live && live.shown == 86 && live.state == .passing && live.count >= 2)
        let resting = Self.board(.glass, .resting).tick.metrics.intelligence
        #expect(resting.form == .resting && resting.shown == 72 && resting.isResting)
        #expect(Self.board(.glass, .none).tick.metrics.intelligence == .none)
        let pending = Self.board(.glass, .pending).tick.metrics.intelligence
        #expect(pending.shown == 58 && pending.state == .pending && pending.isUnproven)
    }

    @Test func rowsShowTheirGroupsHighestAndTheirOwnValue() throws {
        let (tick, _) = Self.board(.neon, .live, size: .large)
        let models = BoardRowModels.make(tick, l10n: Self.l10n, twoLine: true)
        for (row, model) in zip(tick.rows, models) {
            switch row.kind {
            case .group(let index):
                #expect(model.intel == BoardRowScore(tick.groups[index].intelligenceReading))
            case .sub(let index, let sub):
                #expect(model.intel == BoardRowScore(TaskSetReading(agent: tick.groups[index].subs[sub])))
            case .moreSubs, .moreGroups:
                #expect(model.intel == nil, "the more lines leave the cell empty")
            }
        }
        // A score shows live in the live color, resting pale, none as `–`; the mark only under an unproven number.
        let none = BoardRowScore(.none)
        #expect(none.text == "–" && !none.live && !none.unproven)
        let resting = BoardRowScore(Self.reading(72, .resting, .pending))
        #expect(resting.text == "72" && !resting.live && resting.unproven)
        // 智力 off: no column.
        let off = Self.board(.glass, .live, size: .medium, readouts: BoardReadouts(intelligence: false, thinking: true, cost: true)).tick
        #expect(BoardRowModels.make(off, l10n: Self.l10n).allSatisfy { $0.intel == nil })
    }

    /// 思考's quiet is Σ w = 0, not the absence of a score: 智力 keeps its own form when work stops.
    @Test func thinkingQuietIsIndependentOfTheScore() {
        let resting = Self.board(.glass, .resting).tick.metrics
        #expect(resting.quiet && resting.thinking == "–" && resting.intelligence.shown == 72)
        let none = Self.board(.glass, .none).tick.metrics
        #expect(!none.quiet && none.thinking == "174" && none.intelligence == .none)
    }

    // MARK: Proof mark

    @Test func theProofMarkFollowsTheSupplyingAgent() throws {
        for style in BoardStyle.allCases {
            let key = style == .timing ? "box.intel.n" : "gauge.iq.n"
            #expect(Self.item(Self.board(style, .pending).layout, key + ".mark") != nil, "\(style) pending")
            #expect(Self.item(Self.board(style, .unverified).layout, key + ".mark") != nil, "\(style) unverified")
            #expect(Self.item(Self.board(style, .live).layout, key + ".mark") == nil, "\(style) passing")
            #expect(Self.item(Self.board(style, .none).layout, key + ".mark") == nil, "\(style) none")
        }
        // Glass draws the unproven value arc as dots, the passing one solid.
        let dotted = try #require(Self.item(Self.board(.glass, .pending).layout, "gauge.arc")?.sweep)
        let solid = try #require(Self.item(Self.board(.glass, .live).layout, "gauge.arc")?.sweep)
        #expect(dotted.dots != nil && solid.dots == nil)
        #expect(abs(solid.fraction - 0.86) < 1e-9 && abs(dotted.fraction - 0.58) < 1e-9)
    }

    /// The dotted arc's reveal ends after a whole dot that clears the tip ring, never inside a dot.
    @Test func theDottedArcNeverCutsADot() {
        let stroke: CGFloat = 3, radius: CGFloat = 29
        let dots = BoardContentItem.Sweep.Dots(pitch: 2 * stroke, radius: stroke / 2, clearance: 2.31 + 1.5)
        let sweep = BoardContentItem.Sweep(center: .zero, radius: radius, width: stroke + 10, start: 0, sweep: 4 * .pi / 3,
                                           fraction: 0, dots: dots)
        for shown in 0...100 {
            let fraction = Double(shown) / 100
            let end = sweep.revealed(fraction) * radius
            let tip = sweep.sweep * CGFloat(fraction) * radius
            guard end > 0 else { continue }
            // The end lies in the gap after a dot: past its far edge, before the next one starts.
            let phase = end.truncatingRemainder(dividingBy: dots.pitch)
            #expect(phase >= dots.radius - 1e-6 && phase <= dots.pitch - dots.radius + 1e-6, "\(shown)")
            #expect(tip - end >= dots.clearance - 0.5 - 1e-6, "\(shown): the last dot keeps clear of the ring")
        }
        var plain = sweep
        plain.dots = nil
        #expect(plain.revealed(0.5) == sweep.sweep / 2)
    }

    // MARK: Motion

    @Test func theHeroCountsUpRisesOfThreeOrMore() {
        let climb = BoardScoreMotion.hero(from: Self.reading(41), to: Self.reading(81), reduceMotion: false)
        #expect(climb.climb == TaskScoreMotion.climbSeconds(40) && climb.numberFade == nil)
        #expect(BoardScoreMotion.hero(from: Self.reading(41), to: Self.reading(43), reduceMotion: false) == .none,
                "a rise of 1 or 2 replaces the digits in place")
        let fall = BoardScoreMotion.hero(from: Self.reading(81), to: Self.reading(60), reduceMotion: false)
        #expect(fall.climb == nil && fall.numberFade == 0.2, "a fall crossfades, never rolling down")
        #expect(BoardScoreMotion.hero(from: .none, to: Self.reading(60), reduceMotion: false).numberFade == 0.2)
        let reduced = BoardScoreMotion.hero(from: Self.reading(41), to: Self.reading(81), reduceMotion: true)
        #expect(reduced.climb == nil && reduced.numberFade == 0.15, "Reduce Motion: a 0.15 s crossfade")
        // Live ↔ resting crossfades the color in 0.2 s, kept with Reduce Motion.
        let rest = BoardScoreMotion.hero(from: Self.reading(72), to: Self.reading(72, .resting), reduceMotion: true)
        #expect(rest.numberFade == 0.2 && rest.shapeFade == 0.2)
        // The proof mark fades with the state (0.25 s), at once with Reduce Motion.
        let pending = BoardScoreMotion.hero(from: Self.reading(72), to: Self.reading(72, .live, .pending), reduceMotion: false)
        #expect(pending.shapeFade == ProofMark.fadeSeconds && pending.numberFade == nil)
        #expect(BoardScoreMotion.hero(from: Self.reading(72), to: Self.reading(72, .live, .pending), reduceMotion: true) == .none)
    }

    @Test func rowsChangeInPlace() {
        func row(_ shown: Int?, live: Bool = true, unproven: Bool = false) -> BoardRowScore {
            BoardRowScore(shown: shown, live: live, unproven: unproven)
        }
        #expect(BoardScoreMotion.row(from: row(41), to: row(81), reduceMotion: false) == nil, "no count-up or roll in a row")
        #expect(BoardScoreMotion.row(from: row(81), to: row(41), reduceMotion: false) == 0.2)
        #expect(BoardScoreMotion.row(from: row(nil), to: row(41), reduceMotion: false) == 0.2)
        #expect(BoardScoreMotion.row(from: row(41), to: row(81), reduceMotion: true) == 0.15)
        #expect(BoardScoreMotion.row(from: row(41), to: row(41, live: false), reduceMotion: true) == 0.2)
        #expect(BoardScoreMotion.row(from: row(41), to: row(41, unproven: true), reduceMotion: false) == ProofMark.fadeSeconds)
        #expect(BoardScoreMotion.row(from: row(41), to: row(41, unproven: true), reduceMotion: true) == nil)
    }

    /// A count-up passes through every integer with easeOutCubic over `min(0.8, 0.3 + 0.012 Δ)` s; between ticks each
    /// layout shows the integer of its instant; a further rise climbs on, a fall ends it.
    @Test func theHeroCountUpRunsBetweenTicks() {
        var hero = BoardHeroScore()
        let start = 100.0
        #expect(hero.update(Self.reading(41), previous: nil, fresh: false, now: start, reduceMotion: false).shown == 41)
        let first = hero.update(Self.reading(81), previous: Self.reading(41), fresh: true, now: start, reduceMotion: false)
        #expect(first.shown == 41 && first.motion.climb != nil)
        let end = start + TaskScoreMotion.climbSeconds(40)
        #expect(hero.climbsUntil(after: start) == end)
        var last = 41
        var seen: Set<Int> = []
        var time = start
        while time < end + 0.05 {
            let shown = hero.update(Self.reading(81), previous: nil, fresh: false, now: time, reduceMotion: false).shown ?? 0
            #expect(shown >= last, "never counts down")
            last = shown
            seen.insert(shown)
            time += 1.0 / 60
        }
        #expect(last == 81 && hero.climbsUntil(after: time) == nil)
        #expect(seen.count > 20, "a 60 fps count-up shows many intermediate integers")
        // A further rise while climbing climbs on from the number shown.
        _ = hero.update(Self.reading(40), previous: Self.reading(81), fresh: true, now: 200, reduceMotion: false)
        _ = hero.update(Self.reading(70), previous: Self.reading(40), fresh: true, now: 200, reduceMotion: false)
        let middle = hero.update(Self.reading(70), previous: nil, fresh: false, now: 200.2, reduceMotion: false).shown ?? 0
        let further = hero.update(Self.reading(90), previous: Self.reading(70), fresh: true, now: 200.2, reduceMotion: false)
        #expect(further.shown == middle && further.motion.climb != nil && middle > 40 && middle < 70)
        // A fall ends the count-up and crossfades to the new value.
        let fall = hero.update(Self.reading(20), previous: Self.reading(90), fresh: true, now: 200.3, reduceMotion: false)
        #expect(fall.shown == 20 && fall.motion.numberFade == 0.2 && hero.climbsUntil(after: 200.3) == nil)
    }

    @Test func aBuilderReportsItsCountUp() {
        let feed = BoardSimFeed(simulation: BoardSimulation(scenario: "team"), style: .neon, size: .medium, dark: true,
                                l10n: Self.l10n, metrics: .init())
        for _ in 0..<40 { feed.step() }
        let builder = BoardContentBuilders.make(style: .neon, size: .medium, dark: true, l10n: Self.l10n, capsule: nil,
                                                highContrast: false)
        var base = feed.tick
        base.metrics.intelligence = Self.reading(30)
        _ = builder.layout(base)
        var next = base
        next.metrics.intelligence = Self.reading(80)
        _ = builder.layout(next)
        #expect(builder.climbsUntil(after: Clock.now) != nil)
        builder.reduceMotion = true
        var reduced = next
        reduced.metrics.intelligence = Self.reading(95)
        _ = builder.layout(reduced)
        #expect(builder.climbsUntil(after: Clock.now) == nil, "Reduce Motion: no count-up")
    }

    // MARK: Cards

    @Test func cardsBelongToTheAgentWhoseScoreTheRowShows() throws {
        let (tick, layout) = Self.board(.glass, .live, size: .large)
        for group in tick.groups {
            let card = BoardScoreCards.group(group, l10n: Self.l10n)
            #expect(card.header.project == group.cardAgent.project && card.header.subAgents == group.subs.count)
            #expect(card.score?.shown == group.intelligenceReading.shown || group.intelligenceReading.form == .none)
            #expect((card.score?.topCount ?? 1) == 1, "task.card.top is the hero's eyebrow only")
            // A source that records no tool calls has no score: its card says so.
            #expect((card.score == nil) == !TaskScoring.scores(group.cardAgent.source))
            #expect(card.facts.last?.hasPrefix("速度 ") == true)
        }
        let hero = try #require(BoardScoreCards.hero(tick, l10n: Self.l10n))
        #expect(hero.score?.topCount == tick.metrics.intelligence.count && hero.score?.shown == 86)
        #expect(BoardScoreCards.hero(Self.board(.glass, .none, size: .large).tick, l10n: Self.l10n) == nil, "nothing while none")
        // Every shown row has its card; the more lines have none.
        for row in tick.rows {
            let card = BoardScoreCards.row(row.key, in: tick, l10n: Self.l10n)
            switch row.kind {
            case .group, .sub: #expect(card != nil, "\(row.key)")
            case .moreSubs, .moreGroups: #expect(card == nil)
            }
        }
        #expect(layout.scoreArea != nil)
        #expect(Self.board(.glass, .live, size: .large, readouts: BoardReadouts(intelligence: false, thinking: true, cost: true))
            .layout.scoreArea == nil)
    }

    @Test func aGroupWithoutAScoreShowsItsRootsCard() throws {
        let (tick, _) = Self.board(.timing, .none, size: .medium)
        let group = try #require(tick.groups.first)
        let card = BoardScoreCards.group(group, l10n: Self.l10n)
        #expect(card.header.project == group.root.project && card.score?.form == TaskScoreForm.none)
    }

    @Test func rowsAnnounceTheirScoreWordsAndCarryTheCard() throws {
        let (tick, _) = Self.board(.glass, .pending, size: .medium)
        let index = try #require(tick.groups.firstIndex { $0.intelligenceReading.isUnproven })
        let group = tick.groups[index]
        let label = BoardAccessibilityReporter.rowLabel(group, rank: index + 1, l10n: Self.l10n, intelligence: true)
        #expect(label.contains("智力 58，待重新驗證"))
        let none = try #require(tick.groups.first { $0.intelligenceReading.form == .none })
        #expect(!BoardAccessibilityReporter.rowLabel(none, rank: 4, l10n: Self.l10n, intelligence: true).contains("智力"),
                "nothing for none")
        let content = BoardAccessibilityReporter.cardContent(BoardScoreCards.group(group, l10n: Self.l10n), l10n: Self.l10n)
        #expect(content.first?.value.hasPrefix("智力 58，待重新驗證") == true && content.first?.importance == .high)
        #expect(content.allSatisfy { $0.label == "任務評分詳情" } && content.count > 8)
        let words = BoardAccessibilityReporter.metricWords(tick.metrics, currency: "美元", l10n: Self.l10n)
        #expect(words.first?.hasPrefix("智力 58，待重新驗證，") == true)
        let resting = Self.board(.glass, .resting, size: .medium).tick.metrics
        #expect(BoardAccessibilityReporter.metricWords(resting, currency: "美元", l10n: Self.l10n).first?
            .hasPrefix("智力 72，閒置中") == true)
        // While none, `intel.none` replaces only the 智力 words: a shown 思考 is still read.
        let quiet = Self.board(.glass, .none, size: .medium).tick.metrics
        let quietWords = BoardAccessibilityReporter.metricWords(quiet, currency: "美元", l10n: Self.l10n).first
        #expect(quietWords == (quiet.showsThinking ? "智力：尚無評分，思考每秒 \(quiet.thinking) 個 token" : "智力：尚無評分"))
    }
}
