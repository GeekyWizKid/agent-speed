import Foundation

/// The side table of the task records (METRICS 6.11 *Store*, 8). Created on every open like `contribution_unpriced`
/// (`user_version` stays 3 and older builds ignore it); written with each file's contributions; read only where a row's
/// `version` equals its file's. No content is stored: classes, times, scores, counts and feature rows only.
extension HistoryDatabase {
    static let taskRecordsTableSQL = """
        CREATE TABLE IF NOT EXISTS task_records (
            file_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, source TEXT NOT NULL, model TEXT, effort TEXT,
            start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, day TEXT NOT NULL, complete INTEGER NOT NULL,
            state TEXT NOT NULL, score REAL, v REAL NOT NULL, c REAL NOT NULL, r REAL NOT NULL, e REAL NOT NULL,
            k REAL NOT NULL, counts TEXT NOT NULL, first_pass_ms INTEGER, success INTEGER NOT NULL,
            stalled INTEGER NOT NULL, snapshots TEXT NOT NULL, fix_rows TEXT NOT NULL, version INTEGER NOT NULL,
            PRIMARY KEY (file_id, ordinal)) WITHOUT ROWID;
        """

    func deleteTaskRecords(fileId: Int64) throws {
        let statement = try connection.prepare("DELETE FROM task_records WHERE file_id = ?1")
        statement.bind(1, fileId)
        try statement.run()
    }

    func saveTaskRecords(_ records: [TaskRecord], fileId: Int64, version: Int64) throws {
        guard !records.isEmpty else { return }
        let statement = try connection.prepare("""
            INSERT OR REPLACE INTO task_records (file_id, ordinal, source, model, effort, start_ms, end_ms, day, complete,
                state, score, v, c, r, e, k, counts, first_pass_ms, success, stalled, snapshots, fix_rows, version)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22, ?23)
            """)
        for record in records {
            statement.bind(1, fileId)
            statement.bind(2, Int64(record.ordinal))
            statement.bind(3, record.source.rawValue)
            statement.bind(4, record.model)
            statement.bind(5, record.effort)
            statement.bind(6, record.startMs)
            statement.bind(7, record.endMs)
            statement.bind(8, record.day)
            statement.bind(9, Int64(record.complete ? 1 : 0))
            statement.bind(10, record.state.rawValue)
            statement.bind(11, record.score)
            statement.bind(12, record.dims.v)
            statement.bind(13, record.dims.c)
            statement.bind(14, record.dims.r)
            statement.bind(15, record.dims.e)
            statement.bind(16, record.dims.k)
            statement.bind(17, TaskDumpJSON.counts(record.counts))
            statement.bind(18, record.firstPassMs)
            statement.bind(19, Int64(record.success ? 1 : 0))
            statement.bind(20, Int64(record.stalled ? 1 : 0))
            statement.bind(21, Self.rows(record.snapshots.map(\.values)))
            statement.bind(22, Self.rows(record.fixRows.map(\.values)))
            statement.bind(23, version)
            try statement.run()
        }
    }

    /// Files whose valid records include an incomplete task (METRICS 6.11 *Writes*).
    func filesWithOpenTasks() throws -> Set<Int64> {
        let statement = try connection.prepare("""
            SELECT DISTINCT t.file_id FROM task_records t JOIN files f ON f.id = t.file_id
            WHERE t.complete = 0 AND t.version = f.version
            """)
        var ids = Set<Int64>()
        try statement.forEachRow { ids.insert($0.int64(0)) }
        return ids
    }

    /// The valid records (METRICS 6.11 *Reading*), each named by its file's path. `rows`: with their snapshots and fix
    /// rows (calibration), otherwise without (the comparison). `completeOnly` and `source` narrow the read; `endingFrom`
    /// keeps records ending at or after that time.
    func taskRecords(rows: Bool, completeOnly: Bool = false, source: SourceKind? = nil,
                     endingFrom: Int64? = nil) throws -> [TaskRecord] {
        var conditions = ["t.version = f.version"]
        if completeOnly { conditions.append("t.complete = 1") }
        if source != nil { conditions.append("t.source = ?1") }
        if endingFrom != nil { conditions.append("t.end_ms >= ?2") }
        let statement = try connection.prepare("""
            SELECT f.path, t.ordinal, t.source, t.model, t.effort, t.start_ms, t.end_ms, t.day, t.complete, t.state, t.score,
                   t.v, t.c, t.r, t.e, t.k, t.counts, t.first_pass_ms, t.success, t.stalled,
                   \(rows ? "t.snapshots, t.fix_rows" : "'[]', '[]'")
            FROM task_records t JOIN files f ON f.id = t.file_id WHERE \(conditions.joined(separator: " AND "))
            """)
        if let source { statement.bind(1, source.rawValue) }
        if let endingFrom { statement.bind(2, endingFrom) }
        var records: [TaskRecord] = []
        try statement.forEachRow { row in
            guard let path = row.string(0), let source = row.string(2).flatMap(SourceKind.init(rawValue:)),
                  let state = row.string(9).flatMap(TaskState.init(rawValue:)) else { return }
            let counts = Self.counts(row.string(16))
            records.append(TaskRecord(
                agentId: path, source: source, day: row.string(7) ?? "", ordinal: Int(row.int64(1)),
                startMs: row.int64(5), endMs: row.int64(6), complete: row.int64(8) != 0, model: row.string(3),
                effort: row.string(4), state: state, score: row.optionalDouble(10),
                dims: TaskDimensions(v: row.double(11), c: row.double(12), r: row.double(13), e: row.double(14),
                                     k: row.double(15)),
                counts: counts, firstPassMs: row.optionalInt64(17), success: row.int64(18) != 0,
                stalled: row.int64(19) != 0,
                snapshots: rows ? Self.parseRows(row.string(20)).compactMap(TaskSnapshotRow.init(values:)) : [],
                fixRows: rows ? Self.parseRows(row.string(21)).compactMap(TaskFixRow.init(values:)) : []))
        }
        return records
    }

    /// Each scored source's prediction coefficients from its valid complete records (METRICS 6.11 *Calibration*):
    /// only the latest records (ties at the cut kept) are read with their rows.
    func taskCalibration() throws -> [SourceKind: TaskModels] {
        var models: [SourceKind: TaskModels] = [:]
        for source in SourceKind.allCases where TaskScoring.scores(source) {
            let all = try taskRecords(rows: false, source: source)
            guard !all.isEmpty else { continue }
            let ends = all.filter(\.complete).map(\.endMs).sorted(by: >)
            guard !ends.isEmpty else {
                // Records but none complete: every model keeps its prior (on 0 tasks).
                models[source] = TaskModels.calibrate(all)
                continue
            }
            let cut = ends[min(ends.count, TaskCalibration.maxTasks) - 1]
            models[source] = TaskModels.calibrate(try taskRecords(rows: true, completeOnly: true, source: source,
                                                                  endingFrom: cut))
        }
        return models
    }

    private static func rows(_ rows: [[Double]]) -> String {
        TaskDumpJSON.array(rows.map { TaskDumpJSON.array($0.map(TaskDumpJSON.number)) })
    }

    private static func parseRows(_ text: String?) -> [[Double]] {
        guard let text, let value = try? JSONParser.parse(text), let rows = value.array else { return [] }
        return rows.map { ($0.array ?? []).compactMap { $0.literal?.double } }
    }

    private static func counts(_ text: String?) -> TaskCounts {
        guard let text, let object = (try? JSONParser.parse(text))?.object else { return TaskCounts() }
        return TaskCounts(values: TaskCounts.names.map { Int(object[$0]?.int64 ?? 0) }) ?? TaskCounts()
    }
}
