using System.Globalization;
using AgentSpeed.History.Tasks;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.History;

/// <summary>
/// The task performance section (DESIGN 4b, 3c; DESIGN 12): the complete task records of the window's range compared per
/// source and per model (METRICS 6.11 <i>Comparison</i>) — over the one source when the window's picker selects one.
/// </summary>
internal sealed partial class HistoryViewModel
{
    private TaskPerformanceViewModel? _taskPerformance;

    public TaskPerformanceViewModel TaskPerformance => _taskPerformance ??= new TaskPerformanceViewModel(_loc);

    /// <param name="pair">The remembered pair, or null for the default.</param>
    /// <param name="changed">Persists a new choice.</param>
    public void RememberTaskPair(TaskPair? pair, Action<TaskPair> changed)
    {
        TaskPerformance.RememberPair(pair, changed);
        UpdateTasks();
    }

    public void SetTaskAppearance(bool dark, bool highContrast, bool reduceMotion) => TaskPerformance.SetAppearance(dark, highContrast, reduceMotion);

    private void UpdateTasks()
    {
        var first = Range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = Range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var comparison = TaskScoreMapping.Comparison(_hub.CompareTasks(first, last, _source));
        TaskPerformance.Update(comparison, _source, IsAnalyzing);
    }

    private void RelocalizeTasks() => _taskPerformance?.Relocalize(_loc);

    private void DisposeTasks() => _taskPerformance?.Dispose();
}
