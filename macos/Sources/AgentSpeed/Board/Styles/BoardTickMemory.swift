import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation

/// What the previous tick showed, to tell this tick's accents (DESIGN 2c Motion): digits that roll, values that
/// flash, ranks that roll, passes and new peaks. A tick laid out again (a board that comes back into view) is not
/// fresh and plays nothing.
struct BoardTickMemory {
    private var signature: Int?
    /// The tick being laid out is new.
    private(set) var fresh = false
    private(set) var number: String?
    private(set) var value: Double?
    private var values: [String: Double] = [:]
    private var ranks: [String: Int] = [:]

    mutating func begin(_ tick: BoardTick) {
        var hasher = Hasher()
        hasher.combine(tick.valueText)
        hasher.combine(tick.value)
        hasher.combine(tick.history)
        hasher.combine(tick.rows.map(\.key))
        hasher.combine(tick.events.map { "\($0.kind.rawValue)\($0.id)\($0.from)\($0.to)" })
        hasher.combine(tick.newPeak)
        let signature = hasher.finalize()
        fresh = self.signature != nil && signature != self.signature
        self.signature = signature
    }

    /// The row's value change since the last fresh tick.
    func change(_ key: String, live: Double) -> BoardValueChange {
        fresh ? BoardValueChange(from: values[key], to: live) : .steady
    }

    /// The group's previous rank when it moved.
    func rankChange(_ id: String, rank: Int) -> Int? {
        guard fresh, let old = ranks[id], old != rank else { return nil }
        return old
    }

    /// The pass of this tick that moved `id`, if any.
    func event(_ tick: BoardTick, for id: String) -> BoardRankEvent? {
        fresh ? tick.events.first { $0.id == id } : nil
    }

    mutating func end(_ tick: BoardTick, rows: [(key: String, live: Double)]) {
        guard fresh || number == nil else { return }
        number = tick.valueText
        value = tick.value
        values = Dictionary(rows.map { ($0.key, $0.live) }, uniquingKeysWith: { a, _ in a })
        ranks = Dictionary(tick.groups.enumerated().map { ($1.id, $0 + 1) }, uniquingKeysWith: { a, _ in a })
    }
}
