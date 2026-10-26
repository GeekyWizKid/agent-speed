namespace AgentSpeed.Core.Reporting;

/// <summary>
/// The values of a tick that make it <i>changed</i> for the stream scheduler when they differ from the last built report's
/// (METRICS 11.9): <c>live.outputTokS</c>, <c>live.totalTokS</c>, <c>live.estimatedShare</c> and the agent counts, rounded as
/// the body writes them (METRICS 11.7).
/// </summary>
public readonly record struct ReportStreamValues(double OutputTokS, double TotalTokS, double EstimatedShare, int Generating, int Tools, int Active)
{
    public static ReportStreamValues Of(ReportSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var agents = default(ReportAgentCounts);
        foreach (var counts in sample.Facts.Agents.Values)
        {
            agents += counts;
        }

        return new ReportStreamValues(TokS(sample.Live.Output), TokS(sample.Live.Total),
            ReportJson.Round(Math.Clamp(double.IsFinite(sample.EstimatedShare) ? sample.EstimatedShare : 0, 0, 1), 3),
            Math.Min(agents.Generating, ReportBody.MaxCount), Math.Min(agents.Tools, ReportBody.MaxCount), Math.Min(agents.Active, ReportBody.MaxCount));
    }

    private static double TokS(double value) => ReportJson.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, ReportBody.MaxTokS), 1);
}
