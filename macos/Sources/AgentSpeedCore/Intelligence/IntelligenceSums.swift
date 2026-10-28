import Foundation

/// The live intelligence sums of METRICS 6.9 for one set of responses and agents, per second of the live window.
/// Additive over sources, so an orb adds those of its own (6.8).
public struct IntelligenceSums: Equatable, Sendable {
    static let thinkingBonus = 20.0

    /// Output tok/s in the window (`Σ w`).
    public var output = 0.0
    /// Output weighted by the grade that produced it (`Σ w × g`).
    public var gradedOutput = 0.0
    /// Reasoning tok/s (`Σ ρ`): `liveThinking`.
    public var thinking = 0.0
    /// Output tok/s of the sources that record reasoning apart from output (`Σ w'`).
    public var thinkingBase = 0.0

    public init() {}

    public static let zero = IntelligenceSums()

    public var thinkingShare: Double { thinkingBase > 0 ? thinking / thinkingBase : 0 }

    /// The index; nil when nothing generated in the window.
    public var index: Double? {
        output > 0 ? gradedOutput / output + Self.thinkingBonus * thinkingShare : nil
    }

    /// The index as shown: rounded half away from zero.
    public var shownIndex: Int? { index.map { Int($0.rounded()) } }

    /// One contribution: `output` tokens of `source` at `grade`, `reasoning` of them reasoning.
    mutating func add(output: Double, reasoning: Double, grade: IntelligenceGrade, from source: SourceKind) {
        self.output += output
        gradedOutput += output * Double(grade.value)
        guard source.recordsReasoningApart else { return }
        thinking += reasoning
        thinkingBase += output
    }

    public mutating func add(_ other: IntelligenceSums) {
        output += other.output
        gradedOutput += other.gradedOutput
        thinking += other.thinking
        thinkingBase += other.thinkingBase
    }

    func scaled(by factor: Double) -> IntelligenceSums {
        var scaled = self
        scaled.output *= factor
        scaled.gradedOutput *= factor
        scaled.thinking *= factor
        scaled.thinkingBase *= factor
        return scaled
    }
}

extension SourceKind {
    /// DeepSeek Harness counts reasoning inside its output (METRICS 4.3): its tokens add their grade to the index but
    /// nothing to the thinking share.
    public var recordsReasoningApart: Bool { self != .deepseek }
}
