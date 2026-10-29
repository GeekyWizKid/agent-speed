import Foundation

/// One transcript of a scored source read whole with the history rules and the task composer (METRICS 6.11 *Task
/// records*): the end-to-end fixture and the probe's task dump read files this way; the history analysis does the same
/// on its decoding threads.
enum TaskTranscriptReading {
    static func read(path: String, family: TranscriptFamily, root: String?, fileID: FileID,
                     deduper: inout Deduper) -> TranscriptParser {
        var parser = TranscriptParser(family: family, agentId: path, fileID: fileID, root: root, tasks: .history)
        var reader = TranscriptReader(path: path, start: .beginning)
        var taskReader = parser.taskReader
        let output = TranscriptDecoder.decode(family, reader: &reader, tasks: &taskReader)
        parser.taskReader = taskReader
        parser.consume(output.lines, taskLines: output.tasks, deduper: &deduper) { _ in }
        return parser
    }

    /// The records of an agent read whole.
    static func records(of tasks: TaskComposer, agentId: String, source: SourceKind, lastLineMs: Int64?,
                        analysisMs: Int64, timeZone: TimeZone) -> [TaskRecord] {
        var labeler = DayLabeler(timeZone: timeZone)
        return TaskRecord.records(events: tasks.events, analysisMs: analysisMs, lastLineMs: lastLineMs ?? 0,
                                  models: tasks.notes, agentId: agentId, source: source) { ms in
            DayLabeler.hasDay(ms) ? labeler.label(forMs: ms) : ""
        }
    }
}
