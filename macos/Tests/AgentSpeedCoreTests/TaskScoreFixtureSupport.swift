import Foundation
import Testing
@testable import AgentSpeedCore

/// `spec/fixtures/task-score-cases.json` read with the strict parser, so integers stay integers and every number keeps
/// its literal (Python's `repr`, which reads back to the exact double).
enum TaskScoreFixture {
    static let file = "task-score-cases.json"

    static let document: JSONValue = {
        guard let data = try? Data(contentsOf: Fixtures.root.appendingPathComponent(file)),
              let document = try? JSONParser.parse(data) else { return .null }
        return document
    }()

    static func section(_ name: String) -> [JSONValue] {
        document[name]?.array ?? []
    }

    static func thinking(_ name: String) -> [JSONValue] {
        document["thinking"]?[name]?.array ?? []
    }

    // MARK: Inputs

    static func digest(_ value: JSONValue?) -> TaskDigest {
        TaskDigest(hex: value?.string ?? "") ?? TaskDigest(value: 0)
    }

    static func event(_ value: JSONValue) -> TaskEvent {
        let t = value["t"]?.int64 ?? 0
        if value["type"]?.string == "prompt" { return .prompt(t, continuation: value["continuation"]?.bool ?? false) }
        let command = value["command"].flatMap { command -> TaskCommand? in
            guard case .object = command else { return nil }
            return TaskCommand(commandClass: TaskCommandClass(rawValue: command["class"]?.string ?? "") ?? .other,
                               suite: command["suite"]?.string, targeted: command["targeted"]?.bool ?? false,
                               key: digest(command["key"]))
        }
        let outcome = value["outcome"].flatMap { outcome -> TaskOutcome? in
            guard case .object = outcome else { return nil }
            return TaskOutcome(exitCode: outcome["exitCode"]?.int64, failed: outcome["failed"]?.bool,
                               t: outcome["t"]?.int64)
        }
        let failures = value["failures"].flatMap { failures -> TaskRunFailures? in
            guard case .object = failures else { return nil }
            return TaskRunFailures(fingerprints: (failures["fingerprints"]?.array ?? []).map { digest($0) },
                                   failedCount: failures["failedCount"]?.int64,
                                   passedCount: failures["passedCount"]?.int64,
                                   framework: failures["framework"]?.string)
        }
        let hunks = value["hunks"]?.array?.map { hunk in
            TaskHunk(file: digest(hunk["file"]), old: hunk["old"]?.string.map { digest(.string($0)) },
                     new: digest(hunk["new"]))
        }
        return .action(t, TaskAction(kind: TaskActionKind(rawValue: value["kind"]?.string ?? "") ?? .other,
                                     tool: value["tool"]?.string ?? "", digest: digest(value["digest"]),
                                     command: command, outcome: outcome, failures: failures, hunks: hunks))
    }

    static func events(_ value: JSONValue?) -> [TaskEvent] {
        (value?.array ?? []).map(event)
    }

    static func double(_ value: JSONValue?) -> Double? {
        value?.literal?.double
    }

    static func doubles(_ value: JSONValue?) -> [Double] {
        (value?.array ?? []).compactMap { $0.literal?.double }
    }

    // MARK: Outputs as JSON

    static func number(_ value: Double?) -> JSONValue {
        value.map { .number(JSONLiteral("\($0)")) } ?? .null
    }

    static func number(_ value: Int?) -> JSONValue {
        value.map { .number(JSONLiteral(String($0))) } ?? .null
    }

    static func number(_ value: Int64?) -> JSONValue {
        value.map { .number(JSONLiteral(String($0))) } ?? .null
    }

    static func string(_ value: String?) -> JSONValue {
        value.map { .string($0) } ?? .null
    }

    static func object(_ members: KeyValuePairs<String, JSONValue>) -> JSONValue {
        .object(Dictionary(members.map { ($0.key, $0.value) }, uniquingKeysWith: { $1 }))
    }

    static func dims(_ dims: TaskDimensions) -> JSONValue {
        object(["V": number(dims.v), "C": number(dims.c), "R": number(dims.r), "E": number(dims.e),
                "K": number(dims.k)])
    }

    static func counts(_ counts: TaskCounts) -> JSONValue {
        .object(Dictionary(uniqueKeysWithValues: zip(TaskCounts.names, counts.values.map { number($0) })))
    }

    static func result(_ result: TaskResult) -> JSONValue {
        let openFailure = result.openFailure.map { failure in
            object(["suite": string(failure.suite), "class": .string(failure.commandClass.rawValue),
                    "key": .string(failure.key.hex), "errors": number(failure.errors), "peak": number(failure.peak),
                    "counted": .bool(failure.counted), "edited": .bool(failure.edited)])
        } ?? .null
        let lastPass = result.lastPass.map { pass in
            object(["suite": string(pass.suite), "class": .string(pass.commandClass.rawValue), "key": .string(pass.key.hex),
                    "buildOnly": .bool(pass.buildOnly)])
        } ?? .null
        return object([
            "state": .string(result.state.rawValue), "score": number(result.score), "shown": number(result.shown),
            "dims": dims(result.dims), "ig": number(result.ig), "loop": number(result.loop), "osc": number(result.osc),
            "counts": counts(result.counts), "firstPassMs": number(result.firstPassMs),
            "stallEnds": .array(result.stallEnds.map { number($0) }), "vHeld": number(result.vHeld),
            "openFailure": openFailure, "lastPass": lastPass,
        ])
    }

    static func snapshotFeatures(_ features: TaskSnapshotFeatures) -> JSONValue {
        object(["passing": number(features.passing), "failing": number(features.failing), "conv": number(features.conv),
                "ig": number(features.ig), "loop": number(features.loop), "osc": number(features.osc),
                "size": number(features.size)])
    }

    static func fixFeatures(_ features: TaskFixFeatures) -> JSONValue {
        object(["attempts": number(features.attempts), "errors": number(features.errors),
                "trend": number(features.trend), "osc": number(features.osc), "ig": number(features.ig)])
    }

    static func predictions(_ predictions: TaskPredictions?) -> JSONValue {
        guard let predictions else { return .null }
        return object([
            "success": number(predictions.success), "stall": number(predictions.stall), "fix": number(predictions.fix),
            "features": snapshotFeatures(predictions.features),
            "fixFeatures": predictions.fixFeatures.map(fixFeatures) ?? .null,
            "successShown": number(predictions.successShown), "stallShown": number(predictions.stallShown),
            "fixShown": number(predictions.fixShown),
        ])
    }

    static func row(_ values: [Double], integers: Set<Int>) -> JSONValue {
        .array(values.enumerated().map { integers.contains($0.offset) ? number(Int($0.element)) : number($0.element) })
    }

    static func record(_ record: TaskRecord) -> JSONValue {
        object([
            "ordinal": number(record.ordinal), "startMs": number(record.startMs), "endMs": number(record.endMs),
            "complete": .bool(record.complete), "model": string(record.model), "effort": string(record.effort),
            "state": .string(record.state.rawValue), "score": number(record.score), "dims": dims(record.dims),
            "counts": counts(record.counts), "firstPassMs": number(record.firstPassMs),
            "success": .bool(record.success), "stalled": .bool(record.stalled),
            "snapshots": .array(record.snapshots.map { row($0.values, integers: [0, 8]) }),
            "fixRows": .array(record.fixRows.map { row($0.values, integers: [5]) }),
        ])
    }

    static func model(_ model: TaskLogisticModel) -> JSONValue {
        object(["coefficients": .array(model.coefficients.map { number($0) }), "calibrated": .bool(model.calibrated),
                "tasks": number(model.tasks)])
    }

    static func comparisonRow(_ row: TaskComparisonRow) -> JSONValue {
        var members: [String: JSONValue] = [
            "tasks": number(row.tasks), "scoredTasks": number(row.scoredTasks), "medianScore": number(row.medianScore),
            "medianShown": number(row.medianShown), "lowerQuartile": number(row.lowerQuartile),
            "upperQuartile": number(row.upperQuartile), "histogram": .array(row.histogram.map { number($0) }),
            "successRate": number(row.successRate), "stallRate": number(row.stallRate),
            "fixFailureRate": number(row.fixFailureRate), "recoverAttempts": number(row.recoverAttempts),
            "timeToFirstPassMs": number(row.timeToFirstPassMs),
        ]
        if let source = row.source { members["source"] = .string(source.rawValue) }
        if let model = row.model {
            members["model"] = .string(model)
            members["sources"] = .array(row.sources.map { .string($0.rawValue) })
        }
        return .object(members)
    }

    /// DESIGN 2c groups as `live_thinking.board_groups`: every root with all its descendants, the root first and then
    /// depth first in list order.
    static func boardGroups(_ agents: [(id: String, parentId: String?)]) -> [(root: String, members: [String])] {
        let ids = Set(agents.map(\.id))
        var children: [String: [String]] = [:]
        var roots: [String] = []
        for agent in agents {
            if let parent = agent.parentId, ids.contains(parent), parent != agent.id {
                children[parent, default: []].append(agent.id)
            } else {
                roots.append(agent.id)
            }
        }
        return roots.map { root in
            var members: [String] = []
            var stack = [root]
            while let id = stack.popLast() {
                members.append(id)
                stack.append(contentsOf: (children[id] ?? []).reversed())
            }
            return (root, members)
        }
    }
}

/// `close` of `task_score.py`: structural equality with numbers within an absolute tolerance, counting how many
/// numbers also match bit for bit.
struct TaskTreeComparison {
    var tolerance = 1e-9
    var mismatches: [String] = []
    var numbers = 0
    var exactNumbers = 0
    var largestDifference = 0.0

    mutating func compare(_ got: JSONValue, _ want: JSONValue, _ label: String) {
        switch (got, want) {
        case (.number(let a), .number(let b)):
            numbers += 1
            guard let x = a.double, let y = b.double else {
                mismatches.append("\(label): \(a.text) vs \(b.text)")
                return
            }
            let difference = abs(x - y)
            if x.bitPattern == y.bitPattern || x == y { exactNumbers += 1 }
            largestDifference = max(largestDifference, difference)
            if !(difference <= tolerance) { mismatches.append("\(label): got \(a.text), fixture \(b.text)") }
        case (.object(let a), .object(let b)):
            if Set(a.keys) != Set(b.keys) {
                mismatches.append("\(label): keys \(a.keys.sorted()) vs \(b.keys.sorted())")
                return
            }
            for key in b.keys.sorted() { compare(a[key]!, b[key]!, "\(label).\(key)") }
        case (.array(let a), .array(let b)):
            guard a.count == b.count else {
                mismatches.append("\(label): \(a.count) items vs \(b.count)")
                return
            }
            for index in a.indices { compare(a[index], b[index], "\(label)[\(index)]") }
        case (.string(let a), .string(let b)):
            if !a.utf8.elementsEqual(b.utf8) { mismatches.append("\(label): \"\(a)\" vs \"\(b)\"") }
        default:
            if got != want { mismatches.append("\(label): got \(got), fixture \(want)") }
        }
    }

    mutating func absorb(_ other: TaskTreeComparison) {
        mismatches += other.mismatches
        numbers += other.numbers
        exactNumbers += other.exactNumbers
        largestDifference = max(largestDifference, other.largestDifference)
    }
}
