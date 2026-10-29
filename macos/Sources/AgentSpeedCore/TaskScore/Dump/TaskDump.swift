import Foundation

/// The probe's task dumps (METRICS 6.9 *Dumps*), in `reference.py`'s format for `compare_dumps.py`.
public enum TaskDump {
    /// `--dump-tasks`: a `record` row per task record of every log of a scored source read whole (Codex archives
    /// included), in startup order with one dedupe set; a `calibration` row per source; the `comparison` over
    /// [`first`, `last`] (default: the records' days), every source or `source`; then a `summary`.
    public static func tasks(configuration: MonitorConfiguration, first: String?, last: String?, source: SourceKind?,
                             utc: Bool) -> String {
        let started = DispatchTime.now().uptimeNanoseconds
        let analysisMs = Clock.milliseconds(Date())
        let timeZone = utc ? TimeZone(identifier: "UTC")! : .current
        var deduper = Deduper(capacity: 1_000_000_000)
        var nextFileID: FileID = 0
        var records: [TaskRecord] = []
        var agents = 0
        var stitched = 0
        for item in startupFiles(configuration) {
            if let family = item.family {
                let parser = TaskTranscriptReading.read(path: item.file.path, family: family, root: item.root,
                                                        fileID: nextFileID, deduper: &deduper)
                nextFileID &+= 1
                guard let tasks = parser.tasks else { continue }
                agents += 1
                stitched += tasks.stitched
                records += TaskTranscriptReading.records(of: tasks, agentId: item.file.path, source: parser.state.source,
                                                         lastLineMs: parser.state.lastLineMs, analysisMs: analysisMs,
                                                         timeZone: timeZone)
            } else {
                let windows = OpenClawTaskWindows.read(path: item.file.path, deduper: &deduper) { _ in
                    defer { nextFileID &+= 1 }
                    return nextFileID
                } ?? []
                for window in windows {
                    agents += 1
                    stitched += window.tasks.stitched
                    records += TaskTranscriptReading.records(of: window.tasks, agentId: window.agentId, source: .openclaw,
                                                             lastLineMs: window.parser.state.lastLineMs,
                                                             analysisMs: analysisMs, timeZone: timeZone)
                }
            }
        }
        records.sort {
            $0.agentId == $1.agentId ? $0.ordinal < $1.ordinal : AgentParents.ordinal($0.agentId, precedes: $1.agentId)
        }
        var output = ""
        for record in records {
            output += "{\"record\":" + TaskDumpJSON.record(record, agentId: record.agentId) + "}\n"
        }
        for kind in SourceKind.allCases {
            let part = records.filter { $0.source == kind }
            guard !part.isEmpty else { continue }
            var row = TaskDumpJSON.Object()
            row.add("source", kind.rawValue)
            row.add("models", raw: TaskDumpJSON.models(TaskModels.calibrate(part)))
            output += "{\"calibration\":" + row.text + "}\n"
        }
        let days = records.map(\.day).filter { !$0.isEmpty }.sorted()
        let firstDay = first ?? days.first ?? "0001-01-01"
        let lastDay = last ?? days.last ?? "9999-12-31"
        let (bySource, byModel) = TaskDumpJSON.comparison(TaskComparison.compare(records, first: firstDay, last: lastDay,
                                                                                 source: source))
        var comparison = TaskDumpJSON.Object()
        comparison.add("first", firstDay)
        comparison.add("last", lastDay)
        comparison.add("source", source?.rawValue)
        comparison.add("bySource", raw: bySource)
        comparison.add("byModel", raw: byModel)
        output += "{\"comparison\":" + comparison.text + "}\n"
        var summary = TaskDumpJSON.Object()
        summary.add("agents", agents)
        summary.add("records", records.count)
        summary.add("complete", records.filter(\.complete).count)
        summary.add("scored", records.filter { $0.score != nil }.count)
        summary.add("stitched", stitched)
        summary.add("analysisMs", analysisMs)
        summary.add("seconds", (Double(DispatchTime.now().uptimeNanoseconds - started) / 1e8).rounded() / 10)
        output += "{\"summary\":" + summary.text + "}\n"
        return output
    }

    /// The `task` rows of the agents and the `taskSets` row at `nowMs` (prior coefficients: dumps analyse no history).
    static func valueRows(_ agents: [TaskValueReport.Agent], nowMs: Int64) -> [String] {
        let report = TaskValueReport(agents: agents, now: nowMs, models: [:])
        var rows = report.rows.map { "{\"task\":" + TaskDumpJSON.task($0, parentId: $0.agent.parentId) { $0 } + "}" }
        rows.append("{\"taskSets\":" + TaskDumpJSON.taskSets(report) { $0 } + "}")
        return rows
    }

    /// The logs of the scored sources and the OpenClaw databases (`family` nil), in startup order.
    static func startupFiles(_ configuration: MonitorConfiguration)
        -> [(file: DiscoveredFile, family: TranscriptFamily?, root: String?)] {
        var files: [(file: DiscoveredFile, family: TranscriptFamily?, root: String?)] = []
        var seen: Set<String> = []
        for (family, root) in HistoryAnalyzer.roots(configuration) where family.sources.contains(where: TaskScoring.scores) {
            guard TranscriptDiscovery.isAvailable(root: root) else { continue }
            var index = DirectoryIndex(root: root, names: family.names, maxCandidates: .max)
            for path in index.scan() where seen.insert(path).inserted {
                guard let stat = family.stat(path) else { continue }
                files.append((DiscoveredFile(path: path, stat: stat), family, root))
            }
        }
        if configuration.readsOpenclaw {
            for path in configuration.resolvedOpenclawRoots.flatMap(OpenClawHome.databases(in:))
            where seen.insert(path).inserted {
                guard let stat = FileStat.lstat(path), stat.isRegularFile else { continue }
                files.append((DiscoveredFile(path: path, stat: stat), nil, nil))
            }
        }
        return files.sorted { TranscriptDiscovery.startupOrder($0.file, $1.file) }
    }
}
