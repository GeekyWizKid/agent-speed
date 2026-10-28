import Foundation

/// One transcript file being followed: its reader, its parser and the last observed stat.
final class TrackedFile {
    let path: String
    let family: TranscriptFamily
    /// The root the file was discovered under (omp derives sub-agents from it).
    let root: String
    let fileID: FileID
    var reader: TranscriptReader
    var parser: TranscriptParser
    /// For an Antigravity database, the `.db` with its `-wal` folded in (`FileStat.databaseStat`); for a Factory Droid
    /// session, the transcript with its sidecar's modification time (`DroidPaths.stat`).
    var lastStat: FileStat
    /// A whole-file read of this file runs off the session queue (METRICS 5.3).
    var wholeReadInFlight = false
    /// The file changed again while that read ran; read once more when it completes.
    var wholeReadAgain = false
    /// Uptime of the last database read; a database is read at most once per `TranscriptSession.databaseReadIntervalNs`.
    var lastReadNs: UInt64 = 0
    /// The database changed while a read was throttled; read it once the interval has passed.
    var isDirty = false
    /// The current task's evaluation, kept until the task stream or the calibration changes (METRICS 6.9).
    var taskStatus: (version: Int, models: Int, status: AgentTaskStatus)?

    init(
        path: String,
        family: TranscriptFamily,
        root: String,
        fileID: FileID,
        stat: FileStat,
        start: FileTailer.Start,
        maxLineBytes: Int = FileTailer.defaultMaxLineBytes
    ) {
        self.path = path
        self.family = family
        self.root = root
        self.fileID = fileID
        lastStat = stat
        reader = TranscriptReader(path: path, start: start, maxLineBytes: maxLineBytes, readsHeaderFirst: family == .omp || family == .pi)
        parser = TranscriptParser(family: family, agentId: path, fileID: fileID, root: root, tasks: .live)
    }

    /// Parser state restarts after truncation or replacement; the global dedupe set is kept.
    func resetParser() {
        parser = TranscriptParser(family: family, agentId: path, fileID: fileID, root: root, tasks: .live)
        taskStatus = nil
    }

    /// The current task's status with `models` (the calibration of version `modelsVersion`), evaluated again only
    /// after the stream changed.
    func currentTaskStatus(models: TaskModels, modelsVersion: Int) -> AgentTaskStatus? {
        guard let tasks = parser.tasks else { return nil }
        if let cached = taskStatus, cached.version == tasks.version, cached.models == modelsVersion { return cached.status }
        let status = AgentTaskStatus(events: tasks.events, now: nil, models: models)
        taskStatus = (tasks.version, modelsVersion, status)
        return status
    }

    /// A Factory Droid session also changes when its sidecar does, whatever its modification time (METRICS 4.16).
    func hasChanged(comparedTo stat: FileStat) -> Bool {
        stat.size != lastStat.size || stat.modifiedNs != lastStat.modifiedNs || stat.identity != lastStat.identity
            || parser.sidecarHasChanged
    }
}
