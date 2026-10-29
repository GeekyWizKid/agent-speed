import Foundation
@testable import AgentSpeedCore

/// A line-by-line port of `Evaluation` in `spec/tools/task_score.py` (quadratic where the reference is), used only
/// to check the model's restructured single pass on random streams.
final class ReferenceEvaluation {
    final class Act {
        let index: Int
        let kind: TaskActionKind
        let key: String
        let command: TaskCommand?
        let outcome: TaskOutcome?
        let failures: TaskRunFailures?
        let hunks: [TaskHunk]?
        let weight: Int
        var result: String?
        let fingerprintList: [TaskDigest]
        let fingerprints: Set<TaskDigest>
        var errors = 0
        var check: String?
        var block = 0
        var effective = false
        var oscillating = false
        var gain = 0

        init(_ index: Int, _ t: Int64, _ action: TaskAction, _ now: Int64?) {
            self.index = index
            kind = action.kind
            key = action.tool + "|" + action.digest.hex
            command = kind == .command ? action.command : nil
            var outcome = action.outcome
            if let known = outcome, let now, let at = known.t, at > now { outcome = nil }
            self.outcome = outcome
            failures = outcome != nil ? action.failures : nil
            hunks = kind == .edit ? action.hunks : nil
            let verifying = command.map { ["test", "build", "check"].contains($0.commandClass.rawValue) } ?? false
            weight = kind == .other ? 0 : verifying ? 2 : 1
            fingerprintList = failures?.fingerprints ?? []
            fingerprints = Set(fingerprintList)
            if verifying { result = ReferenceEvaluation.runResult(outcome, failures) }
            let failedCount = failures?.failedCount
            errors = result != "fail" ? 0 : max(1, fingerprintList.count, failedCount.map { Int($0) } ?? 0)
            if result != nil, let command {
                check = command.targeted || command.suite == nil ? "key:" + command.key.hex : "suite:" + command.suite!
            }
        }

        var targeted: Bool { command?.targeted ?? false }
        var suite: String? { command?.suite }
        var runClass: String? { command?.commandClass.rawValue }
    }

    static func runResult(_ outcome: TaskOutcome?, _ failures: TaskRunFailures?) -> String? {
        guard let outcome else { return nil }
        let failedCount = failures?.failedCount
        let passedCount = failures?.passedCount
        if outcome.failed == true || (failedCount != nil && failedCount! > 0) { return "fail" }
        if outcome.failed == false || (failedCount != nil && failedCount! == 0 && !(passedCount != nil && passedCount! == 0)) {
            return "pass"
        }
        return nil
    }

    final class FileEdits {
        var written: [TaskDigest] = []
        var latest: TaskDigest?
        var current = Set<TaskDigest>()
        var superseded = Set<TaskDigest>()
    }

    var acts: [Act] = []
    var counted: [Act] = []
    var edits = 0
    var oscillations = 0
    var osc = 0.0
    var ig = 0.0
    var loopActions = 0
    var loop = 0.0
    var stallEnds: [Int] = []
    var runs: [Act] = []
    var seqOrder: [String] = []
    var seq: [String: [Act]] = [:]
    var episodes: [(check: String, fails: [Act], recovered: Bool)] = []
    var nextInSeq: [Int: Act?] = [:]
    var currentBlock: [Act] = []
    var latestBlock: [Act] = []
    var state = "none"
    var v = 0.0, c = 0.0, r = 0.0, e = 0.0, k = 0.0
    var score: Double?

    init(_ events: [TaskEvent], now: Int64?) {
        for event in events {
            guard case .action(let action) = event.body, now == nil || event.t <= now! else { continue }
            acts.append(Act(acts.count, event.t, action, now))
        }
        editsAndGain()
        runsAndState()
        dimensions()
    }

    func applyEdit(_ files: inout [TaskDigest: FileEdits], _ act: Act) {
        if act.outcome?.failed == true { return }
        let hunks = act.hunks ?? []
        if hunks.isEmpty {
            act.effective = true
            return
        }
        var changed = false
        var oscillating = false
        for h in hunks {
            if files[h.file] == nil { files[h.file] = FileEdits() }
            let state = files[h.file]!
            if h.old == nil {
                if let latest = state.latest, h.new == latest { continue }
                changed = true
                if state.written.contains(h.new) { oscillating = true }
                state.written.append(h.new)
                state.latest = h.new
                state.current.removeAll()
                state.superseded.removeAll()
            } else {
                let old = h.old!
                if old == h.new { continue }
                changed = true
                if state.current.contains(old) && state.superseded.contains(h.new) { oscillating = true }
                state.superseded.insert(old)
                state.current.remove(old)
                state.current.insert(h.new)
                state.latest = nil
            }
        }
        act.effective = changed
        act.oscillating = changed && oscillating
    }

    func editsAndGain() {
        var files: [TaskDigest: FileEdits] = [:]
        var lastSeen: [String: Int] = [:]
        var lastRun: [TaskDigest: (Int, String, [TaskDigest], Int64?)] = [:]
        for a in acts {
            a.block = edits
            if a.kind == .edit {
                applyEdit(&files, a)
                a.gain = a.effective && !a.oscillating ? 1 : 0
                if a.effective { edits += 1 }
            } else if let result = a.result {
                let signature = (edits, result, a.fingerprints.sorted(), a.failures?.failedCount)
                let previous = lastRun[a.command!.key]
                a.gain = previous == nil || !(previous! == signature) ? 1 : 0
                lastRun[a.command!.key] = signature
            } else if a.weight > 0 {
                a.gain = lastSeen[a.key] != edits ? 1 : 0
            }
            if a.kind != .edit && a.weight > 0 && a.result == nil { lastSeen[a.key] = edits }
            if a.weight > 0 { counted.append(a) }
        }
        let effective = acts.filter(\.effective)
        oscillations = effective.filter(\.oscillating).count
        osc = effective.isEmpty ? 0.0 : Double(oscillations) / Double(effective.count)
        let total = counted.reduce(0) { $0 + $1.weight }
        ig = total > 0 ? Double(counted.reduce(0) { $0 + $1.weight * $1.gain }) / Double(total) : 0.0
        let marks = TaskScoreModelTests.referenceMarks(keys(counted.map(\.key)))
        loopActions = marks.filter { $0 }.count
        loop = counted.isEmpty ? 0.0 : Double(loopActions) / Double(counted.count)
        if counted.count >= 8 {
            for end in 8...counted.count {
                let window = counted[(end - 8)..<end]
                if Double(window.reduce(0) { $0 + $1.weight * $1.gain }) <= 0.25 * Double(window.reduce(0) { $0 + $1.weight }) {
                    stallEnds.append(end)
                }
            }
        }
    }

    func keys(_ strings: [String]) -> [Int] {
        var ids: [String: Int] = [:]
        return strings.map { key in
            if let id = ids[key] { return id }
            ids[key] = ids.count
            return ids.count - 1
        }
    }

    func runsAndState() {
        runs = acts.filter { $0.result != nil }
        var own: [String: [Act]] = [:]
        for run in runs {
            if own[run.check!] == nil { seqOrder.append(run.check!) }
            own[run.check!, default: []].append(run)
        }
        for check in seqOrder {
            let mine = own[check]!
            let first = mine[0]
            var covered = mine
            if first.targeted && first.suite != nil {
                covered += runs.filter { $0.index > first.index && $0.result == "pass" && !$0.targeted && $0.suite == first.suite && $0.check != check }
            }
            seq[check] = covered.sorted { $0.index < $1.index }
        }
        for check in seqOrder {
            let list = seq[check]!
            var current: Int?
            for (i, run) in list.enumerated() {
                if run.check == check && run.result == "fail" { nextInSeq[run.index] = i + 1 < list.count ? list[i + 1] : nil }
                if run.result == "fail" {
                    if current == nil {
                        episodes.append((check, [], false))
                        current = episodes.count - 1
                    }
                    episodes[current!].fails.append(run)
                } else if current != nil {
                    episodes[episodes.count - 1].recovered = true
                    current = nil
                }
            }
        }
        var blocks: [Int: [Act]] = [:]
        for run in runs { blocks[run.block, default: []].append(run) }
        currentBlock = blocks[edits] ?? []
        latestBlock = blocks.keys.max().map { blocks[$0]! } ?? []
        if !currentBlock.isEmpty {
            state = blockFails(currentBlock) ? "failing" : "passing"
        } else if !runs.isEmpty {
            state = blockFails(latestBlock) ? "failing" : "pending"
        } else {
            state = edits > 0 ? "unverified" : "none"
        }
    }

    func blockFails(_ block: [Act]) -> Bool {
        let members = Set(block.map(\.index))
        for check in Set(block.map { $0.check! }) {
            let inside = seq[check]!.filter { members.contains($0.index) }
            if let last = inside.last, last.result == "fail" { return true }
        }
        return false
    }

    func dimensions() {
        let block = currentBlock
        let passingTests = block.filter { $0.runClass == "test" && $0.result == "pass" }
        v = state == "passing" ? (passingTests.isEmpty ? 20.0 : 40.0) : 0.0
        c = convergence()
        if !episodes.isEmpty {
            var sum = 0.0
            for episode in episodes { sum += episode.recovered ? 2.0 / Double(episode.fails.count + 1) : 0.0 }
            r = 15.0 * sum / Double(episodes.count)
        } else {
            r = runs.contains { $0.result == "pass" } ? 15.0 : 0.0
        }
        let n = counted.count
        e = n > 0 ? 10.0 * min(1.0, Double(n) / 8) * ig * (1 - loop) * (1 - osc) : 0.0
        k = 0.0
        if block.contains(where: { $0.runClass == "build" || $0.runClass == "check" }) || !passingTests.isEmpty { k += 4 }
        if block.contains(where: { $0.runClass == "test" }) { k += 4 }
        for run in passingTests where !run.targeted
            && !block.contains(where: { $0.index > run.index && $0.runClass == "test" && $0.result == "fail" }) {
            k += 2
            break
        }
        score = state == "none" ? nil : v + c + r + e + k
    }

    func convergence() -> Double {
        if runs.isEmpty { return 0.0 }
        var weighted = 0.0
        var total = 0.0
        for check in seqOrder {
            let list = seq[check]!
            let peak = list.map(\.errors).max()!
            if peak == 0 { continue }
            let firstPeak = list.firstIndex { $0.errors == peak }!
            let base = list[...firstPeak].reduce(into: Set<TaskDigest>()) { $0.formUnion($1.fingerprints) }
            let last = list.last!
            let new = last.result == "fail" ? last.fingerprints.subtracting(base).count : 0
            weighted += Double(peak) * max(0.0, 1 - Double(last.errors + new) / Double(peak))
            total += Double(peak)
        }
        return total == 0 ? 25.0 : 25.0 * weighted / total
    }

    var countsValue: TaskCounts {
        let recovered = episodes.filter(\.recovered).map(\.fails)
        let fixAttempts = runs.filter { $0.result == "fail" }
        let fixFailures = fixAttempts.filter { run in
            guard let next = nextInSeq[run.index] ?? nil else { return true }
            return next.result == "fail"
        }.count
        return TaskCounts(actions: acts.count, counted: counted.count, edits: edits, oscillations: oscillations,
                          loopActions: loopActions, runs: runs.count, failingRuns: fixAttempts.count,
                          episodes: episodes.count, recovered: recovered.count,
                          recoverAttempts: recovered.reduce(0) { $0 + $1.count }, fixAttempts: fixAttempts.count,
                          fixFailures: fixFailures)
    }

    var firstPassMs: Int64? {
        runs.filter { $0.result == "pass" }.compactMap { $0.outcome?.t }.min()
    }

    var openFailure: TaskOpenFailure? {
        guard state == "failing" else { return nil }
        let open = seqOrder.compactMap { check -> Act? in
            let last = seq[check]!.last!
            return last.result == "fail" && last.check == check ? last : nil
        }
        let run = open.max { $0.index < $1.index }!
        let failedCount = run.failures?.failedCount
        return TaskOpenFailure(suite: run.suite, commandClass: run.command!.commandClass, key: run.command!.key,
                               errors: run.errors, peak: seq[run.check!]!.map(\.errors).max()!,
                               counted: !run.fingerprints.isEmpty || (failedCount != nil && failedCount! > 0),
                               edited: run.block < edits)
    }

    var vHeld: Double? {
        guard state == "pending" else { return nil }
        return latestBlock.contains { $0.runClass == "test" && $0.result == "pass" } ? 40 : 20
    }

    var lastPass: TaskLastPass? {
        guard state == "passing" || state == "pending" else { return nil }
        let block = state == "passing" ? currentBlock : latestBlock
        let run = block.last!
        return TaskLastPass(suite: run.suite, commandClass: run.command!.commandClass, key: run.command!.key,
                            buildOnly: !block.contains { $0.runClass == "test" && $0.result == "pass" })
    }
}
