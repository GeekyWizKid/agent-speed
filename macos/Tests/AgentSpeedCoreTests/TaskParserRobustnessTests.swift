import Foundation
import Testing
@testable import AgentSpeedCore

/// The parsers never fail (TASK-PARSERS 1): fixture inputs mutated at random (quotes, escapes, brackets, line breaks,
/// unpaired forms, cut texts) run through every function without trapping, and stay deterministic.
@Suite("Task parser robustness")
struct TaskParserRobustnessTests {
    /// A small deterministic generator (SplitMix64).
    struct Generator: RandomNumberGenerator {
        var state: UInt64

        mutating func next() -> UInt64 {
            state &+= 0x9E37_79B9_7F4A_7C15
            var z = state
            z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
            z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
            return z ^ (z >> 31)
        }
    }

    static let tokens = [
        "'", "\"", "\\", "$(", "${", "`", "\n", "\r\n", "\r", "|", "||", "&&", "&", ";", "(", ")", "{", "}", "[", "]",
        "<<EOF\n", "<<-X\n", ">", "2>&1", "#", " ", "\t", "--", "-", "=", ":", "::", "é", "\u{2028}", "\u{1F600}",
        "\u{0}", "\u{1b}[31m", "\u{1b}]8;;x\u{07}", "*** Begin Patch\n", "*** Update File: a\n", "@@", "+", "Exit code 1\n",
        "Output:\n", "{\"output\":\"x\",\"exit_code\":1}", "tools.exec_command(", "await ", "text(", "/", "/*", "//",
        "0x", "1e9", "\\u{110000}", "\\uD800", "${PIPESTATUS[0]}", "$?", "echo ", "cargo test", "| grep error",
        "FAILED ", "error: ", " --> ", "test result: ok. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out",
        "[x#ABCD]\n", "PUT >1:\n", "+line\n",
    ]

    static func mutate(_ text: String, _ generator: inout Generator) -> String {
        var scalars = Array(text.unicodeScalars)
        for _ in 0..<Int.random(in: 1...4, using: &generator) {
            let position = scalars.isEmpty ? 0 : Int.random(in: 0...scalars.count, using: &generator)
            switch Int.random(in: 0..<4, using: &generator) {
            case 0:
                scalars.insert(contentsOf: tokens.randomElement(using: &generator)!.unicodeScalars, at: position)
            case 1 where !scalars.isEmpty:
                let end = min(scalars.count, position + Int.random(in: 1...8, using: &generator))
                scalars.removeSubrange(min(position, scalars.count)..<end)
            case 2:
                scalars.removeSubrange(position...)
            default:
                let token = tokens.randomElement(using: &generator)!
                scalars.insert(contentsOf: (token + token).unicodeScalars, at: position)
            }
        }
        return String(String.UnicodeScalarView(scalars))
    }

    /// The value with one of its strings mutated (or itself when it holds none).
    static func mutate(_ value: JSONValue, _ generator: inout Generator) -> JSONValue {
        switch value {
        case .string(let text):
            return .string(mutate(text, &generator))
        case .array(var items) where !items.isEmpty:
            let index = Int.random(in: 0..<items.count, using: &generator)
            items[index] = mutate(items[index], &generator)
            return .array(items)
        case .object(var members) where !members.isEmpty:
            let key = members.keys.sorted()[Int.random(in: 0..<members.count, using: &generator)]
            members[key] = mutate(members[key]!, &generator)
            return .object(members)
        default:
            return value
        }
    }

    @Test func mutatedInputsNeverTrap() {
        var generator = Generator(state: 0x5EED)
        var runs = 0
        for spec in TaskParserCaseTests.cases {
            if case .string(let output)? = spec["output"], output.utf8.count > 20_000 { continue }
            for _ in 0..<6 {
                var mutated = spec
                for field in ["text", "output", "command", "input", "result", "line"] where spec[field] != nil {
                    mutated[field] = Self.mutate(spec[field]!, &generator)
                }
                let first = TaskParserCaseTests.compute(mutated)
                let second = TaskParserCaseTests.compute(mutated)
                #expect(TaskParserCaseTests.same(first, second), "\(spec["id"]?.string ?? "?") is not deterministic")
                runs += 1
            }
        }
        #expect(runs > 3_000)
    }

    @Test func unexpectedTypesAreAbsent() {
        let odd: [JSONValue?] = [nil, .null, .bool(true), .number(JSONLiteral("1")), .string(""), .array([]),
                                 .object([:]), .string("{"), .string("[{\"type\":1}]"), .array([.null, .string("x")])]
        for source in SourceKind.allCases {
            for value in odd {
                _ = TaskParsers.actionKind(source, tool: "Bash", input: value, cwd: nil)
                _ = TaskParsers.outcome(source, result: value, t: nil, tool: "exec", input: value, cwd: "/")
                _ = TaskParsers.resultText(source, result: value)
                _ = TaskParsers.continuation(source, tool: "write_stdin", input: value)
                _ = TaskParsers.runningSession(source, result: value)
                _ = TaskParsers.notification(source, line: value)
                _ = TaskParsers.codeScript(source, tool: "exec", input: value)
                _ = TaskParsers.codeItems(source, result: value)
                _ = TaskParsers.editHunks(source, tool: "edit", input: value, cwd: "C:")
            }
        }
        #expect(TaskParsers.actionKind(.gemini, tool: "Bash", input: .object(["command": .string("ls")]), cwd: nil) == .other)
        #expect(TaskParsers.commandInfo("", cwd: nil).key == TaskText.emptyDigest)
    }

    @Test func theComparisonSeesDifferences() {
        #expect(!TaskParserCaseTests.same(.string("e\u{301}"), .string("\u{E9}")))
        #expect(!TaskParserCaseTests.same(.number(JSONLiteral("1")), .number(JSONLiteral("1.0"))))
        #expect(!TaskParserCaseTests.same(.object(["a": .null]), .object(["b": .null])))
        #expect(!TaskParserCaseTests.same(.array([.bool(true)]), .array([.bool(false)])))
        #expect(TaskParserCaseTests.same(.number(JSONLiteral("100000.0")), .number(JSONLiteral("1e5"))))
    }
}
