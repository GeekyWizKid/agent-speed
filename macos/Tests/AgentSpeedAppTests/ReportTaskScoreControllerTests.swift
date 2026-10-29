import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

/// METRICS 11.10 in the report controller: the watch follows the service, the switches and the user's state and
/// reaches the monitor as an epoch; completions of that watch join the ledger once; a build takes it; a break empties it
/// after the tick's leaving report took it.
@Suite("Report task score controller")
@MainActor
struct ReportTaskScoreControllerTests {
    static let service = ReportController.previewService
    static let advertising = ReportController.previewDiscovery
    static let silent: ReportDiscovery = {
        let text = """
        {"protocol":"agent-speed-report","versions":[1],
         "service":{"name":"x","terms":"https://example.com/terms","privacy":"https://example.com/privacy"},
         "api":"https://api.example.com/agent-speed","sections":["sources","models","cost","traffic"]}
        """
        return try! ReportDiscovery.parse(Data(text.utf8), service: service).get()
    }()

    static func row(score: Double) -> ReportTaskRow {
        ReportTaskRow(ReportTaskRecord(actions: 4, score: score, success: true, stalled: false, fixAttempts: 1))!
    }

    @Test func theWatchFollowsTheSwitchesAndReachesTheMonitor() {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.advertising)
        #expect(Self.advertising.extensions?.taskScore == true)
        rig.tick()
        #expect(rig.epochs == [1])
        #expect(rig.report.taskWatchKey == Self.service.origin)

        rig.report.setSection(.models, on: false)
        #expect(rig.epochs == [1, nil], "the models switch off is a break")
        rig.report.setSection(.models, on: true)
        #expect(rig.epochs == [1, nil, 2])

        rig.report.updateIdentity(Self.service.origin) { $0.discovery = Self.silent }
        rig.tick()
        #expect(rig.epochs.last == .some(nil), "a document without the capability")
        rig.report.updateIdentity(Self.service.origin) { $0.discovery = Self.advertising }
        rig.tick()
        #expect(rig.epochs.last == 3)

        rig.report.settings.networkEnabled = false
        rig.tick()
        #expect(rig.epochs.last == .some(nil), "network off")
    }

    @Test func seedingAgainIsABreak() {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.advertising)
        rig.tick()
        var starting = PreviewData.snapshot(speed: 0, redline: 2000)
        starting.state = .starting(progress: 0.5)
        rig.report.handleLive(starting)
        #expect(rig.epochs == [1, nil])
        rig.tick()
        #expect(rig.epochs == [1, nil, 2])
    }

    @Test func completionsOfTheWatchJoinTheLedgerOnce() {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.advertising)
        rig.tick()
        let epoch = rig.report.taskTracker.epoch!
        rig.log.append(epoch: epoch, modelId: "claude-opus-5-5", source: .claudeCode, row: Self.row(score: 86.4))
        rig.log.append(epoch: epoch + 9, modelId: "gpt-5.5", source: .codex, row: Self.row(score: 50))
        rig.tick()
        rig.tick()
        #expect(rig.report.taskTracker.ledger.completed == 1)
        #expect(rig.report.taskTracker.pending["claude-opus-5-5"]?[.claudeCode]?.scoreSum == 86)

        // The preview shows what the next build takes, without taking it.
        let previewText = rig.report.preview().map { String(decoding: $0.body.compactData, as: UTF8.self) } ?? ""
        #expect(previewText.contains(#""scoreSum":86"#))
        #expect(rig.report.taskTracker.ledger.completed == 1)
    }

    /// Pausing sends a leaving report that carries the pending completions; the break then empties the ledger.
    @Test func aLeavingReportTakesTheLedgerBeforeTheBreak() async throws {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.advertising)
        rig.tick()
        rig.log.append(epoch: rig.report.taskTracker.epoch!, modelId: "claude-opus-5-5", source: .claudeCode,
                       row: Self.row(score: 93.75))
        rig.tick()
        #expect(rig.report.taskTracker.ledger.completed == 1)
        rig.report.pause(for: 3600)
        #expect(rig.report.taskTracker.ledger.isEmpty && rig.epochs.last == .some(nil))
        try await rig.waitUntil { !rig.transport.requests.isEmpty }
        let request = try #require(rig.transport.requests.last)
        let body = try #require(try JSONSerialization.jsonObject(with: request.body) as? [String: Any])
        #expect(body["activity"] as? String == "leaving")
        let models = try #require(body["models"] as? [[String: Any]])
        let opus = try #require(models.first { $0["id"] as? String == "claude-opus-5-5" })
        let rows = try #require(opus["taskStats"] as? [[String: Any]])
        #expect(rows.count == 1 && rows.first?["scoreSum"] as? Int == 94 && rows.first?["source"] as? String == "claudeCode")
        #expect(body["taskScore"] != nil)
    }

    @Test func aBuildTakesTheLedgerOnlyWhileTheExtensionIsActive() {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.advertising)
        rig.tick()
        rig.log.append(epoch: rig.report.taskTracker.epoch!, modelId: "gpt-5.5", source: .codex, row: Self.row(score: 40))
        rig.tick()
        let inactive = ReportOptions(sections: [.sources])
        #expect(rig.report.takeTaskStats(inactive).isEmpty)
        #expect(rig.report.taskTracker.ledger.completed == 1)
        let active = ReportOptions(sections: [.models], extensions: ReportExtensions(taskScore: true))
        #expect(rig.report.takeTaskStats(active)["gpt-5.5"]?[.codex]?.completed == 1)
        #expect(rig.report.taskTracker.ledger.isEmpty)
    }
}

/// A controller on its own settings suite and folder, with transports that never leave the process, and a completion
/// log the ticks' snapshots carry.
@MainActor
private final class Rig {
    let suite = "AgentSpeedReportTaskScoreControllerTests-\(UUID().uuidString)"
    let folder: URL
    let transport = RecordingTransport()
    let report: ReportController
    var log = ReportTaskCompletionLog()
    var epochs: [Int?] = []

    init() {
        let defaults = UserDefaults(suiteName: suite)!
        folder = FileManager.default.temporaryDirectory.appendingPathComponent(suite, isDirectory: true)
        report = ReportController(
            settings: AppSettings(defaults: defaults), store: SpeedStore(), traffic: TrafficStore(),
            priceBook: PriceBook(), appData: folder, transport: transport, streamTransport: SilentStreamTransport(),
            version: "1.4.1")
        report.taskWatchHandler = { [weak self] epoch in self?.epochs.append(epoch) }
    }

    func tearDown() {
        report.shutdown()
        UserDefaults.standard.removePersistentDomain(forName: suite)
        try? FileManager.default.removeItem(at: folder)
    }

    /// Registered with the example service on `document` over HTTP, reporting on, a software key.
    func register(_ document: ReportDiscovery) {
        let now = Date()
        var preferences = ReportPreferences()
        preferences.enabled = true
        preferences.preset = .custom
        preferences.customURL = ReportController.previewService.string
        preferences.invite = .accepted
        preferences.identities = [ReportIdentity(
            service: ReportController.previewService, deviceId: ReportCanonical.newUUID(), username: "racer-test",
            profileURL: nil, pin: ReportConsentPin(document), discovery: document, discoveryFetchedAt: now,
            keyCreatedAt: now, lastUsedAt: now)]
        preferences.setTransport(.http, for: ReportController.previewService.origin)
        report.preferences = preferences
        report.signer = SoftwareReportSigner()
    }

    /// One engine tick with the monitor running, an agent generating with a live score, and the completion log.
    func tick() {
        var snapshot = PreviewData.snapshot(speed: 240, redline: 2000)
        let value = TaskSetValue(id: "a1", lastLineMs: nil,
                                 value: TaskAgentValue(form: .live, score: 71.5, shown: 72, state: .passing))
        snapshot.report = ReportLiveFacts(
            agents: [.claudeCode: ReportAgentCounts(generating: 1, active: 1)],
            generatingModels: [ReportModelKey(modelId: "claude-opus-5-5", source: .claudeCode): 1],
            counters: ReportCounters(),
            task: ReportTaskFacts(value: value, thinking: 3.25,
                                  models: ["claude-opus-5-5": ReportTaskModel(value: value, thinking: 3.25,
                                                                              liveSources: [.claudeCode])],
                                  completions: log))
        report.handleLive(snapshot)
    }

    func waitUntil(_ condition: () -> Bool) async throws {
        let deadline = Date().addingTimeInterval(5)
        while !condition(), Date() < deadline {
            try await Task.sleep(for: .milliseconds(20))
        }
        #expect(condition())
    }
}

/// Answers every API request with `accepted` and keeps it; discovery is not fetched in these tests.
private final class RecordingTransport: ReportTransport, @unchecked Sendable {
    private let lock = NSLock()
    private var sent: [ReportRequest] = []

    var requests: [ReportRequest] { lock.withLock { sent } }

    func get(_ url: URL, userAgent: String, maxBytes: Int, timeout: TimeInterval)
        async throws(ReportTransportError) -> ReportHTTPResponse {
        ReportHTTPResponse(status: 404)
    }

    func send(_ request: ReportRequest, maxBytes: Int, timeout: TimeInterval)
        async throws(ReportTransportError) -> ReportHTTPResponse {
        lock.withLock { sent.append(request) }
        return ReportHTTPResponse(status: 200, headers: ["content-type": "application/json"],
                                  body: Data(#"{"status":"accepted"}"#.utf8))
    }
}

private final class SilentStreamTransport: ReportStreamTransport, @unchecked Sendable {
    func link(
        settings: ReportStreamSettings, queue: DispatchQueue,
        onEvent: @escaping @Sendable (ReportStreamEvent) -> Void,
        onSigningFailure: @escaping @Sendable () -> Void
    ) -> any ReportStreamLink {
        SilentStreamLink()
    }
}

private final class SilentStreamLink: ReportStreamLink, @unchecked Sendable {
    func start(_ makeRequest: @escaping @Sendable () throws -> ReportRequest) {}
    func send(_ makeText: @escaping @Sendable () throws -> String) {}
    func close() {}
    func abort() {}
}
