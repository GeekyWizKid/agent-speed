using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

/// <summary>
/// The task score extension's side of live reporting (METRICS 11.10): the watch the engine follows and the task ledger's
/// rows that builds take. One task window per controller, so new runs, reconnections and transport changes keep the rows; a
/// break — the watch turning off, or another service — drops them.
/// </summary>
internal sealed partial class ReportController
{
    private readonly ReportTaskWindow _tasks = new();

    /// <summary>
    /// The extension is active for the selected service's reports: the known document advertises it and lists <c>models</c>,
    /// <c>maxModels</c> ≥ 1, and the user's models switch is on.
    /// </summary>
    private bool TaskScoreActive => Identity?.Discovery is { } document && IsTaskScoreActive(document, Preferences.Sections);

    private static bool IsTaskScoreActive(ReportDiscovery document, IEnumerable<ReportSection> switches) =>
        document.Extensions.TaskScore && document.Limits.MaxModels >= 1 && document.Sections.Contains(ReportSection.Models)
        && switches.Contains(ReportSection.Models);

    /// <summary>
    /// The watch (METRICS 11.10, Watch): reporting on for a confirmed and registered service, the network on, not paused, no
    /// error or stop that needs the user, the extension active and seeding finished. Backoffs, waits, quiet and new runs leave
    /// it on. Evaluated after each tick's actions, so a leaving report takes the rows before the break empties them.
    /// </summary>
    private void FollowTaskWatch()
    {
        var on = IsReady() && NetworkEnabled && !IsPaused && TaskScoreActive && !(UsesWebSocket && _stream?.Scheduler.Stop is not null);
        if (_tasks.Update(on, Identity?.Id))
        {
            _hub.TaskWatch = _tasks.Watch;
            ReportLog.Write(on ? $"task watch on (generation {_tasks.Watch.Generation})" : "task watch off");
        }
    }

    /// <summary>A build takes the task rows since the previous build; the engine learns how many it took (the 1000 cap).</summary>
    private IReadOnlyDictionary<ReportModelKey, ReportTaskRow> TakeTasks(ReportSample sample)
    {
        var taken = _tasks.Take(sample);
        _hub.TaskWatch = _tasks.Watch;
        return taken;
    }
}
