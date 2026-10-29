using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Presentation.Reporting;

/// <summary>
/// The caption under a section switch (DESIGN 11, item 5): <c>report.section.notAccepted</c> when the service does not list
/// the section, <c>report.section.trafficOff</c> while traffic measuring is off, and under the models switch
/// <c>report.section.models.taskScore</c> — what the switch also shares — while the known document advertises the task score
/// extension or no document is known yet (METRICS 11.10). It stays with the switch off, so the user knows what turning it on
/// shares.
/// </summary>
public static class ReportSectionCaption
{
    public const string NotAccepted = "report.section.notAccepted";
    public const string TrafficOff = "report.section.trafficOff";
    public const string TaskScore = "report.section.models.taskScore";

    /// <param name="document">The known discovery document; null before the first fetch.</param>
    /// <param name="trafficMeasured">Traffic measuring is on (DESIGN 10).</param>
    /// <returns>The caption's localization key, or null for none.</returns>
    public static string? KeyOf(ReportSection section, ReportDiscovery? document, bool trafficMeasured)
    {
        if (document is not null && !document.Sections.Contains(section))
        {
            return NotAccepted;
        }

        return section switch
        {
            ReportSection.Traffic when !trafficMeasured => TrafficOff,
            ReportSection.Models when document is null || document.Extensions.TaskScore => TaskScore,
            _ => null,
        };
    }
}
