#if DEBUG
import AgentSpeedCore
import Foundation

/// The `taskScore` part of `--report-e2e` (`AGENT_SPEED_REPORT_E2E_TASKSCORE=1`, server with `--task-score`), over the
/// transport the run uses: the real controller with the harness's snapshots standing in for the monitor (a live score
/// and a completion log marked with the controller's watch epoch). The server's merged model table
/// (`GET {api}/public/models`) must count every completion once and show the latest live values.
@MainActor
enum ReportTaskScoreHarness {
    typealias Check = ReportStreamHarness.Check
    typealias Wait = ReportStreamHarness.Wait

    static let enabled = ProcessInfo.processInfo.environment["AGENT_SPEED_REPORT_E2E_TASKSCORE"] == "1"
    static let opus = "claude-opus-5-5"
    static let gpt = "gpt-5.5"

    /// The completions the harness's snapshots carry, as the engine's log would.
    private static var log = ReportTaskCompletionLog()
    private static var lastTable: [String: Any] = [:]

    /// The task facts of a harness snapshot: one Claude Code agent on Opus, live while busy, resting while idle.
    static func facts(busy: Bool) -> ReportTaskFacts {
        guard enabled else { return .empty }
        let value = TaskSetValue(id: "harness-agent", lastLineMs: nil, value: TaskAgentValue(
            form: busy ? .live : .resting, score: 71.5, shown: 72, state: .passing))
        let thinking = busy ? 3.25 : 0
        return ReportTaskFacts(value: value, thinking: thinking,
                               models: [opus: ReportTaskModel(value: value, thinking: thinking,
                                                              liveSources: busy ? [.claudeCode] : [])],
                               completions: log)
    }

    static func run(_ model: AppModel, publish: @escaping @MainActor () -> Void, check: Check, wait: Wait) async {
        let report = model.report
        check("taskScore: the service advertises it", report.discovery?.extensions?.taskScore == true)
        await wait("taskScore: the watch is on", 10) { report.taskTracker.epoch != nil }
        guard let epoch = report.taskTracker.epoch, let api = report.discovery?.api.string,
              let tableURL = URL(string: api + "/public/models") else { return }

        complete(epoch, opus, .claudeCode, ReportTaskRecord(actions: 6, score: 86.4, success: true, stalled: false,
                                                            fixAttempts: 1, recovered: 1, recoverAttempts: 1))
        complete(epoch, opus, .claudeCode, ReportTaskRecord(actions: 4, score: 93.75, success: true, stalled: false))
        complete(epoch, gpt, .codex, ReportTaskRecord(actions: 9, score: 55, success: false, stalled: true,
                                                      fixAttempts: 3, fixFailures: 1, recovered: 1, recoverAttempts: 2))
        complete(epoch + 7, gpt, .codex, ReportTaskRecord(actions: 3, score: 10, success: false, stalled: false))
        complete(epoch, gpt, .codex, ReportTaskRecord(actions: 0, score: nil, success: false, stalled: false))
        await waitForTable("taskScore: the completions reach the service", tableURL, wait) {
            completed($0, opus) == 2 && completed($0, gpt) == 1
        }
        let delivered = deliveries(report)
        await wait("taskScore: three more reports", 30) { deliveries(report) >= delivered + 3 }
        await waitForTable("taskScore: each completion is counted once", tableURL, wait) {
            completed($0, opus) == 2 && completed($0, gpt) == 1
        }
        check("taskScore: the rows add up as sent", {
            let opusStats = stats(lastTable, opus)
            let gptStats = stats(lastTable, gpt)
            return opusStats["scoreSum"] as? Int == 180 && opusStats["successes"] as? Int == 2
                && (opusStats["scoreBins"] as? [Int]) == [0, 0, 0, 0, 0, 0, 0, 0, 1, 1]
                && opusStats["fixAttempts"] as? Int == 1 && opusStats["recoverAttempts"] as? Int == 1
                && gptStats["scored"] as? Int == 1 && gptStats["scoreSum"] as? Int == 55 && gptStats["stalls"] as? Int == 1
                && gptStats["fixFailures"] as? Int == 1 && gptStats["recoverAttempts"] as? Int == 2
        }())
        check("taskScore: the live values are accepted", {
            let top = lastTable["live"] as? [String: Any]
            let live = row(lastTable, opus)?["live"] as? [String: Any]
            return top?["score"] as? Int == 72 && top?["form"] as? String == "live"
                && (top?["thinkingTokS"] as? NSNumber)?.doubleValue == 3.3
                && live?["score"] as? Int == 72 && live?["devices"] as? Int == 1
        }())
        check("taskScore: no report was refused", report.problem == nil
              && (report.stream?.scheduler.counters.rejected ?? 0) == 0)

        // A completion just before pausing goes out with the leaving report; the break then empties the ledger.
        complete(epoch, opus, .claudeCode, ReportTaskRecord(actions: 2, score: 64, success: false, stalled: false))
        publish()
        report.pause(for: 3600)
        check("taskScore: paused, the watch is off and the ledger empty", report.taskTracker.epoch == nil
              && report.taskTracker.ledger.isEmpty)
        await waitForTable("taskScore: the leaving report carried the last completion", tableURL, wait) {
            completed($0, opus) == 3
        }
        report.resume()
        await wait("taskScore: resumed, a new watch", 10) { (report.taskTracker.epoch ?? 0) > epoch }
        let resumed = report.taskTracker.epoch ?? 0
        complete(epoch, opus, .claudeCode, ReportTaskRecord(actions: 2, score: 99, success: true, stalled: false))
        complete(resumed, gpt, .codex, ReportTaskRecord(actions: 5, score: 77, success: true, stalled: false))
        await waitForTable("taskScore: only the new watch's completion counts", tableURL, wait) {
            completed($0, gpt) == 2 && completed($0, opus) == 3
        }

        report.setSection(.models, on: false)
        check("taskScore: the models switch off ends the watch", report.taskTracker.epoch == nil)
        let off = deliveries(report)
        await wait("taskScore: reports go on without models", 30) { deliveries(report) >= off + 2 }
        await waitForTable("taskScore: the service clears the live values and keeps the sums", tableURL, wait) {
            row($0, opus)?["live"] is NSNull && completed($0, opus) == 3 && completed($0, gpt) == 2
        }
        report.setSection(.models, on: true)
        await wait("taskScore: models on again, a new watch", 10) { (report.taskTracker.epoch ?? 0) > resumed }
        await waitForTable("taskScore: live values again", tableURL, wait) {
            (row($0, opus)?["live"] as? [String: Any])?["score"] as? Int == 72
        }
        check("taskScore: still nothing refused", report.problem == nil
              && (report.stream?.scheduler.counters.rejected ?? 0) == 0)
    }

    // MARK: - Helpers

    private static func complete(_ epoch: Int, _ model: String, _ source: SourceKind, _ record: ReportTaskRecord) {
        guard let row = ReportTaskRow(record) else { return }
        log.append(epoch: epoch, modelId: model, source: source, row: row)
    }

    /// Delivered reports of either transport.
    private static func deliveries(_ report: ReportController) -> Int {
        report.stream.map { $0.scheduler.counters.accepted + $0.scheduler.counters.duplicate } ?? deliveredOverHTTP(report)
    }

    private static var httpDeliveries = 0
    private static var lastHTTPDelivery: Date?

    private static func deliveredOverHTTP(_ report: ReportController) -> Int {
        if let at = report.lastDeliveryAt, at != lastHTTPDelivery {
            lastHTTPDelivery = at
            httpDeliveries += 1
        }
        return httpDeliveries
    }

    private static func waitForTable(_ name: String, _ url: URL, _ wait: Wait,
                                     _ condition: @escaping @MainActor ([String: Any]) -> Bool) async {
        let deadline = Date().addingTimeInterval(20)
        while Date() < deadline {
            if let table = await fetch(url) {
                lastTable = table
                if condition(table) { break }
            }
            try? await Task.sleep(for: .milliseconds(400))
        }
        let table = lastTable
        await wait(name, 0) { condition(table) }
    }

    private static func fetch(_ url: URL) async -> [String: Any]? {
        let session = URLSession(configuration: .ephemeral)
        defer { session.finishTasksAndInvalidate() }
        guard let (data, response) = try? await session.data(from: url),
              (response as? HTTPURLResponse)?.statusCode == 200 else { return nil }
        return try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    }

    private static func row(_ table: [String: Any], _ id: String) -> [String: Any]? {
        (table["models"] as? [[String: Any]])?.first { $0["id"] as? String == id }
    }

    private static func stats(_ table: [String: Any], _ id: String) -> [String: Any] {
        row(table, id)?["stats"] as? [String: Any] ?? [:]
    }

    private static func completed(_ table: [String: Any], _ id: String) -> Int {
        stats(table, id)["completed"] as? Int ?? 0
    }
}
#endif
