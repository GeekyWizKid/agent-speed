using AgentSpeed.Core.IO;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Trae;

internal sealed record TraeAgent(string Id, string SessionId, string? Project, string? Model, long LastMs,
    AgentPhase Phase = AgentPhase.Idle, long? InFlightStartMs = null)
{
    public IReadOnlyList<ParsedResponse> ReferenceResponses { get; init; } = [];
}
internal sealed class TraeUpdate
{
    public List<(ParsedResponse Response, bool Completion)> Responses { get; } = [];
    public List<TraeAgent> Agents { get; } = [];
    public List<string> Removed { get; } = [];
    public bool Changed => Responses.Count > 0 || Agents.Count > 0 || Removed.Count > 0;
}

internal sealed class TraeSource(string root, IDeduper deduper)
{
    private readonly Dictionary<string, ParsedResponse> _emitted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TraeAgent> _agents = new(StringComparer.Ordinal);
    private TraeTiming.StoreStamp? _stamp;
    private readonly TraeTiming _timing = new();
    private FileIdentity? _identity;
    private string? _lastPath;
    private long _sinceMs;
    private long? _lastReadMs;
    internal Func<long> ClockMs { get; set; } = static () => Environment.TickCount64;
    internal Func<long> WallClockMs { get; set; } = static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string DatabasePath => TraePaths.DatabasePath(root);
    public string WatchDirectory => Path.GetDirectoryName(DatabasePath) ?? string.Empty;
    public bool Available => File.Exists(DatabasePath);
    public bool Unsupported { get; private set; }
    public bool Unreadable { get; private set; }
    public bool PartialHistory { get; private set; }
    public int ActiveSessionCount(long sinceMs) => _agents.Values.Count(agent => agent.LastMs >= sinceMs);
    public bool IsWatchedPath(string path) => string.Equals(path, DatabasePath, PathComparison) || string.Equals(path, DatabasePath + "-wal", PathComparison);

    public TraeUpdate Seed(long todayStartMs)
    {
        _sinceMs = Math.Max(0, todayStartMs - Monitoring.MonitorCore.BacklogWindowMs);
        var result = Refresh(true);
        _lastReadMs = null;
        return result;
    }

    public TraeUpdate Refresh(bool force = false)
    {
        var update = new TraeUpdate();
        var path = DatabasePath;
        var stamp = TraeTiming.Stamp(path);
        var identity = ReadIdentity(path);
        if (stamp is null || !string.Equals(path, _lastPath, PathComparison)
            || (_identity is { } old && identity is { } current && old != current))
        {
            update.Removed.AddRange(_agents.Keys);
            _agents.Clear();
            _stamp = null;
            _lastReadMs = null;
        }
        _lastPath = path;
        _identity = identity;
        if (stamp is null) { Unsupported = Unreadable = false; return update; }
        if (!force && stamp == _stamp)
        {
            var wallNow = WallClockMs();
            foreach (var agent in _agents.Values.Where(agent => agent.Phase != AgentPhase.Idle
                && (agent.LastMs > wallNow || wallNow - agent.LastMs > Engine.SpeedEngine.StaleMs)).ToArray())
            {
                var idle = agent with { Phase = AgentPhase.Idle, InFlightStartMs = null, ReferenceResponses = [] };
                _agents[agent.Id] = idle;
                update.Agents.Add(idle);
            }
            return update;
        }
        var now = ClockMs();
        if (!force && _lastReadMs is { } read && now - read < 1000) return update;
        _lastReadMs = now;
        try
        {
            using var database = TraeDatabase.Open(path);
            var nextAgents = new Dictionary<string, TraeAgent>(StringComparer.Ordinal);
            foreach (var group in database.Read(_sinceMs, timing: _timing).GroupBy(row => row.SessionId))
            {
                var owned = group.Where(row => !deduper.TryGetOwner(row.Response(path).Key, out var owner) || owner == row.AgentId(path)).ToArray();
                if (owned.Length == 0) continue;
                var latest = owned.MaxBy(row => row.EndMs)!;
                var agent = new TraeAgent(latest.AgentId(path), latest.SessionId, latest.Project, latest.Model, latest.EndMs);
                nextAgents[agent.Id] = agent;
                foreach (var row in owned)
                {
                    var response = row.Response(path);
                    if (_emitted.TryGetValue(response.Key, out var previous) && previous == response) continue;
                    var completion = deduper.TryClaim(response.Key, response.AgentId, out var owner);
                    if (!completion && owner != response.AgentId) continue;
                    _emitted[response.Key] = response;
                    update.Responses.Add((response, completion));
                }
            }
            foreach (var activity in database.ReadActivities(_timing, WallClockMs()))
            {
                var id = path + "#" + activity.Session;
                if (activity.Phase == AgentPhase.Idle && !nextAgents.ContainsKey(id) && !_agents.ContainsKey(id)) continue;
                if (!deduper.TryClaim("trae:activity:" + activity.Session + ":" + activity.Turn, id, out var owner) && owner != id) continue;
                var previous = nextAgents.GetValueOrDefault(id) ?? _agents.GetValueOrDefault(id);
                var agent = new TraeAgent(id, activity.Session, activity.Project ?? previous?.Project, activity.Model ?? previous?.Model,
                    Math.Max(activity.LastMs, previous?.LastMs ?? 0), activity.Phase, activity.InFlightStartMs)
                { ReferenceResponses = activity.ReferenceResponses };
                nextAgents[id] = agent;
            }
            foreach (var (id, agent) in nextAgents)
            {
                if (!_agents.TryGetValue(id, out var previous) || previous with { ReferenceResponses = agent.ReferenceResponses } != agent
                    || !previous.ReferenceResponses.SequenceEqual(agent.ReferenceResponses)) update.Agents.Add(agent);
                _agents[id] = agent;
            }
            foreach (var id in _agents.Keys.Where(id => !nextAgents.ContainsKey(id)).ToArray())
            {
                _agents.Remove(id);
                update.Removed.Add(id);
            }
            PartialHistory = true;
            Unsupported = Unreadable = false;
            _stamp = stamp;
        }
        catch (InvalidDataException) { Unsupported = true; Unreadable = false; _stamp = stamp; }
        catch (EntryPointNotFoundException) { Unsupported = true; _stamp = stamp; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        { Unreadable = true; }
        return update;
    }

    private static FileIdentity? ReadIdentity(string path)
    {
        try { using var file = TranscriptTailer.Open(path); return FileIdentity.From(file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
