import Foundation

/// A model's class by the base output price of its price-table entry (METRICS 6.9); the value is its grade at effort
/// level 0. Tiers, periods, off-peak and fast prices are not used: the class is the model's, not the request's.
public enum ModelClass: Int, CaseIterable, Sendable {
    case light = 60
    case standard = 80
    case flagship = 100

    /// Base output prices at which a class starts, in micro-USD per million tokens.
    static let flagshipOutput: Int64 = 15_000_000
    static let standardOutput: Int64 = 3_000_000

    /// A free entry or provider is light; an unknown model is standard.
    init(_ resolution: ModelResolution, in table: PriceTable) {
        switch resolution {
        case .free:
            self = .light
        case .unknown:
            self = .standard
        case .priced(let index, _):
            let output = table.models[index].pricing?.base.prices.output ?? 0
            self = output >= Self.flagshipOutput ? .flagship : output >= Self.standardOutput ? .standard : .light
        }
    }
}

/// How strong a model at an effort is (METRICS 6.9): `class + 6 × level`, 60 – 136.
public struct IntelligenceGrade: Equatable, Sendable {
    static let levelStep = 6
    /// Medium, the providers' common default: an unknown effort, or a token without a level.
    static let defaultLevel = 2

    public var modelClass: ModelClass
    /// The effort's meter level (EXECUTION-MODES), plus one for a top rung.
    public var level: Int

    public var value: Int { modelClass.rawValue + Self.levelStep * level }

    public init(modelClass: ModelClass, level: Int) {
        self.modelClass = modelClass
        self.level = level
    }

    /// `effort` is a normalized effort token, nil when unknown.
    init(modelClass: ModelClass, effort: String?) {
        let level = effort.flatMap { token in ModeDisplay.effortLevel(token).map { $0 + (ModeDisplay.isTopRung(token) ? 1 : 0) } }
        self.init(modelClass: modelClass, level: level ?? Self.defaultLevel)
    }

    /// An unknown model at an unknown effort.
    public static let unknown = IntelligenceGrade(modelClass: .standard, level: defaultLevel)
}

/// Grades responses and agents against the current price table (METRICS 6.9): a response by its own model, provider
/// and effort, an agent by its current model and effort with the provider of its latest response.
struct IntelligenceGrader {
    let table: PriceTable
    let agentProviders: [String: String]

    func grade(model: String?, provider: String?, effort: String?) -> IntelligenceGrade {
        let resolution = table.resolver.resolve(model: model, provider: provider)
        return IntelligenceGrade(modelClass: ModelClass(resolution, in: table), effort: effort)
    }

    func grade(of record: ResponseRecord) -> IntelligenceGrade {
        grade(model: record.model, provider: record.provider, effort: record.modes.effortToken)
    }

    /// The weight of the agent's in-flight estimate: an unknown model weighs as the standard class.
    func grade(of agent: EngineAgent) -> IntelligenceGrade {
        grade(model: agent.model, provider: agentProviders[agent.id], effort: agent.modes.effortToken)
    }

    /// The agent's own grade, nil while its current model is unknown (METRICS 6.9, Agent intelligence).
    func ownGrade(of agent: EngineAgent) -> IntelligenceGrade? {
        agent.model?.isEmpty == false ? grade(of: agent) : nil
    }
}

private extension ExecutionModes {
    /// The attributed effort token, nil when unknown.
    var effortToken: String? {
        let effort = attribution(for: .effort)
        return effort.isUnknown ? nil : effort.mode
    }
}
