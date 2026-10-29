import Foundation

/// The sequential half of the task composer (METRICS 6.9 *Composing the stream*): one agent's event stream, built from
/// the `TaskLine`s of its lines in order, right after the source's parser has read each line (so the dedupe set already
/// holds the line's keys). It decides what depends on other files and on the stream itself: duplicates, withdrawal,
/// which call a result completes (first result only), and the sessions stitched back (TASK-PARSERS 4.3).
///
/// `keepsEveryTask` (history) keeps the whole stream for the records of 6.11; otherwise (the live engine) the events
/// before the current task are dropped once no withdrawal can bring them back, and a watched task among them completes
/// for live reporting (METRICS 11.10 *Counting*).
struct TaskComposer: Sendable {
    private struct CallRef: Sendable {
        /// Index of the action in the whole stream (`base` + position).
        var index: Int
        var info: TaskCommandInfo?
        var continues: String?
        var cwd: String?
    }

    private enum Entry: Sendable {
        case call(CallRef)
        case script([CallRef])
    }

    /// A command whose result left a session running: its output so far, until an exit code.
    private struct Running: Sendable {
        var index: Int
        var info: TaskCommandInfo?
        var cwd: String?
        var masked: Bool
        var texts: [String]
    }

    let source: SourceKind
    let keepsEveryTask: Bool
    private(set) var events: [TaskEvent] = []
    /// The stream index of `events[0]`.
    private var base = 0
    /// A prompt or command that can still be withdrawn: its kind and stream index.
    private var pending: (kind: TaskPromptLine.Kind, index: Int)?
    /// The latest task-starting prompt not trimmed yet (live only).
    private var startCandidate: Int?
    /// By the reader's call handle.
    private var calls: [Int: Entry] = [:]
    private var sessions: [String: Running] = [:]
    private(set) var notes: [TaskModelNote] = []
    /// Sessions stitched back with an exit code.
    private(set) var stitched = 0
    /// Bumped on every change of `events`.
    private(set) var version = 0
    /// Live reporting's watch while lines are fed (METRICS 11.10 *Watch*); nil while it is off or seeding runs.
    var watch: ReportTaskWatchContext?
    /// Watched tasks by the stream index of their first event.
    private var marks = ReportTaskWatchMarks<Int>()
    /// Watched tasks that completed, until the owner takes them.
    private(set) var completions: [TaskCompletion] = []
    /// `notes.count` when `startCandidate` was set: the start line's note comes next (live trimming of the notes).
    private var startCandidateNotes = 0

    init(source: SourceKind, keepsEveryTask: Bool) {
        self.source = source
        self.keepsEveryTask = keepsEveryTask
    }

    /// Applies one line. `isDuplicate` tells whether another file owns an assistant key. Returns the line's time when it
    /// is a line of the stream (model and effort are then noted after it), else nil.
    @discardableResult
    mutating func feed(_ line: TaskLine, isDuplicate: (String) -> Bool) -> Int64? {
        guard case .line(let t, let ops) = line else { return nil }
        for index in completions.indices { completions[index].awaitsNote = false }
        var duplicate = false
        for op in ops {
            switch op {
            case .prompt(let kind, let at, let continuation):
                lineKind(kind, t: at, continuation: continuation)
            case .plainPrompt(let at, let continuation):
                prompt(at, continuation: continuation)
                if !continuation { setStartCandidate(base + events.count - 1) }
            case .assistant(let key):
                duplicate = key.map { !$0.isEmpty && isDuplicate($0) } ?? false
                if pending != nil, duplicate { withdraw() }
                pending = nil
            case .calls(let prepared):
                guard !duplicate else { continue }
                for call in prepared where calls[call.handle] == nil {
                    calls[call.handle] = .call(add(call))
                }
            case .script(let script):
                guard calls[script.handle] == nil else { continue }
                var refs: [CallRef] = []
                for call in script.calls { refs.append(add(call)) }
                calls[script.handle] = .script(refs)
            case .result(let result):
                apply(result)
            case .notice(let session, let exitCode, let at):
                finish(session, outcome: TaskOutcome(exitCode: exitCode, failed: exitCode != 0, t: nil), text: nil, t: at)
            }
        }
        trimIfPossible()
        return t
    }

    /// Notes the agent's model and effort after a line of the stream. A task the line completed takes this note's
    /// model when it is the last one at most its end.
    mutating func note(t: Int64, model: String?, effort: String?) {
        for index in completions.indices where completions[index].awaitsNote {
            completions[index].awaitsNote = false
            if t <= completions[index].endMs { completions[index].model = model }
        }
        if let last = notes.last, TaskPythonText.same(last.model, model), TaskPythonText.same(last.effort, effort),
           t >= last.t { return }
        notes.append(TaskModelNote(t: t, model: model, effort: effort))
    }

    /// The current task's events (everything read, METRICS 6.9 *Event stream*).
    var currentTask: TaskSpan? { TaskSplit.current(events, now: nil) }

    // MARK: - Prompts and withdrawal

    private mutating func lineKind(_ kind: TaskPromptLine.Kind, t: Int64, continuation: Bool) {
        switch kind {
        case .prompt, .command:
            let isContinuation = kind == .prompt && continuation
            prompt(t, continuation: isContinuation)
            let index = base + events.count - 1
            pending = (kind, index)
            if !isContinuation { setStartCandidate(index) }
        case .commandOutput:
            if pending?.kind == .command { withdraw() }
            pending = nil
        }
    }

    private mutating func prompt(_ t: Int64, continuation: Bool) {
        let starts = !continuation || events.isEmpty
        events.append(.prompt(t, continuation: continuation))
        version &+= 1
        if starts { marks.started(base + events.count - 1, firstEventMs: t, context: watch) }
    }

    private mutating func setStartCandidate(_ index: Int) {
        startCandidate = index
        startCandidateNotes = notes.count
    }

    private mutating func withdraw() {
        guard let pending else { return }
        let local = pending.index - base
        // A withdrawn prompt never started a task; the previous task keeps its own watch.
        marks.unwatch(pending.index)
        if startCandidate == pending.index { startCandidate = nil }
        guard local >= 0, local < events.count else { return }
        events.remove(at: local)
        version &+= 1
        if startCandidate == nil, !keepsEveryTask { settleEarlierPrompt() }
        guard local < events.count else { return }
        marks.rekey { $0 > pending.index ? $0 - 1 : $0 }
        // Not the last event (never expected): later stream indices move down by one.
        func shift(_ ref: inout CallRef) { if ref.index > pending.index { ref.index -= 1 } }
        for (id, entry) in calls {
            switch entry {
            case .call(var ref):
                shift(&ref)
                calls[id] = .call(ref)
            case .script(var refs):
                for i in refs.indices { shift(&refs[i]) }
                calls[id] = .script(refs)
            }
        }
        for (key, var running) in sessions where running.index > pending.index {
            running.index -= 1
            sessions[key] = running
        }
        if let candidate = startCandidate, candidate > pending.index { startCandidate = candidate - 1 }
    }

    // MARK: - Calls and results

    private mutating func add(_ call: TaskPreparedCall) -> CallRef {
        let starts = events.isEmpty
        events.append(.action(call.t, call.action))
        version &+= 1
        if starts { marks.started(base + events.count - 1, firstEventMs: call.t, context: watch) }
        return CallRef(index: base + events.count - 1, info: call.info, continues: call.continues, cwd: call.cwd)
    }

    private mutating func apply(_ result: TaskPreparedResult) {
        guard let entry = calls[result.handle], let answer = result.answer else { return }
        switch (entry, answer) {
        case (.script(let refs), .script(let mapped)):
            calls[result.handle] = nil
            for (index, prepared) in mapped where index < refs.count {
                apply(prepared, to: refs[index], t: result.t)
            }
        case (.call(let ref), .call(let prepared)):
            // The reader sends a call's first result only, so an answered call needs no entry.
            calls[result.handle] = nil
            apply(prepared, to: ref, t: result.t)
        default:
            return
        }
    }

    private mutating func apply(_ answer: TaskPreparedAnswer, to ref: CallRef, t: Int64) {
        update(ref.index) { $0.outcome = answer.outcome }
        if let session = answer.session {
            sessions[session] = Running(index: ref.index, info: ref.info, cwd: ref.cwd, masked: answer.masked,
                                        texts: [answer.text ?? ""])
        } else if ref.info != nil {
            update(ref.index) { $0.failures = answer.failures }
        }
        if let key = ref.continues, sessions[key] != nil {
            finish(key, outcome: answer.outcome, text: answer.text, t: t)
        }
    }

    /// A continuation's result (or a Claude notice, which has no text) for session `key`: its output joins the
    /// command's; an exit code ends it.
    private mutating func finish(_ key: String, outcome: TaskOutcome, text: String?, t: Int64) {
        guard var running = sessions[key] else { return }
        if let text { running.texts.append(text) }
        guard let exitCode = outcome.exitCode, let failed = outcome.failed else {
            sessions[key] = running
            return
        }
        let final = TaskOutcome(exitCode: exitCode, failed: running.masked ? nil : failed, t: t)
        update(running.index) { $0.outcome = final }
        if let info = running.info {
            let joined = running.texts.joined(separator: source == .codex ? "" : "\n")
            let failures = TaskRunFailures(TaskParsers.failures(info, output: joined, cwd: running.cwd, failed: final.failed))
            update(running.index) { $0.failures = failures }
        }
        sessions[key] = nil
        stitched += 1
    }

    private mutating func update(_ index: Int, _ change: (inout TaskAction) -> Void) {
        let local = index - base
        guard local >= 0, local < events.count, case .action(var action) = events[local].body else { return }
        change(&action)
        events[local].body = .action(action)
        version &+= 1
    }

    // MARK: - Live trimming

    /// Drops the events before the current task once its prompt can no longer be withdrawn; the watched tasks among
    /// them complete (METRICS 11.10 *Counting*, rule 1), and the notes no remaining task can take go with them.
    private mutating func trimIfPossible() {
        guard !keepsEveryTask, pending == nil, let start = startCandidate else { return }
        let local = start - base
        guard local > 0, local < events.count else { return }
        if !marks.isEmpty { complete(Array(events[..<local]), firstIndex: base, awaitsNote: true) }
        events.removeFirst(local)
        base = start
        startCandidate = nil
        notes.removeFirst(min(notes.count, max(0, startCandidateNotes - 1)))
        // What only the dropped tasks still need: their calls without results, scripts and sessions.
        calls = calls.filter { _, entry in
            switch entry {
            case .call(let ref): ref.index >= start
            case .script(let refs): refs.contains { $0.index >= start }
            }
        }
        sessions = sessions.filter { $0.value.index >= start }
    }

    // MARK: - Live reporting (METRICS 11.10)

    /// After a withdrawal left no start candidate: the latest task-starting prompt still kept is no longer withdrawable,
    /// so the tasks before it may complete.
    private mutating func settleEarlierPrompt() {
        guard let local = events.lastIndex(where: { $0.body == .prompt(continuation: false) }), local > 0 else { return }
        startCandidate = base + local
        startCandidateNotes = 0
    }

    var hasWatchedTasks: Bool { !marks.isEmpty }

    /// The agent has been quiet for 30 minutes (METRICS 11.10 *Counting*, rule 2): every watched task completes.
    mutating func completeWatchedTasks() {
        guard !marks.isEmpty else { return }
        complete(events, firstIndex: base, awaitsNote: false)
    }

    /// A break: every task is unwatched and the completions not taken yet are dropped.
    mutating func clearWatch() {
        marks.clear()
        completions.removeAll()
    }

    mutating func takeCompletions() -> [TaskCompletion] {
        defer { completions.removeAll() }
        return completions
    }

    /// The watched tasks among `events` (whose first has stream index `firstIndex`) complete: each is unwatched, and one
    /// with an action is evaluated as its 6.11 record, with the model of the last note at most its end.
    private mutating func complete(_ events: [TaskEvent], firstIndex: Int, awaitsNote: Bool) {
        for task in TaskSplit.split(events) {
            guard let epoch = marks.complete(firstIndex + task.events.startIndex),
                  task.events.contains(where: { !$0.isPrompt }) else { continue }
            let end = TaskSplit.end(task)
            let model = notes.last(where: { $0.t <= end })?.model
            completions.append(TaskCompletion(epoch: epoch, model: model, record: ReportTaskRecord(events: task.events),
                                              endMs: end, awaitsNote: awaitsNote))
        }
    }
}

/// A watched task that completed (METRICS 11.10 *Counting*): its record so far and the raw model of its last note.
struct TaskCompletion: Equatable, Sendable {
    var epoch: Int
    var model: String?
    var record: ReportTaskRecord
    var endMs: Int64
    /// Completed by the line just fed: the note after it may still name the model.
    var awaitsNote = false
}
