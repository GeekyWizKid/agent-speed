using AgentSpeed.Core;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Probe;

/// <summary>
/// The daily history of every log, analysed into a store (a temporary one, or <c>--history-store PATH</c>) and read back
/// from it, so stored facts go through METRICS 9.10 <i>Reading</i> as in the app.
/// </summary>
internal static class HistoryRun
{
    public static (HistoryReport Report, List<DailyUsage> Days, long StoreBytes) Analyze(ProbeOptions options, PriceTable table, TimeZoneInfo zone)
    {
        var temporary = options.StorePath is null
            ? Path.Combine(Path.GetTempPath(), "agentspeed-probe-" + Guid.NewGuid().ToString("N"))
            : null;
        var storePath = Path.GetFullPath(options.StorePath ?? Path.Combine(temporary!, "history.sqlite"));
        try
        {
            var tuning = Tuning();
            HistoryReport report;
            using (var store = HistoryStore.Open(storePath))
            {
                report = new HistoryAnalyzer(store, tuning, new PriceBook([table]))
                    .Run(options.Locations, new HashSet<SourceKind>(Enum.GetValues<SourceKind>()), zone, CancellationToken.None);
            }

            List<DailyUsage> days;
            using (var store = HistoryStore.Open(storePath))
            {
                days = new HistoryAnalyzer(store, tuning, new PriceBook([table])).Days();
            }

            var storeBytes = new[] { "", "-wal", "-shm" }
                .Select(suffix => new FileInfo(storePath + suffix))
                .Where(f => f.Exists)
                .Sum(f => f.Length);
            return (report, days, storeBytes);
        }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static HistoryOptions Tuning()
    {
        // Hermes's wall times are read in this computer's zone even when days are grouped in UTC (METRICS 4.15).
        var tuning = new HistoryOptions { HermesZone = TimeZoneInfo.Local };
        if (Environment.GetEnvironmentVariable("AGENTSPEED_HISTORY_WORKERS") is { } workers && int.TryParse(workers, out var w))
        {
            tuning = tuning with { Workers = w };
        }

        if (Environment.GetEnvironmentVariable("AGENTSPEED_HISTORY_BUFFER_MB") is { } buffer && long.TryParse(buffer, out var mb))
        {
            tuning = tuning with { MaxBufferedBytes = mb * 1024 * 1024 };
        }

        return tuning;
    }
}
