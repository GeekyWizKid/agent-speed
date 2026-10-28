import Foundation

/// The live metrics a board shows (DESIGN 2c Data model `readouts`, Live metrics *Switches*): four independent
/// switches, all on for a new board and for a board saved by an earlier version. Decoding is tolerant: a missing key,
/// or a value that is not a boolean, reads as on.
public struct BoardReadouts: Equatable, Hashable, Sendable {
    /// 智力 in the sub-gauge, and the rows' 智力 column with its captions line.
    public var intelligence: Bool
    /// 思考 in the sub-gauge.
    public var thinking: Bool
    /// 費用 in the sub-gauge.
    public var cost: Bool
    /// The group rows' second line (M and L).
    public var rowCost: Bool

    public init(intelligence: Bool = true, thinking: Bool = true, cost: Bool = true, rowCost: Bool = true) {
        self.intelligence = intelligence
        self.thinking = thinking
        self.cost = cost
        self.rowCost = rowCost
    }

    /// Every switch on: a new board.
    public static let all = BoardReadouts()
    /// Every switch off: the board as it was before the live metrics.
    public static let none = BoardReadouts(intelligence: false, thinking: false, cost: false, rowCost: false)

    /// The hero has the sub-gauge (and glass and timing draw their history as a strip).
    public var hero: Bool { intelligence || thinking || cost }

    /// The group rows draw their second line at `size` (never at S).
    public func twoLineRows(_ size: BoardSize) -> Bool { rowCost && size != .small }

    /// 思考 may hide at zero: it is on with another hero readout (alone it never hides).
    public var thinkingMayHide: Bool { thinking && (intelligence || cost) }

    public subscript(_ readout: BoardReadout) -> Bool {
        get {
            switch readout {
            case .intelligence: intelligence
            case .thinking: thinking
            case .cost: cost
            case .rowCost: rowCost
            }
        }
        set {
            switch readout {
            case .intelligence: intelligence = newValue
            case .thinking: thinking = newValue
            case .cost: cost = newValue
            case .rowCost: rowCost = newValue
            }
        }
    }
}

/// One switch of `BoardReadouts`, in the menu's and editor's order.
public enum BoardReadout: String, CaseIterable, Sendable, Identifiable {
    case intelligence, thinking, cost, rowCost

    public var id: String { rawValue }

    /// `boards.readouts.intel`, `.thinking`, `.cost`, `.rowCost`.
    public var titleKey: String {
        switch self {
        case .intelligence: "boards.readouts.intel"
        case .thinking: "boards.readouts.thinking"
        case .cost: "boards.readouts.cost"
        case .rowCost: "boards.readouts.rowCost"
        }
    }
}

extension BoardReadouts: Codable {
    enum CodingKeys: String, CodingKey {
        case intelligence, thinking, cost, rowCost
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        func flag(_ key: CodingKeys) -> Bool {
            ((try? container.decodeIfPresent(Bool.self, forKey: key)) ?? nil) ?? true
        }
        self.init(intelligence: flag(.intelligence), thinking: flag(.thinking), cost: flag(.cost), rowCost: flag(.rowCost))
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(intelligence, forKey: .intelligence)
        try container.encode(thinking, forKey: .thinking)
        try container.encode(cost, forKey: .cost)
        try container.encode(rowCost, forKey: .rowCost)
    }
}
