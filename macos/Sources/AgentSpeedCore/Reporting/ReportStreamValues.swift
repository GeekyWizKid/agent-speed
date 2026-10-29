import Foundation

/// The values of the changed rule (METRICS 11.9), rounded as a report sends them (METRICS 11.7): a tick whose values
/// differ from the last built report's is changed.
public struct ReportStreamValues: Equatable, Sendable {
    public let outputTokS: Double
    public let totalTokS: Double
    public let estimatedShare: Double
    public let generating: Int
    public let tools: Int
    public let active: Int
    /// The `taskScore` objects the report would carry while the extension is active (METRICS 11.10 *WebSocket*).
    public let taskScore: ReportTaskScoreValues?

    public init(_ sample: ReportSample, taskScore: ReportTaskScoreValues? = nil) {
        self.taskScore = taskScore
        outputTokS = Self.tokS(sample.live.output)
        totalTokS = Self.tokS(sample.live.total)
        estimatedShare = ReportBody.rounded(min(max(sample.estimatedShare.isFinite ? sample.estimatedShare : 0, 0), 1), 3)
        let agents = sample.facts.agents.values
        generating = min(agents.reduce(0) { $0 + $1.generating }, ReportBody.maxCount)
        tools = min(agents.reduce(0) { $0 + $1.tools }, ReportBody.maxCount)
        active = min(agents.reduce(0) { $0 + $1.active }, ReportBody.maxCount)
    }

    private static func tokS(_ value: Double) -> Double {
        ReportBody.rounded(min(max(value.isFinite ? value : 0, 0), ReportBody.maxTokS), 1)
    }
}
