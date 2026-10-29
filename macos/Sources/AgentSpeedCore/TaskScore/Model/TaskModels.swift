import Foundation

/// The three logistic models of METRICS 6.11 *Predictions*.
public enum TaskModelName: String, CaseIterable, Sendable {
    case success, stall, fix

    /// The prior coefficients, intercept first.
    public var prior: [Double] {
        switch self {
        case .success: [-1.5, 3.0, -0.5, 1.0, 1.0, -2.0, -1.5, -0.5]
        case .stall: [-1.0, -2.0, 3.0, 2.0, 0.8, 0.5]
        case .fix: [-0.5, 1.5, 1.0, 1.0, 1.5, -1.0]
        }
    }

    /// The feature names, in coefficient order.
    public var features: [String] {
        switch self {
        case .success: ["passing", "failing", "conv", "ig", "loop", "osc", "size"]
        case .stall: ["ig", "loop", "osc", "failing", "size"]
        case .fix: ["attempts", "errors", "trend", "osc", "ig"]
        }
    }
}

/// One model's coefficients (intercept first), whether they were calibrated, and on how many tasks.
public struct TaskLogisticModel: Equatable, Sendable {
    public var coefficients: [Double]
    public var calibrated: Bool
    public var tasks: Int

    public init(coefficients: [Double], calibrated: Bool, tasks: Int) {
        self.coefficients = coefficients
        self.calibrated = calibrated
        self.tasks = tasks
    }

    public static func prior(_ name: TaskModelName, tasks: Int = 0) -> TaskLogisticModel {
        TaskLogisticModel(coefficients: name.prior, calibrated: false, tasks: tasks)
    }

    /// `σ(β₀ + Σ βᵢ·xᵢ)` over features in coefficient order.
    public func probability(_ features: [Double]) -> Double {
        TaskLogistic.sigmoid(TaskLogistic.linear(coefficients, features))
    }
}

/// The coefficients of one source's three models.
public struct TaskModels: Equatable, Sendable {
    public var success: TaskLogisticModel
    public var stall: TaskLogisticModel
    public var fix: TaskLogisticModel

    public init(success: TaskLogisticModel, stall: TaskLogisticModel, fix: TaskLogisticModel) {
        self.success = success
        self.stall = stall
        self.fix = fix
    }

    public static let prior = TaskModels(success: .prior(.success), stall: .prior(.stall), fix: .prior(.fix))

    public subscript(name: TaskModelName) -> TaskLogisticModel {
        get {
            switch name {
            case .success: success
            case .stall: stall
            case .fix: fix
            }
        }
        set {
            switch name {
            case .success: success = newValue
            case .stall: stall = newValue
            case .fix: fix = newValue
            }
        }
    }
}

/// The logistic function and linear predictor in the reference's operation order.
enum TaskLogistic {
    static let etaLimit = 35.0

    /// `σ(η) = 1 / (1 + e^(−η))` with `η` clamped to [−35, 35] as Python's `max(-35, min(35, η))`.
    static func sigmoid(_ eta: Double) -> Double {
        let upper = eta < etaLimit ? eta : etaLimit
        let clamped = upper > -etaLimit ? upper : -etaLimit
        return 1.0 / (1.0 + exp(-clamped))
    }

    /// `c₀ + sum(cᵢ·xᵢ)`, the sum taken left to right from 0 and as long as both lists last (Python `zip`).
    static func linear(_ coefficients: [Double], _ features: [Double]) -> Double {
        guard let intercept = coefficients.first else { return 0 }
        var sum = 0.0
        for index in 0..<min(coefficients.count - 1, features.count) {
            sum += coefficients[index + 1] * features[index]
        }
        return intercept + sum
    }
}
