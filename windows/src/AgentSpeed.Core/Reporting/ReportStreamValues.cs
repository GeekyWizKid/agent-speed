namespace AgentSpeed.Core.Reporting;

/// <summary>
/// The values of a tick that make it <i>changed</i> for the stream scheduler when they differ from the last built report's
/// (METRICS 11.9): <c>live.outputTokS</c>, <c>live.totalTokS</c>, <c>live.estimatedShare</c> and the agent counts, rounded as
/// the body writes them (METRICS 11.7); with the task score extension also the <c>taskScore</c> values (METRICS 11.10).
/// </summary>
public readonly record struct ReportStreamValues(double OutputTokS, double TotalTokS, double EstimatedShare, int Generating, int Tools, int Active)
{
    /// <summary>The <c>taskScore</c> values; null while the extension is inactive.</summary>
    public ReportStreamTasks? Tasks { get; init; }

    public static ReportStreamValues Of(ReportSample sample, bool taskScore = false)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var agents = default(ReportAgentCounts);
        foreach (var counts in sample.Facts.Agents.Values)
        {
            agents += counts;
        }

        return new ReportStreamValues(TokS(sample.Live.Output), TokS(sample.Live.Total),
            ReportJson.Round(Math.Clamp(double.IsFinite(sample.EstimatedShare) ? sample.EstimatedShare : 0, 0, 1), 3),
            Math.Min(agents.Generating, ReportBody.MaxCount), Math.Min(agents.Tools, ReportBody.MaxCount), Math.Min(agents.Active, ReportBody.MaxCount))
        {
            Tasks = taskScore ? ReportStreamTasks.Of(sample.Facts.Tasks) : null,
        };
    }

    private static double TokS(double value) => ReportJson.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, ReportBody.MaxTokS), 1);
}

/// <summary>
/// The <c>taskScore</c> values of a tick rounded as sent (METRICS 11.10, WebSocket): the top-level value and the value of every
/// model id an agent with a score or a thinking speed has. That covers every entry a report can carry (an <c>other</c> entry
/// merges ids whose values are compared one by one), so a test result that moves a score counts as a change.
/// </summary>
public sealed class ReportStreamTasks : IEquatable<ReportStreamTasks>
{
    private readonly (int? Score, string Form, double Thinking) _top;
    private readonly (string Id, (int? Score, string Form, double Thinking) Value)[] _models;

    private ReportStreamTasks((int?, string, double) top, (string, (int?, string, double))[] models)
    {
        _top = top;
        _models = models;
    }

    public static ReportStreamTasks Of(ReportTaskFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var ids = new SortedSet<string>(facts.ModelThinking.Keys, StringComparer.Ordinal);
        foreach (var agent in facts.Agents)
        {
            ids.Add(agent.ModelId);
        }

        var models = ids.Select(id => (id, ReportTaskValue.Of(facts.Agents.Where(a => a.ModelId == id), facts.ModelThinking.GetValueOrDefault(id)).Sent))
            .ToArray();
        return new ReportStreamTasks(facts.Top.Sent, models);
    }

    public bool Equals(ReportStreamTasks? other) => other is not null && _top == other._top && _models.SequenceEqual(other._models);

    public override bool Equals(object? obj) => Equals(obj as ReportStreamTasks);

    public override int GetHashCode() => HashCode.Combine(_top, _models.Length);
}
