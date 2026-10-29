import AgentSpeedCore
import Foundation

/// The `taskScore` extension on the reporting side (METRICS 11.10): the watch, worked out at every tick and handed to
/// the live monitor as an epoch; the completions each snapshot brings, taken once; and the task ledger, which every
/// build with the extension active takes and a break empties after the leaving report of its tick.
extension ReportController {
    /// The service the watch is on for, nil while it is off: reporting on for a confirmed and registered service, the
    /// network on, not paused, nothing that needs the user (an error, a review, a stop), the extension active and the
    /// monitor's seeding done. Transient waits (backoff, `Retry-After`, quiet, a sleep gap, a new run or transport, a
    /// stale document) leave it on.
    var taskWatchKey: String? {
        guard monitorRunning, preferences.enabled, settings.networkEnabled, !preferences.isPaused, problem == nil,
              signer != nil, let identity, identity.isRegistered, identity.pin != nil, !needsReview,
              let document = identity.discovery, document.supportsThisClient,
              ReportTaskScoreRule.isActive(document, switches: preferences.sections),
              stream?.scheduler.stop == nil else { return nil }
        return identity.id
    }

    /// At the start of a tick: the snapshot's new completions of the current watch join the ledger (true when one
    /// did, the changed rule); a watch that moved to another service starts before anything is built for it.
    func beginTaskTick(_ sample: ReportSample) -> Bool {
        if let key = taskWatchKey, key != taskTracker.watchKey { updateTaskWatch() }
        return taskTracker.ingest(sample.facts.task.completions)
    }

    /// The watch as it is now, after the tick's leaving report took the ledger: turning off (or to another service)
    /// is a break that empties it; a new epoch reaches the monitor.
    func updateTaskWatch() {
        guard taskTracker.watch(taskWatchKey) else { return }
        taskWatchHandler?(taskTracker.epoch)
    }

    /// A build takes the task rows into the body while the extension is active (also when the size rule drops
    /// `models`: those completions are lost, never resent).
    func takeTaskStats(_ options: ReportOptions) -> ReportTaskStats {
        options.taskScoreActive ? taskTracker.take() : [:]
    }

    /// The `taskScore` objects the next report of the stream would carry, for the changed rule; nil while inactive.
    func streamTaskScoreValues(_ window: ReportWindow, _ sample: ReportSample) -> ReportTaskScoreValues? {
        guard let document = identity?.discovery else { return nil }
        let options = ReportOptions(sections: preferences.sections.intersection(document.sections),
                                    maxModels: document.limits.maxModels, extensions: document.extensions ?? .none)
        guard options.taskScoreActive else { return nil }
        var content = window.content(sample)
        content.taskStats = taskTracker.pending
        return ReportBody.taskScoreValues(content, sample, options)
    }
}
