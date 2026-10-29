using AgentSpeed.Core;
using AgentSpeed.Core.Antigravity;
using AgentSpeed.Core.Droid;
using AgentSpeed.Core.History;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.OpenClaw;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Probe;

/// <summary>The logs of the sources with a task score (METRICS 6.9) in startup order, as <c>reference.py startup_files</c> lists them.</summary>
internal static class TaskLogs
{
    /// <summary>
    /// Every log of a scored source for <c>--dump-tasks</c> (<c>include_archived=True</c>): the history's transcript roots
    /// (Codex <c>archived_sessions</c> included, every omp root, Pi, Factory Droid) and every OpenClaw agent database, by
    /// modification time (a database with its <c>-wal</c>, a Droid session with its sidecar), then path.
    /// </summary>
    public static List<TaskLog> Startup(SourceLocations locations)
    {
        var timed = new List<(DateTime Time, TaskLog Log)>();
        var seen = new HashSet<(string Path, string Root)>();
        foreach (var (family, root) in HistoryAnalyzer.Roots(locations, new HashSet<SourceKind>(Enum.GetValues<SourceKind>())))
        {
            if (SourceOf(family) is not { } source || !Directory.Exists(root))
            {
                continue;
            }

            var parserRoot = family is RootFamily.Omp or RootFamily.Pi ? root : string.Empty;
            foreach (var file in TranscriptDiscovery.Enumerate(root, Families.Naming(family), CancellationToken.None, int.MaxValue))
            {
                if (seen.Add((file.Path, parserRoot)))
                {
                    timed.Add((file.LastWriteUtc, new TaskLog(file.Path, source, parserRoot)));
                }
            }
        }

        foreach (var (path, stamp) in Databases(locations))
        {
            if (seen.Add((path, string.Empty)))
            {
                timed.Add((stamp.LastWriteUtc, new TaskLog(path, SourceKind.OpenClaw)));
            }
        }

        timed.Sort(static (a, b) =>
        {
            var byTime = a.Time.CompareTo(b.Time);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Log.Path, b.Log.Path);
        });
        return [.. timed.Select(t => t.Log)];
    }

    /// <summary>
    /// The OpenClaw agent databases changed at <paramref name="sinceMs"/> or later (with their <c>-wal</c>) in startup order,
    /// with that time: <c>reference.py --dump-today</c> reads every window of each whole.
    /// </summary>
    public static List<(TaskLog Log, DateTime Time)> OpenClawSince(SourceLocations locations, long sinceMs) =>
    [
        .. Databases(locations)
            .Where(d => new DateTimeOffset(d.Stamp.LastWriteUtc, TimeSpan.Zero).ToUnixTimeMilliseconds() >= sinceMs)
            .OrderBy(d => d.Stamp.LastWriteUtc)
            .ThenBy(d => d.Path, StringComparer.Ordinal)
            .Select(d => (new TaskLog(d.Path, SourceKind.OpenClaw), d.Stamp.LastWriteUtc)),
    ];

    /// <summary>A transcript's time in the startup order: its modification time, a Droid session's with its sidecar's (4.16).</summary>
    public static DateTime TranscriptTime(string path, SourceKind source)
    {
        var written = File.GetLastWriteTimeUtc(path);
        return source == SourceKind.Droid ? DroidPaths.Modified(path, written) : written;
    }

    private static IEnumerable<(string Path, AntigravityStamp Stamp)> Databases(SourceLocations locations)
    {
        foreach (var path in OpenClawPaths.Databases(locations.OpenClawRoots ?? []))
        {
            if (AntigravityStamp.Of(path) is { } stamp)
            {
                yield return (path, stamp);
            }
        }
    }

    private static SourceKind? SourceOf(RootFamily family) => family switch
    {
        RootFamily.Codex => SourceKind.Codex,
        RootFamily.Claude => SourceKind.ClaudeCode,
        RootFamily.Omp => SourceKind.Omp,
        RootFamily.Pi => SourceKind.Pi,
        RootFamily.Droid => SourceKind.Droid,
        _ => null,
    };
}
