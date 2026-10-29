import Foundation
import Testing
@testable import AgentSpeedCore

/// Every section of `spec/fixtures/task-score-cases.json` but `thinking` (METRICS 6.9 and 6.11), compared with the
/// reference's output as `spec/tools/task_score.py` compares it: same structure, numbers within 1e-9.
@Suite("Task score fixture cases")
struct TaskScoreCaseTests {
    typealias F = TaskScoreFixture

    /// One section's comparison and its case count.
    struct Outcome {
        var cases = 0
        var tree = TaskTreeComparison()
    }

    static func expectClean(_ outcome: Outcome, _ section: String, minimum: Int,
                            sourceLocation: SourceLocation = #_sourceLocation) {
        #expect(outcome.cases >= minimum, "\(section): \(outcome.cases) cases", sourceLocation: sourceLocation)
        #expect(outcome.tree.mismatches.isEmpty,
                "\(section): \(outcome.tree.mismatches.count) differences\n\(outcome.tree.mismatches.prefix(30).joined(separator: "\n"))",
                sourceLocation: sourceLocation)
    }

    // MARK: Sections

    static func continuations() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("continuations") {
            outcome.cases += 1
            let text = spec["text"]?.string ?? ""
            outcome.tree.compare(.bool(TaskPrompts.isContinuation(text)), spec["expect"] ?? .null, "continuation \(text.debugDescription)")
        }
        return outcome
    }

    static func promptLines() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("promptLines") {
            outcome.cases += 1
            let name = spec["name"]?.string ?? "?"
            guard let source = SourceKind(rawValue: spec["source"]?.string ?? "") else {
                outcome.tree.mismatches.append("\(name): unknown source")
                continue
            }
            var got = JSONValue.null
            if let line = TaskPrompts.line(source, spec["line"] ?? .null) {
                var members: [String: JSONValue] = ["kind": .string(line.kind.rawValue)]
                if line.kind == .prompt { members["continuation"] = .bool(line.continuation) }
                if let t = line.t { members["t"] = F.number(t) }
                got = .object(members)
            }
            outcome.tree.compare(got, spec["expect"] ?? .null, name)
        }
        return outcome
    }

    static func segmentation() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("segmentation") {
            outcome.cases += 1
            let lines = (spec["lines"]?.array ?? []).map { line -> TaskClassifiedLine in
                let kind: TaskClassifiedLine.Kind = switch line["line"]?.string {
                case "prompt": .prompt
                case "command": .command
                case "commandOutput": .commandOutput
                default: .assistant
                }
                return TaskClassifiedLine(kind: kind, t: line["t"]?.int64 ?? 0,
                                          continuation: line["continuation"]?.bool ?? false,
                                          duplicate: line["duplicate"]?.bool ?? false)
            }
            let got = TaskPrompts.segment(lines).map { event -> JSONValue in
                guard case .prompt(let continuation) = event.body else { return .null }
                return F.object(["t": F.number(event.t), "type": .string("prompt"), "continuation": .bool(continuation)])
            }
            outcome.tree.compare(.array(got), spec["expect"] ?? .null, spec["name"]?.string ?? "?")
        }
        return outcome
    }

    static func tasks() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("tasks") {
            let name = spec["name"]?.string ?? "?"
            let events = F.events(spec["events"])
            for query in spec["queries"]?.array ?? [] {
                outcome.cases += 1
                let now = query["nowMs"]?.int64
                guard let task = TaskSplit.current(events, now: now) else {
                    outcome.tree.mismatches.append("\(name): no current task")
                    continue
                }
                let result = TaskResult(TaskEvaluation(task.events, now: now))
                outcome.tree.compare(F.result(result), query["expect"] ?? .null, "\(name) @ \(now.map(String.init) ?? "end")")
            }
        }
        return outcome
    }

    static func values() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("values") {
            outcome.cases += 1
            let name = spec["name"]?.string ?? "?"
            let now = spec["nowMs"]?.int64
            let agents = spec["agents"]?.array ?? []
            var values: [String: TaskAgentValue] = [:]
            var members: [String: (id: String, lastLineMs: Int64?, value: TaskAgentValue)] = [:]
            var agentJSON: [String: JSONValue] = [:]
            for agent in agents {
                let id = agent["id"]?.string ?? ""
                let value = TaskAgentValue.agentValue(events: F.events(agent["events"]),
                                                      idle: agent["phase"]?.string == "idle", now: now)
                values[id] = value
                members[id] = (id, agent["lastLineMs"]?.int64, value)
                agentJSON[id] = F.object(["form": .string(value.form.rawValue), "score": F.number(value.score),
                                          "shown": F.number(value.shown), "state": F.string(value.state?.rawValue)])
            }
            func setJSON(_ value: TaskSetValue) -> [String: JSONValue] {
                ["form": .string(value.form.rawValue), "score": F.number(value.score), "shown": F.number(value.shown),
                 "agent": F.string(value.agentId)]
            }
            let order = agents.map { $0["id"]?.string ?? "" }
            var groupJSON: [String: JSONValue] = [:]
            let groups = F.boardGroups(agents.map { ($0["id"]?.string ?? "", $0["parentId"]?.string) })
            for group in groups {
                var members = setJSON(TaskSetValue.highest(group.members.compactMap { members[$0] }))
                members["members"] = .array(group.members.map { .string($0) })
                groupJSON[group.root] = .object(members)
            }
            let all = TaskSetValue.highest(order.compactMap { members[$0] })
            let got = F.object(["agents": .object(agentJSON), "groups": .object(groupJSON), "all": .object(setJSON(all))])
            outcome.tree.compare(got, spec["expect"] ?? .null, name)
            // Merging per-agent values in any grouping gives the same set value.
            let singles = order.compactMap { members[$0] }.map { TaskSetValue(id: $0.id, lastLineMs: $0.lastLineMs, value: $0.value) }
            let leftFold = singles.reduce(TaskSetValue.empty) { $0.merged(with: $1) }
            let rightFold = singles.reversed().reduce(TaskSetValue.empty) { $1.merged(with: $0) }
            let halves = singles.count / 2
            let split = singles[..<halves].reduce(TaskSetValue.empty) { $0.merged(with: $1) }
                .merged(with: singles[halves...].reduce(TaskSetValue.empty) { $0.merged(with: $1) })
            if leftFold != all || rightFold != all || split != all {
                outcome.tree.mismatches.append("\(name): merged values differ from highest")
            }
        }
        return outcome
    }

    static func models(_ value: JSONValue?) -> TaskModels {
        guard case .object = value else { return .prior }
        var models = TaskModels.prior
        for name in TaskModelName.allCases {
            guard let model = value?[name.rawValue] else { continue }
            models[name] = TaskLogisticModel(coefficients: F.doubles(model["coefficients"]),
                                             calibrated: model["calibrated"]?.bool ?? false,
                                             tasks: Int(model["tasks"]?.int64 ?? 0))
        }
        return models
    }

    static func predictions() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("predictions") {
            outcome.cases += 1
            let name = spec["name"]?.string ?? "?"
            let events = F.events(spec["events"])
            let now = spec["nowMs"]?.int64
            let models = models(spec["models"])
            let got = TaskPredictions.predictions(events: events, now: now, models: models)
            outcome.tree.compare(F.predictions(got), spec["expect"] ?? .null, name)
            let fromTask = TaskSplit.current(events, now: now).flatMap {
                TaskPredictions.predictions(taskEvents: $0.events, now: now, models: models)
            }
            if fromTask != got { outcome.tree.mismatches.append("\(name): the current task's events predict otherwise") }
        }
        return outcome
    }

    static func records() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("records") {
            outcome.cases += 1
            let notes = (spec["models"]?.array ?? []).map { note in
                TaskModelNote(t: note.array?.first?.int64 ?? 0, model: note.array?[1].string, effort: note.array?[2].string)
            }
            let source = SourceKind(rawValue: spec["source"]?.string ?? "") ?? .codex
            let got = TaskRecord.records(events: F.events(spec["events"]), analysisMs: spec["analysisMs"]?.int64 ?? 0,
                                         lastLineMs: spec["lastLineMs"]?.int64 ?? 0, models: notes, agentId: "agent",
                                         source: source, day: { _ in "" })
            outcome.tree.compare(.array(got.map(F.record)), spec["expect"] ?? .null, spec["name"]?.string ?? "?")
        }
        return outcome
    }

    static func calibrationRecords(_ items: [JSONValue], source: SourceKind) -> [TaskRecord] {
        items.map { item in
            TaskRecord(agentId: item["agentId"]?.string ?? "", source: source, day: "",
                       ordinal: Int(item["ordinal"]?.int64 ?? 0), startMs: item["startMs"]?.int64 ?? 0,
                       endMs: item["endMs"]?.int64 ?? 0, complete: item["complete"]?.bool ?? false,
                       success: item["success"]?.bool ?? false,
                       snapshots: (item["snapshots"]?.array ?? []).compactMap { TaskSnapshotRow(values: F.doubles($0)) },
                       fixRows: (item["fixRows"]?.array ?? []).compactMap { TaskFixRow(values: F.doubles($0)) })
        }
    }

    /// Coefficients and probes against the fixture, and independently that the gradient vanishes (below 1e-9) at the
    /// fixture's calibrated coefficients.
    static func calibration() -> (outcome: Outcome, stationary: Int) {
        var outcome = Outcome()
        var stationary = 0
        for spec in F.section("calibration") {
            outcome.cases += 1
            let name = spec["name"]?.string ?? "?"
            guard case .object(let bySource) = spec["records"] ?? .null else { continue }
            var expect: [String: JSONValue] = [:]
            for key in bySource.keys.sorted() {
                let source = SourceKind(rawValue: key) ?? .codex
                let records = calibrationRecords(bySource[key]?.array ?? [], source: source)
                let models = TaskModels.calibrate(records)
                var modelJSON: [String: JSONValue] = [:]
                for model in TaskModelName.allCases { modelJSON[model.rawValue] = F.model(models[model]) }
                let probes = (spec["probes"]?.array ?? []).map { probe -> JSONValue in
                    let model = TaskModelName(rawValue: probe["model"]?.string ?? "") ?? .success
                    let features = model.features.map { F.double(probe["features"]?[$0]) ?? 0 }
                    let p = models[model].probability(features)
                    return F.object(["model": .string(model.rawValue), "p": F.number(p),
                                     "shown": F.number(taskRoundHalfAway(p * 100))])
                }
                expect[key] = F.object(["models": .object(modelJSON), "probes": .array(probes)])
                let chosen = TaskCalibration.chosen(records)
                for model in TaskModelName.allCases {
                    guard let stored = spec["expect"]?[key]?["models"]?[model.rawValue],
                          stored["calibrated"]?.bool == true else { continue }
                    let rows = TaskCalibration.rows(chosen, model)
                    let gradient = TaskCalibration.gradientAndHessian(F.doubles(stored["coefficients"]),
                                                                      prior: model.prior, rows: rows).gradient
                    let largest = gradient.map(abs).max() ?? 0
                    stationary += 1
                    if !(largest <= 1e-9) {
                        outcome.tree.mismatches.append("\(name) \(key) \(model): gradient \(gradient) does not vanish")
                    }
                }
            }
            outcome.tree.compare(.object(expect), spec["expect"] ?? .null, name)
        }
        return (outcome, stationary)
    }

    static func comparison() -> Outcome {
        var outcome = Outcome()
        for spec in F.section("comparison") {
            outcome.cases += 1
            let records = (spec["records"]?.array ?? []).map { item in
                let counts = item["counts"]
                return TaskRecord(
                    agentId: "", source: SourceKind(rawValue: item["source"]?.string ?? "") ?? .codex,
                    day: item["day"]?.string ?? "", ordinal: 0, startMs: item["startMs"]?.int64 ?? 0, endMs: 0,
                    complete: item["complete"]?.bool ?? false, model: item["model"]?.string,
                    score: F.double(item["score"]),
                    counts: TaskCounts(recovered: Int(counts?["recovered"]?.int64 ?? 0),
                                       recoverAttempts: Int(counts?["recoverAttempts"]?.int64 ?? 0),
                                       fixAttempts: Int(counts?["fixAttempts"]?.int64 ?? 0),
                                       fixFailures: Int(counts?["fixFailures"]?.int64 ?? 0)),
                    firstPassMs: item["firstPassMs"]?.int64, success: item["success"]?.bool ?? false,
                    stalled: item["stalled"]?.bool ?? false)
            }
            let source = spec["source"]?.string.flatMap(SourceKind.init(rawValue:))
            let got = TaskComparison.compare(records, first: spec["first"]?.string ?? "", last: spec["last"]?.string ?? "",
                                             source: source)
            let json = F.object(["bySource": .array(got.bySource.map(F.comparisonRow)),
                                 "byModel": .array(got.byModel.map(F.comparisonRow))])
            outcome.tree.compare(json, spec["expect"] ?? .null, spec["name"]?.string ?? "?")
        }
        return outcome
    }

    // MARK: Tests

    @Test func continuationCases() { Self.expectClean(Self.continuations(), "continuations", minimum: 32) }
    @Test func promptLineCases() { Self.expectClean(Self.promptLines(), "promptLines", minimum: 44) }
    @Test func segmentationCases() { Self.expectClean(Self.segmentation(), "segmentation", minimum: 6) }
    @Test func taskCases() { Self.expectClean(Self.tasks(), "tasks", minimum: 35) }
    @Test func valueCases() { Self.expectClean(Self.values(), "values", minimum: 4) }
    @Test func predictionCases() { Self.expectClean(Self.predictions(), "predictions", minimum: 5) }
    @Test func recordCases() { Self.expectClean(Self.records(), "records", minimum: 2) }

    @Test func calibrationCases() {
        let (outcome, stationary) = Self.calibration()
        Self.expectClean(outcome, "calibration", minimum: 2)
        #expect(stationary >= 4, "\(stationary) calibrated models checked for a vanishing gradient")
    }

    @Test func comparisonCases() { Self.expectClean(Self.comparison(), "comparison", minimum: 5) }

    /// Every section at once, with the counts and how many numbers match the reference bit for bit.
    @Test func summary() {
        let calibration = Self.calibration()
        let sections: [(String, Outcome)] = [
            ("continuations", Self.continuations()), ("promptLines", Self.promptLines()),
            ("segmentation", Self.segmentation()), ("taskQueries", Self.tasks()), ("values", Self.values()),
            ("predictions", Self.predictions()), ("records", Self.records()), ("calibration", calibration.outcome),
            ("comparison", Self.comparison()),
        ]
        var total = TaskTreeComparison()
        for (_, outcome) in sections { total.absorb(outcome.tree) }
        let counts = sections.map { "\($0.1.cases) \($0.0)" }.joined(separator: ", ")
        print("task score fixture: \(counts); \(calibration.stationary) stationary models; \(total.numbers) numbers, "
              + "\(total.exactNumbers) bit-exact, largest difference \(total.largestDifference)")
        #expect(total.mismatches.isEmpty)
        #expect(total.numbers > 0)
    }
}
