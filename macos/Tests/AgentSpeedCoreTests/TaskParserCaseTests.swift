import Foundation
import Testing
@testable import AgentSpeedCore

/// `spec/fixtures/task-parser-cases.json` (TASK-PARSERS 9): every case of every group, compared exactly with the
/// reference's output.
@Suite("Task parser fixture cases")
struct TaskParserCaseTests {
    static let file = "task-parser-cases.json"

    static let cases: [[String: JSONValue]] = {
        guard let data = try? Data(contentsOf: Fixtures.root.appendingPathComponent(file)),
              let document = try? JSONParser.parse(data),
              let cases = document["cases"]?.array else { return [] }
        return cases.compactMap(\.object)
    }()

    @Test func theFixtureHasEveryGroup() {
        let groups = Dictionary(grouping: Self.cases) { $0["group"]?.string ?? "" }.mapValues(\.count)
        #expect(Self.cases.count >= 602)
        for group in ["actionKind", "command", "outcome", "resultText", "session", "notification", "codeScript",
                      "codeItems", "maskedStatus", "edits", "failures"] {
            #expect((groups[group] ?? 0) > 0, "\(group)")
        }
    }

    @Test func everyCaseMatchesTheReference() {
        var mismatches: [String] = []
        var counts: [String: Int] = [:]
        for spec in Self.cases {
            let id = spec["id"]?.string ?? "?"
            let group = spec["group"]?.string ?? ""
            counts[group, default: 0] += 1
            let got = Self.compute(spec)
            let expected = spec["expect"] ?? .null
            if !Self.same(got, expected) {
                mismatches.append("\(id): expected \(Self.render(expected)) got \(Self.render(got))")
            }
        }
        let report = "\(mismatches.count) of \(Self.cases.count) cases differ:\n"
            + mismatches.prefix(40).joined(separator: "\n")
        #expect(mismatches.isEmpty, Comment(rawValue: report))
        print("task parser cases: \(Self.cases.count) (\(counts.sorted { $0.key < $1.key }.map { "\($0.key) \($0.value)" }.joined(separator: ", "))), \(mismatches.count) differ")
    }

    /// Microseconds per call of each function over the fixture corpus (a rough guide; build with `-c release` for
    /// representative numbers).
    @Test func benchmark() {
        let rounds = 3
        var totals: [String: (Duration, Int)] = [:]
        let clock = ContinuousClock()
        for _ in 0..<rounds {
            for spec in Self.cases {
                let group = spec["group"]?.string ?? ""
                let elapsed = clock.measure { _ = Self.compute(spec) }
                let (time, calls) = totals[group] ?? (.zero, 0)
                totals[group] = (time + elapsed, calls + 1)
            }
        }
        let all = totals.values.reduce((Duration.zero, 0)) { ($0.0 + $1.0, $0.1 + $1.1) }
        func micro(_ value: (Duration, Int)) -> String {
            let seconds = Double(value.0.components.seconds) + Double(value.0.components.attoseconds) * 1e-18
            return String(format: "%.1f", seconds * 1e6 / Double(max(value.1, 1)))
        }
        let lines = totals.sorted { $0.key < $1.key }.map { "\($0.key) \(micro($0.value)) µs" }
        print("task parser µs per call: all \(micro(all)) µs; " + lines.joined(separator: ", "))
        #expect(all.1 == rounds * Self.cases.count)
    }

    // MARK: Running a case

    static func source(_ spec: [String: JSONValue]) -> SourceKind? {
        spec["source"]?.string.flatMap(SourceKind.init(rawValue:))
    }

    static func compute(_ spec: [String: JSONValue]) -> JSONValue {
        let cwd = spec["cwd"]?.string
        let tool = spec["tool"]?.string
        let input = spec["input"]
        switch spec["group"]?.string {
        case "actionKind":
            guard let source = source(spec) else { return .string("other") }
            return .string(TaskParsers.actionKind(source, tool: tool, input: input, cwd: cwd).rawValue)
        case "command":
            return json(TaskParsers.commandInfo(spec["text"]?.string ?? "", cwd: cwd))
        case "outcome":
            let t = spec["t"]?.int64
            guard let source = source(spec) else { return json(TaskOutcome(exitCode: nil, failed: nil, t: t)) }
            return json(TaskParsers.outcome(source, result: spec["result"], t: t, tool: tool, input: input, cwd: cwd))
        case "resultText":
            guard let source = source(spec) else { return .string("") }
            return .string(TaskParsers.resultText(source, result: spec["result"]))
        case "session":
            guard let source = source(spec) else { return .object(["continuation": .null, "running": .null]) }
            let running = spec["result"] != nil ? TaskParsers.runningSession(source, result: spec["result"]) : nil
            return .object(["continuation": optional(TaskParsers.continuation(source, tool: tool, input: input)),
                            "running": optional(running)])
        case "notification":
            guard let source = source(spec), let notice = TaskParsers.notification(source, line: spec["line"]) else {
                return .null
            }
            return .object(["session": .string(notice.session), "exitCode": number(notice.exitCode)])
        case "codeScript":
            guard let source = source(spec), let script = TaskParsers.codeScript(source, tool: tool, input: input) else {
                return .null
            }
            return .object([
                "calls": .array(script.calls.map {
                    .object(["tool": .string($0.tool), "input": $0.input, "digest": .string($0.digest)])
                }),
                "plain": .bool(script.plain),
                "prints": .array(script.prints.map { .array([number(Int64($0.call)), .string($0.mode.rawValue)]) }),
            ])
        case "codeItems":
            guard let source = source(spec), let items = TaskParsers.codeItems(source, result: spec["result"]) else {
                return .null
            }
            return .array(items.map(optional))
        case "maskedStatus":
            guard let status = TaskParsers.maskedStatus(spec["text"]?.string ?? "", output: spec["output"]?.string ?? "",
                                                        cwd: cwd) else { return .null }
            return .object(["exitCode": optional(status.exitCode), "failed": .bool(status.failed)])
        case "failures":
            let info = TaskParsers.commandInfo(spec["command"]?.string ?? "", cwd: cwd)
            let (result, canonical) = TaskParsers.failuresDetail(info, output: spec["output"]?.string ?? "", cwd: cwd,
                                                                 failed: spec["failed"]?.bool)
            return .object([
                "fingerprints": .array(result.fingerprints.map(JSONValue.string)),
                "failedCount": optional(result.failedCount),
                "passedCount": optional(result.passedCount),
                "framework": optional(result.framework),
                "canonical": .array(canonical.map(JSONValue.string)),
            ])
        case "edits":
            guard let source = source(spec) else { return .array([]) }
            return .array(TaskParsers.editHunks(source, tool: tool, input: input, cwd: cwd).map {
                .object(["file": .string($0.file), "old": optional($0.old), "new": .string($0.new)])
            })
        default:
            return .string("unknown group")
        }
    }

    static func json(_ info: TaskCommandInfo) -> JSONValue {
        .object([
            "class": .string(info.commandClass.rawValue), "suite": optional(info.suite), "targeted": .bool(info.targeted),
            "key": .string(info.key), "masked": .bool(info.masked), "background": .bool(info.background),
            "errorFilter": .bool(info.errorFilter),
        ])
    }

    static func json(_ outcome: TaskOutcome) -> JSONValue {
        .object(["exitCode": optional(outcome.exitCode), "failed": optional(outcome.failed), "t": optional(outcome.t)])
    }

    static func number(_ value: Int64) -> JSONValue { .number(JSONLiteral(String(value))) }

    static func optional(_ value: String?) -> JSONValue { value.map(JSONValue.string) ?? .null }

    static func optional(_ value: Int64?) -> JSONValue { value.map(number) ?? .null }

    static func optional(_ value: Bool?) -> JSONValue { value.map(JSONValue.bool) ?? .null }

    // MARK: Comparing

    /// Equal values; strings compare ordinally (scalar by scalar), numbers by kind and value.
    static func same(_ a: JSONValue, _ b: JSONValue) -> Bool {
        switch (a, b) {
        case (.null, .null): return true
        case (.bool(let x), .bool(let y)): return x == y
        case (.string(let x), .string(let y)): return x.unicodeScalars.elementsEqual(y.unicodeScalars)
        case (.number(let x), .number(let y)):
            guard x.isInteger == y.isInteger else { return false }
            return x.isInteger ? x.decimal == y.decimal : x.double == y.double
        case (.array(let x), .array(let y)): return x.count == y.count && zip(x, y).allSatisfy { same($0, $1) }
        case (.object(let x), .object(let y)):
            return Set(x.keys) == Set(y.keys) && x.allSatisfy { key, value in y[key].map { same(value, $0) } ?? false }
        default: return false
        }
    }

    static func render(_ value: JSONValue) -> String {
        switch value {
        case .null: return "null"
        case .bool(let flag): return flag ? "true" : "false"
        case .number(let literal): return literal.text
        case .string(let text): return "\"" + text.replacingOccurrences(of: "\n", with: "\\n") + "\""
        case .array(let items): return "[" + items.map(render).joined(separator: ", ") + "]"
        case .object(let members):
            return "{" + members.sorted { $0.key < $1.key }.map { "\($0.key): \(render($0.value))" }
                .joined(separator: ", ") + "}"
        }
    }
}
