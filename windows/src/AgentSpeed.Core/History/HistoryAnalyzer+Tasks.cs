using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.History;

/// <summary>
/// Task records (METRICS 6.11): every analysed file of a scored source stores its agent's records with its contributions;
/// a file whose last task was still open is analysed again once it has been quiet for 30 minutes, and every completed run
/// calibrates each scored source's models on its stored records, in memory.
/// </summary>
internal sealed partial class HistoryAnalyzer
{
    /// <summary>A file quiet this long since its modification has its incomplete task analysed again (6.11 <i>Store</i>).</summary>
    private const long QuietMs = 30 * 60_000;

    /// <summary>The sources with tasks (METRICS 6.9), in the source order of section 1.</summary>
    private static readonly SourceKind[] ScoredSources =
    [
        SourceKind.Codex, SourceKind.ClaudeCode, SourceKind.ClaudeDesktop, SourceKind.Omp, SourceKind.Pi, SourceKind.OpenClaw,
        SourceKind.Droid,
    ];

    /// <summary>The current run's <c>analysisMs</c> (METRICS 6.11), read from <see cref="HistoryOptions.NowMs"/> when it starts.</summary>
    private long _analysisMs;

    /// <summary>
    /// Per scored source with stored records, its models calibrated on them (METRICS 6.11 <i>Calibration</i>), as of the
    /// last completed run (or the load); never stored.
    /// </summary>
    public IReadOnlyDictionary<SourceKind, TaskModels> Calibration { get; private set; }

    /// <summary>Every valid stored task record of every file, by agent id (ordinal), then ordinal.</summary>
    public IReadOnlyList<TaskRecord> TaskRecords()
    {
        var records = new List<TaskRecord>();
        foreach (var entry in _entries.Values)
        {
            records.AddRange(entry.TaskRecords);
        }

        records.Sort(static (a, b) =>
        {
            var order = string.CompareOrdinal(a.AgentId, b.AgentId);
            return order != 0 ? order : a.Ordinal.CompareTo(b.Ordinal);
        });
        return records.AsReadOnly();
    }

    /// <summary>The task records of one agent read whole (METRICS 6.11 <i>Task records</i>), days from <paramref name="labeler"/>.</summary>
    private IReadOnlyList<TaskRecord> RecordsOf(TranscriptParser parser, DayLabeler labeler)
    {
        // Task records: filled in by the composer (METRICS 6.9).
        return [];
    }

    /// <summary>The file was last modified at least 30 minutes before this run (METRICS 6.11 <i>Store</i>).</summary>
    private bool IsQuiet(DateTime lastWriteUtc) =>
        _analysisMs - ((lastWriteUtc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond) >= QuietMs;

    private IReadOnlyDictionary<SourceKind, TaskModels> Calibrate()
    {
        var bySource = new Dictionary<SourceKind, List<TaskRecord>>();
        foreach (var entry in _entries.Values)
        {
            foreach (var record in entry.TaskRecords)
            {
                if (!bySource.TryGetValue(record.Source, out var list))
                {
                    bySource[record.Source] = list = [];
                }

                list.Add(record);
            }
        }

        var models = new Dictionary<SourceKind, TaskModels>();
        foreach (var source in ScoredSources)
        {
            if (bySource.TryGetValue(source, out var records))
            {
                models[source] = TaskCalibration.Calibrate(records);
            }
        }

        return models.AsReadOnly();
    }
}
