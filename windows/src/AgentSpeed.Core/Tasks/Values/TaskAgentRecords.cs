using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Tasks;

/// <summary>The task records of one agent read whole (METRICS 6.11 <i>Task records</i>) with their identity fields.</summary>
internal static class TaskAgentRecords
{
    /// <param name="dayOf">The local date of a record's end, empty when it has none (METRICS 8).</param>
    public static IReadOnlyList<TaskRecord> Of(TranscriptParser parser, long analysisMs, Func<long, string> dayOf)
    {
        if (parser.Tasks is not { } tasks)
        {
            return [];
        }

        var records = TaskRecords.Of(tasks.Events, analysisMs, parser.LastLineMs ?? 0, tasks.Notes);
        if (records.Count == 0)
        {
            return [];
        }

        var result = new TaskRecord[records.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var record = records[i];
            result[i] = record with { AgentId = parser.AgentId, Source = parser.Source, Day = dayOf(record.EndMs) };
        }

        return result;
    }
}
