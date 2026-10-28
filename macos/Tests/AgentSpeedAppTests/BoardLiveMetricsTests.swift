import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 2c Live metrics as the boards draw them: the prototype's numbers from the shared simulation, the sub-gauge
/// per style and size, the table cells, folding and thinking hidden at zero, the rows' 智力 column and second line
/// (no cost), the collapse at S, the tooltip and accessibility words.
@Suite("Board live metrics")
@MainActor
struct BoardLiveMetricsTests {
    static let l10n = Localizer(preference: .zhHant)

    /// The `team` scenario after 40 ticks, as `board-metrics.html?scenario=team&steps=40`.
    static func feed(_ style: BoardStyle, _ size: BoardSize, readouts: BoardReadouts = .all, currency: String = "USD",
                     subscriptions: Bool = false, reasoning: Bool = true) -> BoardSimFeed {
        var simulation = BoardSimulation(scenario: "team")
        simulation.recordsReasoning = reasoning
        let feed = BoardSimFeed(simulation: simulation, style: style, size: size, dark: true, l10n: l10n,
                                metrics: .init(readouts: readouts, currency: currency, subscriptions: subscriptions))
        for _ in 0..<40 { feed.step() }
        return feed
    }

    static func item(_ layout: BoardContentLayout, _ key: String) -> BoardContentItem? {
        layout.items.first { $0.key == key }
    }

    static func keys(_ layout: BoardContentLayout, prefix: String) -> [String] {
        layout.items.map(\.key).filter { $0.hasPrefix(prefix) }
    }

    @Test func theSimulationShowsThePrototypesNumbers() {
        let tick = Self.feed(.glass, .medium).tick
        #expect(tick.valueText == "707")
        #expect(tick.metrics.intelligence == 123 && tick.metrics.thinking == "174")
        #expect(tick.metrics.cost == BoardMoney(number: "0.024", unit: "US$/s"))
        #expect(tick.costText == "US$42.71")
        let first = tick.groups[0]
        #expect(first.root.project == "opencdn" && first.intelligence.shown == 135)
        #expect(tick.metrics.groupCosts[first.id]?.number == "0.0056")
        #expect(tick.groups.map { SpeedFormat.speed($0.live) } == ["177", "197", "168", "139", "25"])
    }

    @Test func moneyStatesFollowTheSpec() {
        let format = BoardMoneyFormat(currency: CurrencyTable.bundled.currencyOrUSD("USD"), rate: 1, l10n: Self.l10n)
        #expect(format.money(24_000, state: .priced) == BoardMoney(number: "0.024", unit: "US$/s"))
        #expect(format.money(24_000, state: .partial).atLeast)
        #expect(!format.money(0, state: .partial).atLeast, "a 0 never carries ≥")
        #expect(format.money(0, state: .priced).number == "0" && format.money(0, state: .priced).isQuiet)
        #expect(format.money(5, state: .nothingPriced) == BoardMoney(number: "–", unit: "", state: .unknown))
        #expect(format.money(5, state: .subscriptionOnly) == BoardMoney(number: "訂閱", unit: "", state: .tag))
        let twd = BoardMoneyFormat(currency: CurrencyTable.bundled.currencyOrUSD("TWD"), rate: 32.552, l10n: Self.l10n)
        #expect(twd.money(24_000, state: .priced).unit == "NT$/s")
    }

    /// DESIGN 2c *Table cells*: macOS, zh-Hant, USD — glass 105.5 and neon 100 at M, 92 and 89 at S.
    @Test func tableCellsMatchTheSpecWidths() {
        let palette = { (style: BoardStyle) in BoardPalette(style: style, dark: true, highContrast: false) }
        let labels = ["思考", "費用"], units = ["tok/s", "US$/s"]
        let expected: [(BoardStyle, BoardSize, CGFloat)] = [(.glass, .medium, 105.5), (.neon, .medium, 100), (.glass, .small, 92),
                                                            (.neon, .small, 89)]
        for (style, size, width) in expected {
            let table = BoardGaugeTable(style: style, size: size, palette: palette(style), labels: labels, units: units)
            #expect(abs(table.width(numberCell: table.numberBase) - width) <= 1.5, "\(style) \(size): \(table.width(numberCell: table.numberBase))")
        }
    }

    @Test func theSubGaugeSitsAtTheSideInsetCentredInTheBand() throws {
        // Glass M: nominal 112 wide, centred 16 in; the dial (66) centred on it, the stack (84) centred in the band (14, 86).
        let glass = Self.feed(.glass, .medium).layout
        let track = try #require(Self.item(glass, "gauge.track"))
        let center: CGFloat = 340 - 16 - 56, top: CGFloat = 14 + (86 - 84) / 2
        #expect(track.frame.midX == center)
        #expect(track.frame.minY + 6 == top)
        // Timing L: the data box's rows end at the side inset; the box (65) centred in the band (18, 88).
        let timing = Self.feed(.timing, .large).layout
        let main = try #require(Self.item(timing, "box.intel"))
        #expect(main.frame.maxX == 420 - 16 && main.frame.width >= 152)
        let boxTop: CGFloat = 18 + (88 - 65) / 2
        #expect(main.frame.minY == boxTop)
        let rows = Self.keys(timing, prefix: "box.").filter { !$0.contains(".n") }
        #expect(rows == ["box.intel", "box.think", "box.cost"])
        // Neon S: the meter (62) centred under the digits, the stack (74.5) centred in the band less the LED bar (92 − 17).
        let neon = Self.feed(.neon, .small).layout
        let meter = try #require(Self.item(neon, "gauge.meter"))
        #expect(meter.frame.width == 62 + 16 || meter.frame.width == 62 + 17)
        #expect(Self.keys(neon, prefix: "gauge.iq.n").count == 3, "three rolling digits")
    }

    @Test func thinkingHiddenAtZeroFoldsTheSubGauge() throws {
        let shown = Self.feed(.glass, .medium).layout
        let hidden = Self.feed(.glass, .medium, reasoning: false)
        #expect(hidden.tick.metrics.thinkingHidden && hidden.tick.metrics.intelligence == 117)
        let folded = hidden.layout
        #expect(!Self.keys(shown, prefix: "gauge.think").isEmpty)
        #expect(Self.keys(folded, prefix: "gauge.think").isEmpty)
        // The dial moves down 8.5 and 費用 up 8.5.
        let dialShown = try #require(Self.item(shown, "gauge.track")).frame.minY
        let dialFolded = try #require(Self.item(folded, "gauge.track")).frame.minY
        #expect(dialFolded - dialShown == 8.5 || dialFolded - dialShown == 8 || dialFolded - dialShown == 9)
        // 思考 alone never hides.
        let alone = Self.feed(.glass, .medium, readouts: BoardReadouts(intelligence: false, thinking: true, cost: false),
                              reasoning: false)
        #expect(!alone.tick.metrics.thinkingHidden && !Self.keys(alone.layout, prefix: "gauge.think").isEmpty)
    }

    @Test func rowsCarryTheIntelligenceColumnAndTheModelLineWithoutACost() throws {
        let medium = Self.feed(.neon, .medium)
        let models = BoardRowModels.make(medium.tick, l10n: Self.l10n, twoLine: true)
        #expect(models[0].intel == "135" && models[0].intelLive && models[0].twoLine && !models[0].detail.isEmpty)
        #expect(Self.item(medium.layout, "captions") != nil)
        #expect(medium.layout.items.contains { $0.key.hasSuffix("#iq") })
        #expect(!medium.layout.items.contains { $0.key.hasSuffix("#cost") })
        // Group rows are two-line at M and L (38 / 40 tall) whatever the switches; S never draws the second line.
        for readouts in [BoardReadouts.all, .none] {
            for (size, height) in [(BoardSize.medium, 38.0), (.large, 40.0)] {
                let feed = Self.feed(.glass, size, readouts: readouts)
                let group = try #require(feed.tick.rows.first { if case .group = $0.kind { true } else { false } })
                #expect(group.height == height, "\(size) \(readouts)")
                #expect(!feed.layout.items.contains { $0.key.hasSuffix("#cost") })
            }
        }
        let small = Self.feed(.neon, .small)
        #expect(BoardRowModels.make(small.tick, l10n: Self.l10n).allSatisfy { !$0.twoLine })
        // 智力 off: no column, no captions, the original value column.
        let noIntelligence = Self.feed(.timing, .medium, readouts: BoardReadouts(intelligence: false, thinking: true, cost: true))
        #expect(Self.item(noIntelligence.layout, "captions") == nil)
        #expect(!noIntelligence.layout.items.contains { $0.key.hasSuffix("#iq") })
    }

    @Test func everySwitchOffIsTheBoardBefore() {
        let off = Self.feed(.glass, .medium, readouts: .none)
        #expect(off.tick.metrics == BoardMetricsTick() && off.tick.metrics.groupCosts.isEmpty)
        #expect(Self.item(off.layout, "hero.spark") != nil && Self.item(off.layout, "hero.strip") == nil)
        #expect(Self.keys(off.layout, prefix: "gauge.").isEmpty && Self.item(off.layout, "rule") != nil)
    }

    @Test func subscriptionsShowTheTag() {
        let feed = Self.feed(.glass, .medium, subscriptions: true)
        #expect(feed.tick.metrics.cost.state == .tag && feed.tick.costIsTag && feed.tick.costText == "訂閱")
        #expect(feed.tick.metrics.groupCosts.values.allSatisfy { $0.state == .tag })
    }

    @Test func tooltipAndAccessibilityWords() {
        let tick = Self.feed(.glass, .medium).tick
        let group = tick.groups[0]
        let lines = BoardTooltip.metricLines(group, l10n: Self.l10n, readouts: .all, money: tick.metrics.groupCosts[group.id])
        #expect(lines.first?.hasPrefix("智力 135 · 思考 ") == true)
        #expect(lines.contains("等級 130 · 旗艦"))
        #expect(lines.last == "費用 0.0056 US$/s（API 等值）")
        #expect(BoardTooltip.caption(readouts: .all, l10n: Self.l10n) == "估算指數，不是測驗分數")
        let words = BoardAccessibilityReporter.metricWords(tick.metrics, currency: "美元", l10n: Self.l10n)
        #expect(words == ["智力 123，思考每秒 174 個 token", "費用每秒 0.024 美元（API 等值）"])
        // The tooltip's cost follows 費用: off, no cost line, and the feed keeps no group costs.
        let noCost = Self.feed(.glass, .medium, readouts: BoardReadouts(intelligence: true, thinking: true, cost: false)).tick
        #expect(noCost.metrics.groupCosts.isEmpty)
        #expect(!BoardTooltip.metricLines(noCost.groups[0], l10n: Self.l10n, readouts: noCost.metrics.readouts, money: nil)
            .contains { $0.hasPrefix("費用") })
        // Rows name no cost.
        let row = BoardAccessibilityReporter.rowLabel(group, rank: 1, l10n: Self.l10n, intelligence: true)
        #expect(row.hasPrefix("第 1 名，opencdn，每秒 177 個 token，智力 135，") && !row.contains("費用") && !row.contains("美元"))
        var hidden = tick.metrics
        hidden.thinkingHidden = true
        #expect(BoardAccessibilityReporter.metricWords(hidden, currency: "美元", l10n: Self.l10n).first == "智力 123")
    }
}
