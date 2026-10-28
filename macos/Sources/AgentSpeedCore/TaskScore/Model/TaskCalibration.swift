import Foundation

/// One model's training rows: `x` with a leading 1 (row-major, `dimension` per row), labels and weights `1 / m`.
struct TaskCalibrationRows: Sendable {
    var dimension: Int
    var x: [Double] = []
    var labels: [Int] = []
    var weights: [Double] = []
    /// Tasks with at least one row.
    var tasks = 0

    var count: Int { labels.count }
}

/// Calibration of the three models on a source's records (METRICS 6.11 *Calibration*), in the reference's exact
/// operation order: sums left to right from 0, the Hessian's lower triangle as `((w·p)·(1 − p))·xᵢ·xⱼ`, Cholesky
/// column by column, forward then back substitution, exactly 25 Newton steps from the prior.
enum TaskCalibration {
    static let minTasks = 40
    static let minClassWeight = 5.0
    static let maxTasks = 2000
    static let l2 = 4.0
    static let iterations = 25

    /// The complete records ordered by `endMs`, `startMs`, agent id (UTF-16 ordinal) and `ordinal` (stable), the
    /// latest 2000.
    static func chosen(_ records: [TaskRecord]) -> [TaskRecord] {
        let complete = records.indices.filter { records[$0].complete }
        let ordered = complete.sorted { lhs, rhs in
            let a = records[lhs]
            let b = records[rhs]
            if a.endMs != b.endMs { return a.endMs < b.endMs }
            if a.startMs != b.startMs { return a.startMs < b.startMs }
            if AgentParents.ordinal(a.agentId, precedes: b.agentId) { return true }
            if AgentParents.ordinal(b.agentId, precedes: a.agentId) { return false }
            if a.ordinal != b.ordinal { return a.ordinal < b.ordinal }
            return lhs < rhs
        }
        return ordered.suffix(maxTasks).map { records[$0] }
    }

    /// `model_rows` over records already in calibration order.
    static func rows(_ records: [TaskRecord], _ name: TaskModelName) -> TaskCalibrationRows {
        var rows = TaskCalibrationRows(dimension: name.prior.count)
        for record in records {
            let count = name == .fix ? record.fixRows.count : record.snapshots.count
            guard count > 0 else { continue }
            rows.tasks += 1
            let weight = 1.0 / Double(count)
            if name == .fix {
                for row in record.fixRows { rows.append(row.features.values, label: row.yFix, weight: weight) }
            } else {
                for row in record.snapshots {
                    let features = row.features
                    if name == .success {
                        rows.append(features.successValues, label: record.success ? 1 : 0, weight: weight)
                    } else {
                        rows.append(features.stallValues, label: row.yStall, weight: weight)
                    }
                }
            }
        }
        return rows
    }

    /// The penalised log-likelihood's gradient and the lower triangle of its Hessian (row-major `d × d`).
    static func gradientAndHessian(_ beta: [Double], prior: [Double], rows: TaskCalibrationRows)
        -> (gradient: [Double], hessian: [Double]) {
        let d = beta.count
        var gradient = (0..<d).map { l2 * (beta[$0] - prior[$0]) }
        var hessian = [Double](repeating: 0.0, count: d * d)
        for i in 0..<d { hessian[i * d + i] = l2 }
        rows.x.withUnsafeBufferPointer { x in
            for row in 0..<rows.count {
                let base = row * rows.dimension
                var eta = 0.0
                for k in 0..<d { eta += beta[k] * x[base + k] }
                let p = TaskLogistic.sigmoid(eta)
                let weight = rows.weights[row]
                let slope = weight * (p - Double(rows.labels[row]))
                let curvature = weight * p * (1 - p)
                for i in 0..<d {
                    gradient[i] += slope * x[base + i]
                    for j in 0...i { hessian[i * d + j] += curvature * x[base + i] * x[base + j] }
                }
            }
        }
        return (gradient, hessian)
    }

    /// Solves `H·δ = g` with `H = L·Lᵀ` (only `H`'s lower triangle is read).
    static func choleskySolve(_ matrix: [Double], _ vector: [Double]) -> [Double] {
        let d = vector.count
        var lower = [Double](repeating: 0.0, count: d * d)
        for j in 0..<d {
            var square = 0.0
            for k in 0..<j { square += lower[j * d + k] * lower[j * d + k] }
            lower[j * d + j] = (matrix[j * d + j] - square).squareRoot()
            for i in (j + 1)..<d {
                var dot = 0.0
                for k in 0..<j { dot += lower[i * d + k] * lower[j * d + k] }
                lower[i * d + j] = (matrix[i * d + j] - dot) / lower[j * d + j]
            }
        }
        var z = [Double](repeating: 0.0, count: d)
        for i in 0..<d {
            var dot = 0.0
            for k in 0..<i { dot += lower[i * d + k] * z[k] }
            z[i] = (vector[i] - dot) / lower[i * d + i]
        }
        var x = [Double](repeating: 0.0, count: d)
        for i in (0..<d).reversed() {
            var dot = 0.0
            for k in (i + 1)..<d { dot += lower[k * d + i] * x[k] }
            x[i] = (z[i] - dot) / lower[i * d + i]
        }
        return x
    }

    static func fit(_ rows: TaskCalibrationRows, prior: [Double]) -> [Double] {
        var beta = prior
        for _ in 0..<iterations {
            let (gradient, hessian) = gradientAndHessian(beta, prior: prior, rows: rows)
            let step = choleskySolve(hessian, gradient)
            beta = zip(beta, step).map { $0 - $1 }
        }
        return beta
    }

    static func model(_ name: TaskModelName, _ chosen: [TaskRecord]) -> TaskLogisticModel {
        let rows = rows(chosen, name)
        var positive = 0.0
        var negative = 0.0
        for row in 0..<rows.count {
            if rows.labels[row] == 1 { positive += rows.weights[row] }
            if rows.labels[row] == 0 { negative += rows.weights[row] }
        }
        guard rows.tasks >= minTasks, positive >= minClassWeight, negative >= minClassWeight else {
            return .prior(name, tasks: rows.tasks)
        }
        return TaskLogisticModel(coefficients: fit(rows, prior: name.prior), calibrated: true, tasks: rows.tasks)
    }
}

extension TaskCalibrationRows {
    mutating func append(_ features: [Double], label: Int, weight: Double) {
        x.append(1.0)
        x.append(contentsOf: features)
        labels.append(label)
        weights.append(weight)
    }
}

extension TaskModels {
    /// Per model: the coefficients fitted on the latest complete records of one source, or the prior.
    public static func calibrate(_ records: [TaskRecord]) -> TaskModels {
        let chosen = TaskCalibration.chosen(records)
        return TaskModels(success: TaskCalibration.model(.success, chosen), stall: TaskCalibration.model(.stall, chosen),
                          fix: TaskCalibration.model(.fix, chosen))
    }
}
