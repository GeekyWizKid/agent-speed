import Foundation
import Testing
@testable import AgentSpeedCore

/// The task score in the live monitor (METRICS 6.9 *Event stream*, *Values*; 6.11 *Predictions*): seeded lines build the
/// current task (unlike the 6.10 actions), live lines move it on, a new prompt starts the next task, and the agents
/// list, sources and all sources carry the values the readouts show.
@Suite("Task score in the monitor")
struct TaskSessionTests {
    static let relative = "claude/projects/-w-atlas/s-atlas.jsonl"

    static func line(_ ms: Int64, _ type: String, _ body: String) -> String {
        let time = Date(timeIntervalSince1970: TimeInterval(ms) / 1000).ISO8601Format()
        return #"{"type":"\#(type)","entrypoint":"cli","cwd":"/w/atlas","timestamp":"\#(time)",\#(body)}"#
    }

    static func prompt(_ ms: Int64, _ text: String) -> String {
        line(ms, "user", #""message":{"role":"user","content":"\#(text)"}"#)
    }

    /// A call and its result: an `Edit` or a `Bash` command.
    static func call(_ id: String, _ ms: Int64, tool: String, input: String, result: String, error: Bool = false) -> [String] {
        [
            line(ms, "assistant", #""message":{"id":"m-\#(id)","model":"claude-opus-5-5","stop_reason":"tool_use","content":[{"type":"tool_use","id":"\#(id)","name":"\#(tool)","input":\#(input)}],"usage":{"output_tokens":5}}"#),
            line(ms + 500, "user", #""message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"\#(id)","is_error":\#(error),"content":"\#(result)"}]}"#),
        ]
    }

    static func text(_ lines: [String]) -> String { lines.joined(separator: "\n") + "\n" }

    @Test func seededLinesBuildTheCurrentTaskAndLiveLinesMoveItOn() throws {
        let directory = try TempDirectory()
        let now = Clock.milliseconds(Date()) / 1000 * 1000
        let today = SessionTests.startOfToday()
        try directory.write(Self.relative, Self.text([Self.prompt(now - 60_000, "fix the parser")]
            + Self.call("e1", now - 55_000, tool: "Edit",
                        input: #"{"file_path":"/w/atlas/a.rs","old_string":"a","new_string":"b"}"#, result: "ok")))
        let configuration = MonitorConfiguration(enabledSources: [.claudeCode, .codex],
                                                 codexRoot: directory.file("codex"),
                                                 claudeRoot: directory.file("claude/projects"))
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: today)

        let seeded = session.advance(nowMs: now, todayStartMs: today, emitsEvents: false).output
        let agent = try #require(seeded.agents.first)
        #expect(agent.agent.task?.result?.state == .unverified, "the seeded edit is in the current task")
        #expect(agent.agent.task?.taskStartMs == now - 60_000)
        #expect(agent.intelligence.score.form == .live && agent.intelligence.score.agentId == agent.agent.id)
        #expect(agent.intelligence.score.score == agent.agent.task?.result?.score)
        #expect(seeded.intelligence.score == agent.intelligence.score, "the highest score of all sources")
        #expect(seeded.sourceIntelligence[.claudeCode]?.score == agent.intelligence.score)
        #expect(seeded.sourceIntelligence[.codex]?.score.form != .live)

        try directory.append(Self.relative, Self.text(Self.call("b1", now - 40_000, tool: "Bash",
                                                                input: #"{"command":"cargo test"}"#,
                                                                result: "test result: ok. 12 passed; 0 failed")))
        #expect(session.rediscover(todayStartMs: today))
        let passing = try #require(session.advance(nowMs: now, todayStartMs: today, emitsEvents: false).output.agents.first)
        #expect(passing.agent.task?.result?.state == .passing)
        let score = try #require(passing.agent.task?.result?.score)
        #expect(score > (agent.agent.task?.result?.score ?? 0), "a passing test raises the score")
        #expect(passing.agent.task?.predictions?.success != nil)
        #expect(passing.agent.task?.models == .prior, "no calibration yet")

        // An idle agent rests on its last score.
        let later = now + 400_000
        let resting = try #require(session.advance(nowMs: later, todayStartMs: today, emitsEvents: false).output.agents.first)
        #expect(resting.phase == .idle && resting.intelligence.score.form == .resting && resting.intelligence.score.score == score)

        // A new prompt starts a task with no score yet.
        try directory.append(Self.relative, Self.text([Self.prompt(now - 10_000, "now update the docs")]))
        #expect(session.rediscover(todayStartMs: today))
        let next = try #require(session.advance(nowMs: now, todayStartMs: today, emitsEvents: false).output.agents.first)
        #expect(next.agent.task?.taskStartMs == now - 10_000)
        #expect(next.agent.task?.result?.state == TaskState.none && next.intelligence.score.form == .none)
        #expect(next.agent.task?.result?.counts.actions == 0)
    }

    @Test func aCalibrationReplacesThePriorsOfItsSource() throws {
        let directory = try TempDirectory()
        let now = Clock.milliseconds(Date()) / 1000 * 1000
        let today = SessionTests.startOfToday()
        try directory.write(Self.relative, Self.text([Self.prompt(now - 60_000, "fix it")]
            + Self.call("b1", now - 50_000, tool: "Bash", input: #"{"command":"cargo test"}"#,
                        result: "test result: FAILED. 3 passed; 2 failed", error: true)))
        let configuration = MonitorConfiguration(enabledSources: [.claudeCode], claudeRoot: directory.file("claude/projects"))
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: today)
        let before = try #require(session.advance(nowMs: now, todayStartMs: today, emitsEvents: false).output.agents.first)
        #expect(before.agent.task?.result?.state == .failing)
        let prior = try #require(before.agent.task?.predictions)

        var calibrated = TaskModels.prior
        calibrated.fix = TaskLogisticModel(coefficients: [2, 0, 0, 0, 0, 0], calibrated: true, tasks: 57)
        session.setTaskModels([.claudeCode: calibrated])
        let after = try #require(session.advance(nowMs: now, todayStartMs: today, emitsEvents: false).output.agents.first)
        #expect(after.agent.task?.models.fix == TaskModelFlag(calibrated: true, tasks: 57))
        #expect(after.agent.task?.models.success == TaskModelFlag(calibrated: false, tasks: 0))
        let fix = try #require(after.agent.task?.predictions?.fix)
        #expect(abs(fix - 1 / (1 + exp(-2))) < 1e-12 && fix != prior.fix)
        #expect(after.agent.task?.predictions?.success == prior.success)
    }

    @Test func aLongSessionKeepsOnlyItsCurrentTaskLive() throws {
        let directory = try TempDirectory()
        let now = Clock.milliseconds(Date()) / 1000 * 1000
        let today = SessionTests.startOfToday()
        var lines: [String] = []
        for index in 0..<40 {
            let at = now - 400_000 + Int64(index) * 8000
            lines.append(Self.prompt(at, "task \(index)"))
            lines += Self.call("e\(index)", at + 1000, tool: "Edit",
                               input: #"{"file_path":"/w/atlas/f\#(index).rs","old_string":"a","new_string":"b"}"#, result: "ok")
        }
        try directory.write(Self.relative, Self.text(lines))
        let configuration = MonitorConfiguration(enabledSources: [.claudeCode], claudeRoot: directory.file("claude/projects"))
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: today)
        let agents = session.taskAgents()
        #expect(agents.count == 1)
        #expect(agents.first?.events.count == 2, "the live stream keeps the current task: its prompt and its edit")
        let row = try #require(session.advance(nowMs: now, todayStartMs: today, emitsEvents: false).output.agents.first)
        #expect(row.agent.task?.taskStartMs == now - 400_000 + 39 * 8000)
    }
}
