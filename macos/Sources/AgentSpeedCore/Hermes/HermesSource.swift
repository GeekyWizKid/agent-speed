import Foundation

/// Incremental, read-only log tailing. Multiple sessions may share a log, and rotations retain response identity.
final class HermesSource {
    struct Update {
        var upserts: [(ResponseUpsert, String)] = []
        var agents: [String: AgentState] = [:]
        var isEmpty: Bool { upserts.isEmpty && agents.isEmpty }
    }
    struct LogFile {
        var path: String
        var directory: String
        var stat: FileStat
        /// Inode identity survives rotations; retaining old entries preserves history after logs expire.
        var historyKey: String { "hermes-log:\(directory):\(stat.identity.device):\(stat.identity.inode)" }
    }
    let rootPath: String
    private let fileID: FileID
    private let parser: HermesLine
    private var tailers: [String: FileTailer] = [:]
    private var stats: [String: FileStat] = [:]
    private var states: [String: AgentState] = [:]
    private var sinceMs: Int64 = 0
    private(set) var exists = false
    private(set) var unreadableFiles = 0
    private(set) var partialHistory = false
    private(set) var bytesRead: Int64 = 0
    var isReady: Bool { exists }
    var watchedPaths: [String] { Self.logDirectories(root: rootPath).filter(TranscriptDiscovery.isAvailable(root:)) }

    init(root: String, fileID: FileID, timeZone: TimeZone = .current) {
        rootPath = root
        self.fileID = fileID
        parser = HermesLine(timeZone: timeZone)
    }

    static func logDirectories(root: String) -> [String] {
        // The configured root may be an alias, as with other sources; discovered child folders may not be symlinks.
        let root = URL(fileURLWithPath: root, isDirectory: true).standardizedFileURL.resolvingSymlinksInPath()
        guard FileStat.lstat(root.path)?.isDirectory == true else { return [] }
        if root.lastPathComponent == "logs" { return [root.path] }
        var paths: [String] = []
        let logs = root.appendingPathComponent("logs")
        if FileStat.lstat(logs.path)?.isDirectory == true { paths.append(logs.path) }
        let profiles = root.appendingPathComponent("profiles")
        guard FileStat.lstat(profiles.path)?.isDirectory == true else { return paths }
        for name in ((try? FileManager.default.contentsOfDirectory(atPath: profiles.path)) ?? []).sorted() {
            let profile = profiles.appendingPathComponent(name)
            guard FileStat.lstat(profile.path)?.isDirectory == true else { continue }
            let logs = profile.appendingPathComponent("logs")
            if FileStat.lstat(logs.path)?.isDirectory == true { paths.append(logs.path) }
        }
        return paths
    }

    static func files(root: String) -> [LogFile] {
        var files: [LogFile] = []
        for directory in logDirectories(root: root) {
            let names = (try? FileManager.default.contentsOfDirectory(atPath: directory)) ?? []
            for name in names where isLogName(name) {
                let path = (directory as NSString).appendingPathComponent(name)
                guard let stat = FileStat.lstat(path), stat.isRegularFile else { continue }
                files.append(LogFile(path: path, directory: directory, stat: stat))
            }
        }
        return files.sorted { lhs, rhs in
            if lhs.stat.modifiedNs != rhs.stat.modifiedNs { return lhs.stat.modifiedNs < rhs.stat.modifiedNs }
            return lhs.path < rhs.path
        }
    }

    static func isLogName(_ name: String) -> Bool {
        if name == "agent.log" { return true }
        guard name.hasPrefix("agent.log.") else { return false }
        let suffix = name.dropFirst("agent.log.".count)
        return !suffix.isEmpty && suffix.utf8.allSatisfy { (48...57).contains($0) }
    }

    @discardableResult
    func refreshAvailability() -> Bool {
        let available = !watchedPaths.isEmpty
        defer { exists = available }
        return exists != available
    }

    func seed(todayStartMs: Int64, deduper: inout Deduper) -> Update {
        sinceMs = todayStartMs - TranscriptSession.startupLookbackMs
        return refresh(deduper: &deduper)
    }

    func refresh(deduper: inout Deduper) -> Update {
        refreshAvailability()
        var update = Update()
        let files = Self.files(root: rootPath)
        let paths = Set(files.map(\.path))
        tailers = tailers.filter { paths.contains($0.key) }
        stats = stats.filter { paths.contains($0.key) }
        unreadableFiles = 0
        var records: [ResponseRecord] = []
        for file in files {
            guard stats[file.path] != file.stat else { continue }
            let start: FileTailer.Start = file.stat.size > FileTailer.largeFileBytes ? .tail(FileTailer.tailWindowBytes) : .beginning
            if tailers[file.path] == nil && file.stat.size > FileTailer.largeFileBytes { partialHistory = true }
            var tailer = tailers[file.path] ?? FileTailer(path: file.path, start: start, maxLineBytes: 64 * 1024)
            let result = tailer.read { bytes in
                if let record = parser.record(bytes, logDirectory: file.directory), record.endMs >= sinceMs { records.append(record) }
            }
            if result.failed { unreadableFiles += 1 } else { stats[file.path] = result.stat }
            if tailer.oversizedLines > 0 { partialHistory = true }
            bytesRead += Int64(result.bytesRead)
            tailers[file.path] = tailer
        }
        for record in records.sorted(by: { $0.endMs != $1.endMs ? $0.endMs < $1.endMs : $0.key < $1.key }) {
            guard deduper.claim(record.key, for: fileID) == .new else { continue }
            var state = states[record.agentId] ?? AgentState(source: .hermes)
            if record.endMs >= (state.lastLineMs ?? 0) {
                state.model = record.model
                state.project = "Hermes"
                state.noteLine(at: record.endMs)
                state.setPhase(.idle, at: record.endMs)
                states[record.agentId] = state
                update.agents[record.agentId] = state
            }
            update.upserts.append((ResponseUpsert(record: record, isCompletion: true), "Hermes"))
        }
        return update
    }

    func activeSessionCount(todayStartMs: Int64) -> Int {
        states.values.filter { ($0.lastLineMs ?? 0) >= todayStartMs }.count
    }
}
