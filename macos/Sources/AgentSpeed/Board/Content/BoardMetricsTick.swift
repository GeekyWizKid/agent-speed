import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation

/// A cost per second as a board draws it (DESIGN 2c Live metrics, *Values*; DESIGN 9 rate form): the number, its
/// unit, `≥` when part is unpriced, `–` when nothing is priced, the subscription tag when every source is excluded.
struct BoardMoney: Hashable {
    enum State: Hashable {
        case value
        /// Nothing priced: `–`, no unit.
        case unknown
        /// Every source excluded as a subscription: `cost.subscription.tag`, no unit.
        case tag
    }

    var number: String
    /// `US$/s` (empty for `–` and the tag).
    var unit: String
    /// A small `≥` precedes the number.
    var atLeast = false
    var state = State.value

    static let zero = BoardMoney(number: "0", unit: "")

    /// `–`, a real `0` and the tag are drawn quietly (ink-3, no glow).
    var isQuiet: Bool { state != .value || number == "0" }
}

/// Formats board costs in the display currency (METRICS 9.8) for the UI language.
struct BoardMoneyFormat {
    let currency: Currency
    /// Units of the display currency per US dollar.
    let rate: Decimal
    let language: String
    /// `cost.unitPerSecond` with the symbol.
    let unit: String
    /// `cost.subscription.tag`.
    let tag: String

    @MainActor
    init(currency: Currency, rate: Decimal, l10n: Localizer) {
        self.currency = currency
        self.rate = rate
        language = l10n.language.rawValue
        unit = l10n("cost.unitPerSecond", currency.symbol(language: l10n.language.rawValue))
        tag = l10n("cost.subscription.tag")
    }

    /// The rate of `microUsdPerSecond` in `state` (METRICS 9.5): a partial value gets `≥` unless it shows 0.
    func money(_ microUsdPerSecond: Double, state: CostReading.State) -> BoardMoney {
        switch state {
        case .subscriptionOnly:
            return BoardMoney(number: tag, unit: "", state: .tag)
        case .nothingPriced:
            return BoardMoney(number: "–", unit: "", state: .unknown)
        case .priced, .partial:
            let number = MoneyFormat.format(MoneyFormat.rate(microUsdPerSecond: max(0, microUsdPerSecond), rate: rate),
                                            currency: currency, language: language, form: .rate).number
            return BoardMoney(number: number, unit: unit, atLeast: state == .partial && number != "0")
        }
    }

    func money(_ reading: CostReading) -> BoardMoney {
        money(reading.microUsdPerSecond, state: reading.state)
    }
}

/// The live metrics of one tick (DESIGN 2c Live metrics): the switches and the hero's values, formatted. The rows'
/// 智力 comes from the groups themselves; nothing on a board shows a cost per group.
struct BoardMetricsTick: Equatable {
    var readouts = BoardReadouts.none
    /// 智力 as shown, rounded half away from zero; nil while there is no value (`–`).
    var intelligence: Int?
    /// 思考 (METRICS 7), `–` while there is no value.
    var thinking = "–"
    /// 思考 hidden at zero (after its 3 s hold).
    var thinkingHidden = false
    var cost = BoardMoney.zero
    /// The display currency's `cost.unitPerSecond` (the table measures its unit cell with it, whatever 費用 shows).
    var costUnit = ""

    /// Σ w = 0 over the board's sources: 智力 and 思考 read `–`.
    var quiet: Bool { intelligence == nil }
    /// 思考 shows in the hero (on and not hidden).
    var showsThinking: Bool { readouts.thinking && !thinkingHidden }

    /// Formats the hero's values from the board's sums and cost reading.
    static func make(readouts: BoardReadouts, intelligence sums: IntelligenceSums, cost: BoardMoney, costUnit: String,
                     thinkingHidden: Bool) -> BoardMetricsTick {
        let shown = sums.shownIndex
        return BoardMetricsTick(readouts: readouts, intelligence: shown,
                                thinking: shown == nil ? "–" : SpeedFormat.speed(sums.thinking),
                                thinkingHidden: thinkingHidden && readouts.thinkingMayHide, cost: cost, costUnit: costUnit)
    }
}
