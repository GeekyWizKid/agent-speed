namespace AgentSpeed.Core.Reporting;

/// <summary>
/// The watch of METRICS 11.10 as live reporting publishes it to the engine. Every time the watch turns on, a new
/// <see cref="Generation"/> starts: a task is watched only when its first event is read while the watch is on, it counts only
/// while that generation is still on, and the rows of another generation are never sent, so a break unwatches every task
/// and empties the task ledger at once whatever the threads see.
/// </summary>
/// <param name="Taken">Completed tasks of this generation that built reports took, in engine <paramref name="TakenEpoch"/>: the
/// engine drops a completion while 1000 wait for a report.</param>
public sealed record ReportTaskWatch(long Generation, bool On, long Taken = 0, long TakenEpoch = 0)
{
    /// <summary>A task whose first event is older than this when it is read is history (the 120 000 ms of METRICS 11.4).</summary>
    public const long FirstEventAgeMs = 120_000;

    /// <summary>Completed tasks in one report, over every row.</summary>
    public const int MaxCompleted = 1000;

    public static ReportTaskWatch Off { get; } = new(0, false);

    /// <summary>
    /// The generation a task whose first event has time <paramref name="eventMs"/> is watched in when that event is read live
    /// at <paramref name="nowMs"/>; 0 when it is history.
    /// </summary>
    public long Mark(long eventMs, long nowMs) => On && nowMs - eventMs <= FirstEventAgeMs ? Generation : 0;

    /// <summary>A task marked in <paramref name="generation"/> counts now.</summary>
    public bool Counts(long generation) => On && generation != 0 && generation == Generation;
}

/// <summary>
/// The report side of the task ledger (METRICS 11.10, Task ledger): what a build takes from the engine's cumulative task rows.
/// A build takes the rows since the previous build; discards and new runs leave them alone; a break (the watch turning off)
/// empties them, since the next generation starts over. One per controller, so it outlives runs and transports.
/// </summary>
public sealed class ReportTaskWindow
{
    private static readonly IReadOnlyDictionary<ReportModelKey, ReportTaskRow> None = new Dictionary<ReportModelKey, ReportTaskRow>();

    private long _baselineEpoch = -1;
    private long _baselineGeneration = -1;
    private IReadOnlyDictionary<ReportModelKey, ReportTaskRow> _baseline = None;
    private string? _scope;

    /// <summary>What the engine reads (through the hub) to mark and count tasks.</summary>
    public ReportTaskWatch Watch { get; private set; } = ReportTaskWatch.Off;

    /// <summary>
    /// Follows the watch: on for <paramref name="scope"/> (the service) or off. Turning on, or switching services while on, starts
    /// a new generation; turning off is a break. True when <see cref="Watch"/> changed.
    /// </summary>
    public bool Update(bool on, string? scope)
    {
        if (on == Watch.On && (!on || string.Equals(scope, _scope, StringComparison.Ordinal)))
        {
            return false;
        }

        _scope = on ? scope : null;
        Watch = on ? new ReportTaskWatch(Watch.Generation + 1, true) : Watch with { On = false };
        return true;
    }

    /// <summary>The rows a build at <paramref name="sample"/> would take, without taking them (the preview).</summary>
    public IReadOnlyDictionary<ReportModelKey, ReportTaskRow> Content(ReportSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var counters = sample.Facts.Counters;
        if (!Watch.On || counters.TaskGeneration != Watch.Generation || counters.TaskRows.Count == 0)
        {
            return None;
        }

        var since = _baselineEpoch == sample.Facts.Epoch && _baselineGeneration == counters.TaskGeneration ? _baseline : None;
        var rows = new SortedDictionary<ReportModelKey, ReportTaskRow>(KeyOrder.Instance);
        var completed = 0L;
        foreach (var (key, row) in counters.TaskRows)
        {
            var change = since.TryGetValue(key, out var before) ? row - before : row;
            if (change.Completed > 0)
            {
                rows[key] = change;
            }
        }

        var kept = new Dictionary<ReportModelKey, ReportTaskRow>();
        foreach (var (key, row) in rows)
        {
            // The engine keeps at most MaxCompleted waiting; whole rows past it are dropped rather than sent invalid.
            if (completed + row.Completed > ReportTaskWatch.MaxCompleted)
            {
                continue;
            }

            completed += row.Completed;
            kept[key] = row;
        }

        return kept;
    }

    /// <summary>A build: the rows since the previous build, which it empties (also when the size rule then drops <c>models</c>).</summary>
    public IReadOnlyDictionary<ReportModelKey, ReportTaskRow> Take(ReportSample sample)
    {
        var taken = Content(sample);
        var counters = sample.Facts.Counters;
        _baselineEpoch = sample.Facts.Epoch;
        _baselineGeneration = counters.TaskGeneration;
        _baseline = counters.TaskRows;
        if (Watch.On && counters.TaskGeneration == Watch.Generation)
        {
            Watch = Watch with { Taken = counters.TaskCompleted, TakenEpoch = sample.Facts.Epoch };
        }

        return taken;
    }

    private sealed class KeyOrder : IComparer<ReportModelKey>
    {
        public static KeyOrder Instance { get; } = new();

        public int Compare(ReportModelKey x, ReportModelKey y)
        {
            var id = string.CompareOrdinal(x.ModelId, y.ModelId);
            return id != 0 ? id : x.Source.CompareTo(y.Source);
        }
    }
}
