using System.Diagnostics;
using AgentSpeed.Core;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Probe;

/// <summary>
/// <c>--dump-tasks</c> (METRICS 6.9 <i>Dumps</i>, 6.11): every log of a scored source read whole in startup order with one
/// dedupe set, then a <c>record</c> row per task record (by agent id, then ordinal), a <c>calibration</c> row per source,
/// the <c>comparison</c> and a <c>summary</c>, as <c>reference.py --dump-tasks</c>.
/// </summary>
internal static class DumpTasks
{
    /// <summary>Like the reference's <c>Deduper(limit=10**9)</c>: no practical limit.</summary>
    private const int DedupeCapacity = 1_000_000_000;

    public static int Run(ProbeOptions options, ProbeOutput output)
    {
        var stopwatch = Stopwatch.StartNew();
        var analysisMs = options.NowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var labeler = new DayLabeler(options.Utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local);
        var logs = TaskLogs.Startup(options.Locations);
        var agents = TaskAgentsReader.Read(logs, new Deduper(DedupeCapacity));
        var records = agents.SelectMany(a => TaskAgentRecords.Of(a, analysisMs, labeler.DayOf))
            .OrderBy(r => r.AgentId, StringComparer.Ordinal)
            .ThenBy(r => r.Ordinal)
            .ToList();

        foreach (var record in records)
        {
            output.Line(JsonLine.Of("record", w => TaskDumpJson.WriteRecord(w, record, Identity)));
        }

        foreach (var source in TaskSetView.ScoredSources)
        {
            var part = records.Where(r => r.Source == source).ToList();
            if (part.Count == 0)
            {
                continue;
            }

            var models = TaskCalibration.Calibrate(part);
            output.Line(JsonLine.Of("calibration", w =>
            {
                w.WriteStartObject();
                w.WriteString("source", SourceNames.Of(source));
                w.WritePropertyName("models");
                TaskDumpJson.WriteModels(w, models);
                w.WriteEndObject();
            }));
        }

        var days = records.Select(r => r.Day).Where(d => d.Length > 0).Order(StringComparer.Ordinal).ToList();
        var first = options.First ?? (days.Count > 0 ? days[0] : "0001-01-01");
        var last = options.Last ?? (days.Count > 0 ? days[^1] : "9999-12-31");
        var comparison = TaskComparison.Compare(records, first, last, options.Source);
        output.Line(JsonLine.Of("comparison", w =>
        {
            w.WriteStartObject();
            w.WriteString("first", first);
            w.WriteString("last", last);
            w.WriteString("source", options.Source is { } source ? SourceNames.Of(source) : null);
            TaskDumpJson.WriteComparison(w, comparison);
            w.WriteEndObject();
        }));

        var summary = new JsonLine()
            .Add("agents", agents.Count)
            .Add("records", records.Count)
            .Add("complete", records.Count(r => r.Complete))
            .Add("scored", records.Count(r => r.Score is not null))
            .Add("stitched", agents.Sum(a => a.Tasks!.Stats.Stitched))
            .Add("analysisMs", analysisMs)
            .Add("seconds", Math.Round(stopwatch.Elapsed.TotalSeconds, 1));
        output.Line(new JsonLine().Raw("summary", summary.Text).Text);
        output.Flush();
        Console.Error.WriteLine($"tasks: read {logs.Count} logs ({agents.Count} agents) in {stopwatch.ElapsedMilliseconds} ms; {records.Count} records");
        return 0;
    }

    private static string Identity(string id) => id;
}
