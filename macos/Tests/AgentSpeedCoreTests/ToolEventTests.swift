import Foundation
import Testing
@testable import AgentSpeedCore

/// Edges of the tool-call reading of METRICS 6.10 that the fixtures leave out.
@Suite("Tool events")
struct ToolEventTests {
    static func codexResult(_ output: String) -> [ToolEvent] {
        let line = #"{"type":"response_item","payload":{"type":"function_call_output","call_id":"c","output":\#(output)}}"#
        return Array(line.utf8).withUnsafeBytes { ToolEventExtractor.events(.codex, $0) }
    }

    @Test(arguments: [
        (#""  {\"exit_code\":2} and more""#, true),
        (#""{\"exit_code\":0}""#, false),
        (#""{\"exit_code\":true}""#, nil),
        (#""{\"exit_code\":1.0}""#, nil),
        (#""exit_code: 1""#, nil),
        (#"[{"type":"input_image"},{"type":"input_text","text":"{\"exit_code\":7}"}]"#, true),
        (#"[{"type":"input_text","text":7},{"type":"input_text","text":"{\"exit_code\":7}"}]"#, nil),
        (#""[{\"type\":\"input_text\",\"text\":\"{\\\"exit_code\\\":3}\"}]""#, true),
        (#""[not json""#, nil),
        (#"{"exit_code":1}"#, nil),
    ] as [(String, Bool?)])
    func codexExitCodes(_ output: String, _ failed: Bool?) {
        #expect(Self.codexResult(output) == [.result(id: "c", failed: failed)])
    }

    @Test func callsWithoutAnIdOrNameAreNotActions() {
        let line = #"{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{}},{"type":"tool_use","id":"t"}]}}"#
        #expect(Array(line.utf8).withUnsafeBytes { ToolEventExtractor.events(.claude, $0) }.isEmpty)
    }

    @Test func aMissingOrNullInputHashesAsEmptyText() {
        let line = #"{"type":"message","message":{"role":"assistant","content":[{"type":"toolCall","id":"p","name":"ls","arguments":null}]}}"#
        let events = Array(line.utf8).withUnsafeBytes { ToolEventExtractor.events(.omp, $0) }
        #expect(events == [.call(id: "p", ToolCall(tool: "ls", digest: .empty))])
        #expect(ToolDigest.empty.hex == "e3b0c44298fc1c14")
    }

    @Test func theLogKeepsTheNewestActions() {
        var log = ActionLog()
        for index in 0..<(ActionLog.capacity + 6) {
            log.record([.call(id: "c\(index)", ToolCall(tool: "t", digest: .empty))], at: Int64(index))
        }
        log.record([.result(id: "c0", failed: true), .result(id: "c69", failed: true)], at: 100)
        #expect(log.actions.count == ActionLog.capacity)
        #expect(log.actions.first?.atMs == 6)
        #expect(log.actions.last?.failed == true)
        #expect(!log.actions.dropLast().contains { $0.failed != nil })
    }
}
