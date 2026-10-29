import Foundation
import Testing
@testable import AgentSpeedCore

/// METRICS 11.10 against report-vectors.json `taskScore` (`capability`, `records`, `assembly`, `ledger`), run through
/// the app's own discovery parser, rows, body builder, watch marks, completion log and tracker, and the two example
/// bodies rebuilt exactly.
@Suite struct ReportTaskScoreTests {
    static var vectors: [String: Any] {
        ((try? Fixtures.json("report-vectors.json"))?["taskScore"] as? [String: Any]) ?? [:]
    }

    static func cases(_ group: String) -> [[String: Any]] { vectors[group] as? [[String: Any]] ?? [] }
    static func names(_ group: String) -> [String] { cases(group).compactMap { $0.string("name") } }
    static func spec(_ group: String, _ name: String) throws -> [String: Any] {
        try #require(cases(group).first { $0.string("name") == name })
    }

    @Test func everyGroupIsInTheFixture() {
        #expect(Self.cases("capability").count == 10)
        #expect(Self.cases("records").count == 6)
        #expect(Self.cases("assembly").count == 3)
        #expect(Self.cases("ledger").count == 8)
    }

    // MARK: - Capability

    @Test(arguments: names("capability"))
    func capability(_ name: String) throws {
        let spec = try Self.spec("capability", name)
        var document: [String: Any] = [
            "protocol": "agent-speed-report", "versions": [1],
            "service": ["name": "x", "terms": "https://example.com/t", "privacy": "https://example.com/p"],
            "api": "https://api.example.com",
        ]
        for (key, value) in try #require(spec["document"] as? [String: Any]) { document[key] = value }
        let data = try JSONSerialization.data(withJSONObject: document)
        let service = try ReportServiceURL.parse("https://example.com").get()
        let discovery = try ReportDiscovery.parse(data, service: service).get()
        let switches: Set<ReportSection> = spec["modelsSwitch"] as? Bool == true
            ? Set(ReportSection.allCases) : Set(ReportSection.allCases).subtracting([.models])
        #expect(ReportTaskScoreRule.isActive(discovery, switches: switches) == (spec["expect"] as? Bool), "\(name)")
    }

    // MARK: - Records

    static func record(_ value: [String: Any]) throws -> ReportTaskRecord {
        let counts = try #require(value["counts"] as? [String: Any])
        return ReportTaskRecord(
            actions: value.int("actions"), score: value.double("score"), success: value["success"] as? Bool ?? false,
            stalled: value["stalled"] as? Bool ?? false, fixAttempts: counts.int("fixAttempts"),
            fixFailures: counts.int("fixFailures"), recovered: counts.int("recovered"),
            recoverAttempts: counts.int("recoverAttempts"))
    }

    static func source(_ value: [String: Any]) throws -> SourceKind {
        try #require(SourceKind(rawValue: value.string("source") ?? ""))
    }

    static func object(_ json: ReportJSON) throws -> Any {
        try JSONSerialization.jsonObject(with: json.compactData, options: .fragmentsAllowed)
    }

    static func same(_ lhs: Any, _ rhs: Any) -> Bool {
        NSArray(array: [lhs]).isEqual(to: [rhs])
    }

    static func row(_ value: [String: Any]) throws -> ReportTaskRow {
        var row = ReportTaskRow()
        row.completed = value.int("completed")
        row.scored = value.int("scored")
        row.scoreSum = value.int("scoreSum")
        row.scoreBins = try #require(value["scoreBins"] as? [Int])
        row.successes = value.int("successes")
        row.stalls = value.int("stalls")
        row.fixAttempts = value.int("fixAttempts")
        row.fixFailures = value.int("fixFailures")
        row.recovered = value.int("recovered")
        row.recoverAttempts = value.int("recoverAttempts")
        return row
    }

    @Test(arguments: names("records"))
    func records(_ name: String) throws {
        let spec = try Self.spec("records", name)
        let input = try #require(spec["record"] as? [String: Any])
        let row = ReportTaskRow(try Self.record(input))
        guard let expected = spec["expect"] as? [String: Any] else {
            #expect(row == nil, "\(name)")
            return
        }
        let built = try Self.object(try #require(row).json(source: try Self.source(input)))
        #expect(Self.same(built, expected), "\(name): \(built)")
    }

    // MARK: - Assembly

    /// The case's agents, thinking, models of 11.3 and task ledger as the engine and the controller hand them to the
    /// body builder.
    static func assembly(_ input: [String: Any]) throws -> (ReportWindowContent, ReportSample, ReportOptions) {
        var counters = ReportCounters()
        var generating: [ReportModelKey: Int] = [:]
        var agents: [SourceKind: ReportAgentCounts] = [:]
        for model in try #require(input["models"] as? [[String: Any]]) {
            let id = try #require(model.string("id"))
            let sources = try #require(model["sources"] as? [String]).map { try #require(SourceKind(rawValue: $0)) }
            let source = try #require(sources.first)
            counters.add(ReportUsage(requests: Int64(model.int("requests")), output: Int64(model.int("output")),
                                     measuredOutput: Int64(model.int("measuredOutput")),
                                     measuredMs: Int64(model.int("measuredMs"))),
                         to: ReportModelKey(modelId: id, source: source))
            if model.int("generating") > 0 {
                generating[ReportModelKey(modelId: id, source: source)] = model.int("generating")
                agents[source, default: .zero].generating += model.int("generating")
                agents[source, default: .zero].active += model.int("generating")
            }
        }
        let thinking = try #require(input["thinking"] as? [String: Any])
        var task = ReportTaskFacts(thinking: try #require(thinking.double("total")))
        for (id, value) in thinking["models"] as? [String: Double] ?? [:] {
            task.models[id, default: ReportTaskModel()].thinking = value
        }
        for agent in try #require(input["agents"] as? [[String: Any]]) {
            let id = try #require(agent.string("model"))
            let form = try #require(TaskValueForm(rawValue: agent.string("form") ?? ""))
            let score = agent.double("score")
            let value = TaskSetValue(id: try #require(agent.string("id")), lastLineMs: nil,
                                     value: TaskAgentValue(form: form, score: score, shown: score.map(taskRoundHalfAway),
                                                           state: nil))
            task.value.merge(value)
            task.models[id, default: ReportTaskModel()].value.merge(value)
            if form == .live { task.models[id]?.liveSources.insert(try Self.source(agent)) }
        }
        let sample = ReportSample(facts: ReportLiveFacts(agents: agents, generatingModels: generating, counters: counters,
                                                         task: task))
        var content = ReportWindow(ReportSample()).content(sample)
        for (id, rows) in input["ledger"] as? [String: [[String: Any]]] ?? [:] {
            for row in rows { content.taskStats[id, default: [:]][try Self.source(row)] = try Self.row(row) }
        }
        let options = ReportOptions(sections: [.models], maxModels: input.int("maxModels"),
                                    extensions: ReportExtensions(taskScore: true))
        return (content, sample, options)
    }

    static let envelope = ReportEnvelope(
        client: ReportClientInfo(version: "1.3.0", osMajor: 27), deviceId: "6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b",
        username: "zoe-mbp", runId: "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", seq: 42, sentAtMs: 1_792_656_930_500,
        windowMs: 5000, activity: .active, titleLevel: 8)

    @Test(arguments: names("assembly"))
    func assembly(_ name: String) throws {
        let spec = try Self.spec("assembly", name)
        let (content, sample, options) = try Self.assembly(try #require(spec["input"] as? [String: Any]))
        let body = try #require(try Self.object(ReportBody.json(Self.envelope, content, sample, options)) as? [String: Any])
        let expected = try #require(spec["expect"] as? [String: Any])
        #expect(Self.same(try #require(body["taskScore"]), try #require(expected["taskScore"])), "\(name): \(body["taskScore"] ?? "")")
        #expect(Self.same(try #require(body["models"]), try #require(expected["models"])), "\(name): \(body["models"] ?? "")")
    }

    /// The values the WebSocket changed rule compares are those the body carries.
    @Test(arguments: names("assembly"))
    func changedRuleValuesMatchTheBody(_ name: String) throws {
        let spec = try Self.spec("assembly", name)
        let (content, sample, options) = try Self.assembly(try #require(spec["input"] as? [String: Any]))
        let values = try #require(ReportBody.taskScoreValues(content, sample, options))
        let expected = try #require(spec["expect"] as? [String: Any])
        for entry in try #require(expected["models"] as? [[String: Any]]) {
            let id = try #require(entry.string("id"))
            let value = try #require(values.entries[id])
            #expect(Self.same(try Self.object(value.json), try #require(entry["taskScore"])), "\(name)")
        }
        #expect(Self.same(try Self.object(values.top.json), try #require(expected["taskScore"])), "\(name)")
        var inactive = options
        inactive.extensions.taskScore = false
        #expect(ReportBody.taskScoreValues(content, sample, inactive) == nil)
    }

    @Test func theChangedRuleComparesTheObjectsTheReportWouldCarry() {
        func value(_ score: Double, _ thinking: Double) -> ReportTaskScoreValue {
            ReportTaskScoreValue(TaskSetValue(id: "a", lastLineMs: nil, value: TaskAgentValue(
                form: .live, score: score, shown: taskRoundHalfAway(score), state: nil)), thinking: thinking)
        }
        func values(top: ReportTaskScoreValue, _ entries: [String: ReportTaskScoreValue]) -> ReportStreamValues {
            ReportStreamValues(ReportSample(), taskScore: ReportTaskScoreValues(top: top, entries: entries))
        }
        let last = values(top: value(80, 1.04), ["a": value(80, 1.04), "b": value(40, 0)])
        #expect(values(top: value(80, 1.04), [:]).changed(since: nil))
        #expect(!values(top: value(80.2, 1.01), ["a": value(79.9, 0.96)]).changed(since: last),
                "rounded as sent; an entry no longer carried changes nothing")
        #expect(values(top: value(80, 1.06), ["a": value(80, 1.04)]).changed(since: last), "thinking 1.0 → 1.1")
        #expect(values(top: value(80, 1.04), ["a": value(81, 1.04)]).changed(since: last), "a test result moved it")
        #expect(values(top: value(80, 1.04), ["c": value(10, 0)]).changed(since: last), "a newly listed model")
        #expect(values(top: value(80, 1.04), [:]).changed(since: ReportStreamValues(ReportSample())), "turned on")
        #expect(!ReportStreamValues(ReportSample()).changed(since: last), "inactive: nothing of the extension")
    }

    // MARK: - Ledger

    /// The `ledger` events through the watch marks (monitor), the completion log (engine) and the tracker (controller).
    struct LedgerRun {
        var tracker = ReportTaskTracker()
        var marks = ReportTaskWatchMarks<String>()
        var log = ReportTaskCompletionLog()
        var watchOn = false
        var builds: [[String: Any]] = []

        mutating func apply(_ event: [String: Any]) throws {
            let at = try #require(event.int64("at"))
            func ids(_ key: String) -> [String] {
                let task = event.string(key) ?? ""
                guard let count = event["count"] as? Int else { return [task] }
                return (0..<count).map { "\(task)-\($0)" }
            }
            if let on = event["watch"] as? Bool {
                if watchOn, !on { marks.clear() }
                watchOn = on
                tracker.watch(on ? "service" : nil)
            } else if event["start"] != nil {
                let seeding = event["seeding"] as? Bool ?? false
                let context = watchOn && !seeding ? tracker.epoch.map { ReportTaskWatchContext(epoch: $0, nowMs: at) } : nil
                for task in ids("start") {
                    marks.started(task, firstEventMs: try #require(event.int64("t")), context: context)
                }
            } else if let task = event.string("withdraw") ?? event.string("drop") {
                marks.unwatch(task)
            } else if event["complete"] != nil {
                let input = try #require(event["record"] as? [String: Any])
                let record = try ReportTaskScoreTests.record(input)
                for task in ids("complete") {
                    guard let epoch = marks.complete(task), let row = ReportTaskRow(record) else { continue }
                    log.append(epoch: epoch, modelId: try #require(input.string("model")),
                               source: try ReportTaskScoreTests.source(input), row: row)
                }
                tracker.ingest(log)
            } else if let seq = event["build"] as? Int {
                tracker.ingest(log)
                let taken = tracker.take()
                builds.append(["seq": seq,
                               "taskStats": event["models"] as? Bool == false ? NSNull() : try Self.json(taken)])
            } else if event["relaunch"] != nil {
                self = LedgerRun(builds: builds)
            }
        }

        static func json(_ stats: ReportTaskStats) throws -> [String: Any] {
            var out: [String: Any] = [:]
            for (id, rows) in stats {
                out[id] = try SourceKind.allCases.compactMap { source in
                    try rows[source].map { try ReportTaskScoreTests.object($0.json(source: source)) }
                }
            }
            return out
        }
    }

    @Test(arguments: names("ledger"))
    func ledger(_ name: String) throws {
        let spec = try Self.spec("ledger", name)
        var run = LedgerRun()
        for event in try #require(spec["events"] as? [[String: Any]]) { try run.apply(event) }
        let expected = try #require(spec["expect"] as? [String: Any])
        #expect(Self.same(run.builds, try #require(expected["builds"])), "\(name): \(run.builds)")
        #expect(run.marks.marks.keys.sorted() == (expected["watched"] as? [String]), "\(name)")
        let pending = try LedgerRun.json(run.tracker.take())
        #expect(Self.same(pending, try #require(expected["pending"])), "\(name)")
    }

    @Test func aCompletionOfAnotherWatchIsIgnoredAndALogIsReadOnce() {
        var tracker = ReportTaskTracker()
        var log = ReportTaskCompletionLog()
        let started = tracker.watch("a")
        let again = tracker.watch("a")
        #expect(started && !again)
        let epoch = tracker.epoch!
        var row = ReportTaskRow()
        row.completed = 1
        log.append(epoch: epoch, modelId: "gpt-5.5", source: .codex, row: row)
        log.append(epoch: epoch + 7, modelId: "gpt-5.5", source: .codex, row: row)
        let received = tracker.ingest(log)
        let repeated = tracker.ingest(log)
        #expect(received && !repeated, "read once")
        #expect(tracker.ledger.completed == 1)
        let switched = tracker.watch("b")
        #expect(switched, "another service: a break, then a new watch")
        #expect(tracker.ledger.isEmpty && tracker.epoch == epoch + 1)
        var next = ReportTaskCompletionLog()
        next.append(epoch: epoch + 1, modelId: "gpt-5.5", source: .codex, row: row)
        let fresh = tracker.ingest(next)
        #expect(fresh, "a new engine's log starts over")
    }

    // MARK: - Examples

    /// The spec's active example with the task score of METRICS 11.10.
    static func exampleInputs() -> (ReportWindowContent, ReportSample) {
        var counters = ReportCounters()
        counters.add(ReportUsage(requests: 3, output: 1500, input: 800_000, cachedInput: 780_000, cacheWriteInput: 12_050,
                                 measuredOutput: 1400, measuredMs: 3300, costMicroUsd: 18_003),
                     to: ReportModelKey(modelId: "claude-opus-5-5", source: .claudeCode))
        counters.add(ReportUsage(requests: 1, output: 340, input: 112_400, cachedInput: 100_120, reasoning: 310,
                                 measuredOutput: 302, measuredMs: 820, costMicroUsd: 3000),
                     to: ReportModelKey(modelId: "gpt-5.5", source: .codex))
        func value(_ id: String, _ form: TaskValueForm, _ score: Double) -> TaskSetValue {
            TaskSetValue(id: id, lastLineMs: nil, value: TaskAgentValue(form: form, score: score,
                                                                         shown: taskRoundHalfAway(score), state: nil))
        }
        let task = ReportTaskFacts(
            value: value("a1", .live, 91.5), thinking: 38.64,
            models: ["claude-opus-5-5": ReportTaskModel(value: value("a1", .live, 91.5), liveSources: [.claudeCode]),
                     "gpt-5.5": ReportTaskModel(value: value("a3", .live, 63.75), thinking: 38.64, liveSources: [.codex]),
                     "claude-sonnet-5-5": ReportTaskModel(value: value("a6", .resting, 23))])
        let facts = ReportLiveFacts(
            agents: [.codex: ReportAgentCounts(generating: 1, active: 2),
                     .claudeCode: ReportAgentCounts(generating: 2, tools: 1, active: 3)],
            generatingModels: [ReportModelKey(modelId: "claude-opus-5-5", source: .claudeCode): 2,
                               ReportModelKey(modelId: "gpt-5.5", source: .codex): 1],
            counters: counters, task: task)
        let sample = ReportSample(
            live: Rate(output: 412.6, total: 182_340.2), estimatedShare: 0.215,
            liveCost: LiveCost(microUsdPerSecond: 4213, knownOutput: 412.6),
            sources: [
                .codex: .init(live: Rate(output: 112.2, total: 32_120.1), liveEstimated: Rate(output: 52.7, total: 0),
                              liveCost: LiveCost(microUsdPerSecond: 613, knownOutput: 112.2)),
                .claudeCode: .init(live: Rate(output: 300.4, total: 150_220.1), liveEstimated: Rate(output: 36, total: 0),
                                   liveCost: LiveCost(microUsdPerSecond: 3600, knownOutput: 300.4)),
            ],
            facts: facts,
            traffic: ReportTrafficSample(
                status: .ok, includesTools: true, rate: TrafficRate(rx: 24_310, tx: 1288),
                sourceRates: [.codex: TrafficRate(rx: 4200, tx: 288), .claudeCode: TrafficRate(rx: 20_110, tx: 1000)],
                totals: TrafficReportTotals(sources: [.codex: TrafficSplit(own: TrafficBytes(rx: 21_000, tx: 1440)),
                                                      .claudeCode: TrafficSplit(own: TrafficBytes(rx: 100_550, tx: 5000))])))
        var content = ReportWindow(ReportSample(traffic: ReportTrafficSample(status: .ok))).content(sample)
        content.peak = Rate(output: 655, total: 240_118.7)
        content.peakCost = 6120
        content.peakRate = TrafficRate(rx: 61_200, tx: 3410)
        var opus = ReportTaskRow()
        (opus.completed, opus.scored, opus.scoreSum, opus.successes) = (2, 2, 141, 1)
        opus.scoreBins[4] = 1
        opus.scoreBins[9] = 1
        (opus.fixAttempts, opus.fixFailures, opus.recovered, opus.recoverAttempts) = (3, 1, 1, 2)
        var sonnet = ReportTaskRow()
        (sonnet.completed, sonnet.scored, sonnet.scoreSum, sonnet.stalls) = (1, 1, 23, 1)
        sonnet.scoreBins[2] = 1
        (sonnet.fixAttempts, sonnet.fixFailures) = (4, 4)
        content.taskStats = ["claude-opus-5-5": [.claudeCode: opus], "claude-sonnet-5-5": [.claudeDesktop: sonnet]]
        return (content, sample)
    }

    /// The example file as compact text, with the floats Python writes as `0.0` and `1.0` in the client's form.
    static func exampleText(_ name: String) throws -> String {
        let data = try Data(contentsOf: Fixtures.root.appendingPathComponent("report-examples/\(name).json"))
        var compact = ""
        var inString = false
        var escaped = false
        for character in String(decoding: data, as: UTF8.self) {
            if inString {
                compact.append(character)
                if escaped { escaped = false } else if character == "\\" { escaped = true } else if character == "\"" { inString = false }
            } else if character == "\"" {
                inString = true
                compact.append(character)
            } else if !character.isWhitespace {
                compact.append(character)
            }
        }
        return compact.replacingOccurrences(of: #"(?<=[:\[,]-?\d{1,20})\.0(?=[,}\]])"#, with: "", options: .regularExpression)
    }

    @Test func theHTTPExampleIsRebuiltExactly() throws {
        let (content, sample) = Self.exampleInputs()
        let options = ReportOptions(sections: Set(ReportSection.allCases), metric: .output, redline: 2000,
                                    extensions: ReportExtensions(taskScore: true))
        let body = String(decoding: ReportBody.data(Self.envelope, content, sample, options), as: UTF8.self)
        #expect(body == (try Self.exampleText("report-taskscore")))
    }

    @Test func theWebSocketExampleIsRebuiltExactly() throws {
        let (content, sample) = Self.exampleInputs()
        var envelope = Self.envelope
        envelope.realtime = ReportRealtime(observationAgeMs: 18)
        var options = ReportOptions(sections: Set(ReportSection.allCases), metric: .output, redline: 2000,
                                    extensions: ReportExtensions(taskScore: true))
        options.messageLimit = ReportStreamSettings.standard.maxMessageBytes
        let body = String(decoding: ReportBody.data(envelope, content, sample, options), as: UTF8.self)
        #expect(body == (try Self.exampleText("report-taskscore-websocket")))
    }

    // MARK: - Body rules

    @Test func inactiveReportsCarryNothingOfTheExtension() throws {
        let (content, sample) = Self.exampleInputs()
        for options in [
            ReportOptions(sections: Set(ReportSection.allCases)),
            ReportOptions(sections: [.sources, .cost], extensions: ReportExtensions(taskScore: true)),
            ReportOptions(sections: Set(ReportSection.allCases), maxModels: 0, extensions: ReportExtensions(taskScore: true)),
        ] {
            let text = String(decoding: ReportBody.data(Self.envelope, content, sample, options), as: UTF8.self)
            #expect(!text.contains("taskScore") && !text.contains("taskStats"))
            #expect(!text.contains("claude-sonnet-5-5"), "a model only the extension lists")
        }
    }

    /// The size rule drops the per-model breakdowns first, then `models` with its task fields; the top-level object
    /// stays.
    @Test func theTopLevelObjectSurvivesTheSizeRule() throws {
        let (content, sample) = Self.exampleInputs()
        var options = ReportOptions(sections: Set(ReportSection.allCases), metric: .output, redline: 2000,
                                    extensions: ReportExtensions(modelHarnessRequests: true, executionModes: true,
                                                                 taskScore: true))
        let full = ReportBody.data(Self.envelope, content, sample, options)
        let fullBody = try #require(try JSONSerialization.jsonObject(with: full) as? [String: Any])
        #expect((fullBody["models"] as? [[String: Any]])?.first?["harnessRequests"] != nil)
        options.bodyLimit = full.count - 1
        let trimmed = try #require(try JSONSerialization.jsonObject(
            with: ReportBody.data(Self.envelope, content, sample, options)) as? [String: Any])
        let models = try #require(trimmed["models"] as? [[String: Any]])
        #expect(models.first?["harnessRequests"] == nil && models.first?["modeUsage"] == nil)
        #expect(models.allSatisfy { $0["taskScore"] != nil && $0["taskStats"] != nil })
        options.bodyLimit = 2000
        let small = try #require(try JSONSerialization.jsonObject(
            with: ReportBody.data(Self.envelope, content, sample, options)) as? [String: Any])
        #expect(small["models"] == nil)
        #expect(Self.same(try #require(small["taskScore"]), ["score": 92, "form": "live", "thinkingTokS": 38.6]))
        let keys = ReportBody.fitted(Self.envelope, content, sample, options).json
        if case .object(let members) = keys { #expect(members.last?.key == "taskScore") }
    }

    /// An entry that only the extension lists has zero usage: an empty Harness breakdown and no mode rows.
    @Test func anEntryOfTheExtensionHasNoUsageBreakdown() throws {
        let (content, sample) = Self.exampleInputs()
        let options = ReportOptions(sections: [.models], extensions: ReportExtensions(
            modelHarnessRequests: true, executionModes: true, taskScore: true))
        let body = try #require(try JSONSerialization.jsonObject(
            with: ReportBody.data(Self.envelope, content, sample, options)) as? [String: Any])
        let sonnet = try #require((body["models"] as? [[String: Any]])?.first { $0["id"] as? String == "claude-sonnet-5-5" })
        #expect((sonnet["harnessRequests"] as? [Any])?.isEmpty ?? true)
        #expect(sonnet["modeUsage"] == nil)
    }

    /// Bodies with the extension pass the reference's schema, its taskScore checks and its conformance rules.
    @Test(.enabled(if: ReferencePython.available)) func referenceAcceptsTheBodies() throws {
        var bodies: [Data] = []
        let (content, sample) = Self.exampleInputs()
        for maxModels in [16, 2, 1] {
            for extensions in [ReportExtensions(taskScore: true),
                               ReportExtensions(modelHarnessRequests: true, executionModes: true, taskScore: true)] {
                let options = ReportOptions(sections: Set(ReportSection.allCases), maxModels: maxModels,
                                            extensions: extensions)
                bodies.append(ReportBody.data(Self.envelope, content, sample, options))
            }
        }
        for name in Self.names("assembly") {
            let (content, sample, options) = try Self.assembly(try #require(try Self.spec("assembly", name)["input"]
                as? [String: Any]))
            bodies.append(ReportBody.data(Self.envelope, content, sample, options))
        }
        var small = ReportOptions(sections: Set(ReportSection.allCases), extensions: ReportExtensions(taskScore: true))
        small.bodyLimit = 1500
        bodies.append(ReportBody.data(Self.envelope, content, sample, small))
        let failures = try ReferencePython.strictErrors(bodies)
        #expect(failures.isEmpty, "\(failures)")
    }
}
