import Foundation

/// The facts of a task for the score card, records and predictions (METRICS 6.9 *Task facts*, 6.11).
extension TaskEvaluation {
    public var counts: TaskCounts {
        var counts = TaskCounts(actions: acts.count, counted: counted.count, edits: edits, oscillations: oscillations,
                                loopActions: loopActions, runs: runs.count, episodes: episodes.count)
        for episode in episodes where episode.recovered {
            counts.recovered += 1
            counts.recoverAttempts += episode.fails.count
        }
        for run in runs where acts[run].result == .fail {
            counts.failingRuns += 1
            counts.fixFailures += fixLabel(run)
        }
        counts.fixAttempts = counts.failingRuns
        return counts
    }

    /// The earliest outcome `t` of a passing run.
    public var firstPassMs: Int64? {
        runs.lazy.filter { self.acts[$0].result == .pass }.compactMap { self.acts[$0].outcomeT }.min()
    }

    /// `pending` only: the V the latest verified block would give without the edit after it.
    public var vHeld: Double? {
        guard state == .pending else { return nil }
        return latestBlock.contains { hasPassingTest(runs[$0]) } ? Self.vTest : Self.vBuild
    }

    /// `failing` only.
    public var openFailure: TaskOpenFailure? {
        guard state == .failing, let index = openFailureRun, let command = acts[index].command else { return nil }
        let run = acts[index]
        return TaskOpenFailure(suite: command.suite, commandClass: command.commandClass, key: command.key,
                               errors: run.errors, peak: checks[run.check].seq.map { acts[$0].errors }.max() ?? 0,
                               counted: !run.fingerprints.isEmpty || (run.failedCount ?? 0) > 0,
                               edited: run.block < edits)
    }

    /// `passing` and `pending` only.
    public var lastPass: TaskLastPass? {
        let block: Range<Int>
        switch state {
        case .passing: block = currentBlock
        case .pending: block = latestBlock
        default: return nil
        }
        guard let last = block.last, let command = acts[runs[last]].command else { return nil }
        return TaskLastPass(suite: command.suite, commandClass: command.commandClass, key: command.key,
                            buildOnly: !block.contains { hasPassingTest(runs[$0]) })
    }

    /// The act index of the newest failing run that is still the last element of its check's sequence.
    var openFailureRun: Int? {
        var newest: Int?
        for (index, check) in checks.enumerated() {
            guard let last = check.seq.last, acts[last].result == .fail, acts[last].check == index else { continue }
            newest = max(newest ?? last, last)
        }
        return newest
    }

    /// `fix_label`: 1 when the next element of the failing run's sequence fails or there is none, 0 when it passes.
    func fixLabel(_ run: Int) -> Int {
        guard let next = acts[run].next else { return 1 }
        return acts[next].result == .fail ? 1 : 0
    }

    private func hasPassingTest(_ run: Int) -> Bool {
        acts[run].runClass == .test && acts[run].result == .pass
    }

    // MARK: Fix features

    /// `fix_features` of one failing run (act index): the features as they stand right after that run.
    func fixFeatures(run: Int) -> TaskFixFeatures {
        fixFeatures(runs: [run])[0]
    }

    /// `fix_features` of every failing run, in order.
    func allFixFeatures() -> [(run: Int, features: TaskFixFeatures)] {
        let failing = runs.filter { acts[$0].result == .fail }
        return Array(zip(failing, fixFeatures(runs: failing)))
    }

    /// The reference evaluates the events up to each run again. Everything it reads is prefix-closed, so one pass
    /// gives the same numbers: the prefix's IG and O are running sums, its sequence of the run's check is the full
    /// sequence cut after the run (whose last element is the run), and the run's position in its episode is the
    /// number of fails since that sequence's last pass.
    private func fixFeatures(runs targets: [Int]) -> [TaskFixFeatures] {
        guard !targets.isEmpty else { return [] }
        var peaks = [Int](repeating: 0, count: acts.count)
        var attempts = [Int](repeating: 0, count: acts.count)
        for (index, check) in checks.enumerated() {
            var peak = 0
            var streak = 0
            for run in check.seq {
                peak = max(peak, acts[run].errors)
                if acts[run].result == .fail {
                    streak += 1
                    if acts[run].check == index {
                        peaks[run] = peak
                        attempts[run] = streak
                    }
                } else {
                    streak = 0
                }
            }
        }
        var out: [TaskFixFeatures] = []
        out.reserveCapacity(targets.count)
        var target = 0
        var effective = 0
        var oscillating = 0
        var total = 0
        var weighted = 0
        for index in acts.indices {
            let act = acts[index]
            if act.effective { effective += 1 }
            if act.oscillating { oscillating += 1 }
            total += act.weight
            weighted += act.weight * act.gain
            while target < targets.count, targets[target] == index {
                let tries = taskDivide(attempts[index] - 1, 4)
                let errors = log2(Double(1 + act.errors)) / 8
                out.append(TaskFixFeatures(attempts: tries < 1.0 ? tries : 1.0, errors: errors < 1.0 ? errors : 1.0,
                                           trend: taskDivide(act.errors, peaks[index]),
                                           osc: effective > 0 ? taskDivide(oscillating, effective) : 0.0,
                                           ig: total > 0 ? taskDivide(weighted, total) : 0.0))
                target += 1
            }
        }
        return out
    }
}
