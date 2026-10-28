import Foundation

/// The sequential half of the task composer (METRICS 6.9 *Composing the stream*): one agent's event stream, built from
/// the `TaskLine`s of its lines in order, right after the source's parser has read each line (so the dedupe set already
/// holds the line's keys). It decides what depends on other files and on the stream itself: duplicates, withdrawal,
/// which call a result completes (first result only), and the sessions stitched back (TASK-PARSERS 4.3).
///
/// `keepsEveryTask` (history) keeps the whole stream for the records of 6.11; otherwise (the live engine) the events
/// before the current task are dropped once no withdrawal can bring them back.
struct TaskComposer: Sendable {
    private struct CallRef: Sendable {
        /// Index of the action in the whole stream (`base` + position).
        var index: Int
        var info: TaskCommandInfo?
        var continues: String?
        var cwd: String?
        var answered = false
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

    init(source: SourceKind, keepsEveryTask: Bool) {
        self.source = source
        self.keepsEveryTask = keepsEveryTask
    }

    /// Applies one line. `isDuplicate` tells whether another file owns an assistant key. Returns the line's time when it
    /// is a line of the stream (model and effort are then noted after it), else nil.
    @discardableResult
    mutating func feed(_ line: TaskLine, isDuplicate: (String) -> Bool) -> Int64? {
        guard case .line(let t, let ops) = line else { return nil }
        var duplicate = false
        for op in ops {
            switch op {
            case .prompt(let kind, let at, let continuation):
                lineKind(kind, t: at, continuation: continuation)
            case .plainPrompt(let at, let continuation):
                prompt(at, continuation: continuation)
                if !continuation { startCandidate = base + events.count - 1 }
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

    /// Notes the agent's model and effort after a line of the stream (history only).
    mutating func note(t: Int64, model: String?, effort: String?) {
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
            if !isContinuation { startCandidate = index }
        case .commandOutput:
            if pending?.kind == .command { withdraw() }
            pending = nil
        }
    }

    private mutating func prompt(_ t: Int64, continuation: Bool) {
        events.append(.prompt(t, continuation: continuation))
        version &+= 1
    }

    private mutating func withdraw() {
        guard let pending else { return }
        let local = pending.index - base
        if startCandidate == pending.index { startCandidate = nil }
        guard local >= 0, local < events.count else { return }
        events.remove(at: local)
        version &+= 1
        guard local < events.count else { return }
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
        events.append(.action(call.t, call.action))
        version &+= 1
        return CallRef(index: base + events.count - 1, info: call.info, continues: call.continues, cwd: call.cwd)
    }

    private mutating func apply(_ result: TaskPreparedResult) {
        guard let entry = calls[result.handle], let answer = result.answer else { return }
        switch (entry, answer) {
        case (.script(let refs), .script(let mapped)):
            calls[result.handle] = nil
            for (index, prepared) in mapped where index < refs.count {
                var ref = refs[index]
                apply(prepared, to: &ref, t: result.t)
            }
        case (.call(var ref), .call(let prepared)):
            guard !ref.answered else { return }
            apply(prepared, to: &ref, t: result.t)
            calls[result.handle] = .call(ref)
        default:
            return
        }
    }

    private mutating func apply(_ answer: TaskPreparedAnswer, to ref: inout CallRef, t: Int64) {
        ref.answered = true
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

    /// Drops the events before the current task once its prompt can no longer be withdrawn.
    private mutating func trimIfPossible() {
        guard !keepsEveryTask, pending == nil, let start = startCandidate else { return }
        let local = start - base
        guard local > 0, local < events.count else { return }
        events.removeFirst(local)
        base = start
        startCandidate = nil
        // What only the dropped tasks still need: answered calls, scripts and sessions of earlier tasks.
        calls = calls.filter { _, entry in
            switch entry {
            case .call(let ref): !ref.answered || ref.index >= start
            case .script(let refs): refs.contains { $0.index >= start }
            }
        }
        sessions = sessions.filter { $0.value.index >= start }
    }
}
