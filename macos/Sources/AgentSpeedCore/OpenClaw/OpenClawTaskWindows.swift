import Foundation

/// Every transcript window of one OpenClaw agent database read whole with the task composer (METRICS 4.14, 6.9, 6.11),
/// in window order with the shared dedupe set: the task records of history, the end-to-end fixture and the probe's
/// task dump.
enum OpenClawTaskWindows {
    struct Window {
        let agentId: String
        let info: OpenClawWindow
        var parser: OpenClawParser
        var tasks: TaskComposer
        /// The spawning window (METRICS 4.14), when it is a window of the same database.
        var parentId: String?
    }

    /// nil when the database cannot be read or has no session tables.
    static func read(path: String, deduper: inout Deduper, fileID: (String) -> FileID) -> [Window]? {
        guard let database = try? OpenClawDatabase(path: path) else { return nil }
        var windows: [Window] = []
        do {
            try database.read {
                let infos = try database.windows()
                let current = try database.currentSessions()
                let present = Set(infos.map(\.sessionId))
                for info in infos {
                    let agentId = OpenClawSource.agentId(path, info.sessionId)
                    let id = fileID(agentId)
                    var parser = OpenClawParser(agentId: agentId, fileID: id, window: info)
                    var tasks = TaskComposer(source: .openclaw, keepsEveryTask: true)
                    var reader = TaskLineReader(source: .openclaw)
                    reader?.mirrorWindow = info.mirrorsTurns
                    try database.rows(session: info.sessionId, after: .min, maxLineBytes: FileTailer.defaultMaxLineBytes,
                                      readsTools: false, tasks: &reader) { row in
                        guard let entry = row.entry else { return }
                        parser.consume(entry, deduper: &deduper) { _ in }
                        guard let t = tasks.feed(row.task, isDuplicate: { deduper.isOwnedByOtherFile($0, for: id) }) else {
                            return
                        }
                        let effort = parser.state.modes.attribution(for: .effort)
                        tasks.note(t: t, model: parser.state.model, effort: effort.isUnknown ? nil : effort.mode)
                    }
                    let parent = info.spawnedBy.flatMap { current[$0] }.flatMap { present.contains($0) ? $0 : nil }
                    windows.append(Window(agentId: agentId, info: info, parser: parser, tasks: tasks,
                                          parentId: parent.map { OpenClawSource.agentId(path, $0) }))
                }
            }
        } catch {
            return nil
        }
        return windows
    }
}
