using System.Globalization;
using System.Text.Json.Nodes;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.OpenClaw;

namespace AgentSpeed.Core.Tests;

/// <summary>Writes an OpenClaw agent database the way the Gateway does (WAL, schema 37 columns that Agent Speed reads).</summary>
internal sealed class OpenClawWriter : IDisposable
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS session_nodes (session_key TEXT NOT NULL PRIMARY KEY, current_session_id TEXT NOT NULL,
          entry_json TEXT NOT NULL, updated_at INTEGER NOT NULL, parent_session_key TEXT, spawned_by TEXT);
        CREATE TABLE IF NOT EXISTS session_windows (session_id TEXT NOT NULL PRIMARY KEY, session_key TEXT NOT NULL,
          created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, transcript_updated_at INTEGER DEFAULT NULL,
          acp_owned INTEGER NOT NULL DEFAULT 0, agent_harness_id TEXT, spawned_by TEXT);
        CREATE TABLE IF NOT EXISTS transcript_events (session_id TEXT NOT NULL, seq INTEGER NOT NULL, event_json TEXT,
          created_at INTEGER NOT NULL, event_zstd BLOB, event_utf8_bytes INTEGER, PRIMARY KEY (session_id, seq));
        CREATE TABLE IF NOT EXISTS transcript_rewrite_watermarks (session_id TEXT NOT NULL PRIMARY KEY, generation TEXT NOT NULL,
          updated_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS auth_profile_store (store_key TEXT NOT NULL PRIMARY KEY, value_json TEXT NOT NULL);
        """;

    private readonly SqliteConnection _db;
    private readonly Dictionary<string, long> _seq = new(StringComparer.Ordinal);

    public OpenClawWriter(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _db = SqliteConnection.Open(path, SqliteOpenMode.ReadWriteCreate);
        _db.Execute("PRAGMA journal_mode=WAL");
        _db.Execute(Schema);
    }

    public string Path { get; }

    public static string Iso(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public OpenClawWriter Window(string session, string key, long created, string? harness = "openclaw", string? spawnedBy = null)
    {
        using (var window = _db.Prepare(
            "INSERT INTO session_windows (session_id, session_key, created_at, updated_at, transcript_updated_at, agent_harness_id, spawned_by) " +
            "VALUES (?1, ?2, ?3, ?3, ?3, ?4, ?5)"))
        {
            window.Bind(1, session).Bind(2, key).Bind(3, created).Bind(4, harness).Bind(5, spawnedBy).Run();
        }

        using (var node = _db.Prepare(
            "INSERT OR REPLACE INTO session_nodes (session_key, current_session_id, entry_json, updated_at, spawned_by) VALUES (?1, ?2, '{}', ?3, ?4)"))
        {
            node.Bind(1, key).Bind(2, session).Bind(3, created).Bind(4, spawnedBy).Run();
        }

        return Entry(session, created, new JsonObject
        {
            ["type"] = "session", ["version"] = 4, ["id"] = session, ["timestamp"] = Iso(created), ["cwd"] = "/Users/dev/projects/relay",
        });
    }

    public OpenClawWriter User(string session, string id, long at) => Entry(session, at, Message(id, at, new JsonObject
    {
        ["role"] = "user", ["content"] = new JsonArray(), ["timestamp"] = at,
    }));

    public OpenClawWriter Assistant(string session, string id, long started, long appended, long output, string stop = "stop",
        string? toolCall = null, string api = "anthropic-messages", string provider = "anthropic", string model = "claude-opus-5-5")
    {
        var content = new JsonArray();
        if (toolCall is not null)
        {
            content.Add(new JsonObject { ["type"] = "toolCall", ["id"] = toolCall, ["name"] = "read", ["arguments"] = new JsonObject { ["path"] = "a.ts" } });
        }

        return Entry(session, appended, Message(id, appended, new JsonObject
        {
            ["role"] = "assistant", ["content"] = content, ["api"] = api, ["provider"] = provider, ["model"] = model,
            ["usage"] = new JsonObject { ["input"] = 10, ["output"] = output, ["cacheRead"] = 1000, ["cacheWrite"] = 0 },
            ["stopReason"] = stop, ["timestamp"] = started,
        }));
    }

    public OpenClawWriter ToolResult(string session, string id, long at, string callId) => Entry(session, at, Message(id, at, new JsonObject
    {
        ["role"] = "toolResult", ["toolCallId"] = callId, ["toolName"] = "read", ["content"] = new JsonArray(), ["isError"] = false, ["timestamp"] = at,
    }));

    /// <summary>OpenClaw rewrote the window (compaction repair, branch trim): new rows from 1 and a new watermark generation.</summary>
    public OpenClawWriter Rewrite(string session, string generation, long at)
    {
        using (var delete = _db.Prepare("DELETE FROM transcript_events WHERE session_id = ?1 AND seq > 1"))
        {
            delete.Bind(1, session).Run();
        }

        _seq[session] = 1;
        using var watermark = _db.Prepare("INSERT OR REPLACE INTO transcript_rewrite_watermarks (session_id, generation, updated_at) VALUES (?1, ?2, ?3)");
        watermark.Bind(1, session).Bind(2, generation).Bind(3, at).Run();
        return Touch(session, at);
    }

    public OpenClawWriter DeleteWindow(string session)
    {
        using var events = _db.Prepare("DELETE FROM transcript_events WHERE session_id = ?1");
        events.Bind(1, session).Run();
        using var window = _db.Prepare("DELETE FROM session_windows WHERE session_id = ?1");
        window.Bind(1, session).Run();
        return this;
    }

    public void Dispose() => _db.Dispose();

    private static JsonObject Message(string id, long at, JsonObject message) => new()
    {
        ["type"] = "message", ["id"] = id, ["timestamp"] = Iso(at), ["message"] = message,
    };

    private OpenClawWriter Entry(string session, long at, JsonObject entry)
    {
        var seq = _seq[session] = _seq.GetValueOrDefault(session) + 1;
        using (var insert = _db.Prepare("INSERT INTO transcript_events (session_id, seq, event_json, created_at) VALUES (?1, ?2, ?3, ?4)"))
        {
            insert.Bind(1, session).Bind(2, seq).Bind(3, entry.ToJsonString()).Bind(4, at).Run();
        }

        return Touch(session, at);
    }

    private OpenClawWriter Touch(string session, long at)
    {
        using var update = _db.Prepare("UPDATE session_windows SET updated_at = ?2, transcript_updated_at = ?2 WHERE session_id = ?1");
        update.Bind(1, session).Bind(2, at).Run();
        return this;
    }
}

/// <summary>The OpenClaw source in the monitor while the Gateway writes its agent database (METRICS 4.14, 5).</summary>
public sealed class OpenClawLiveTests : IDisposable
{
    private const string Main = "0f1c2d3e-0001-4a00-8000-00000000c001";
    private const string Child = "0f1c2d3e-0002-4a00-8000-00000000c002";

    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Dispose() => _temp.Dispose();

    private string StateFolder => _temp.Combine("home", ".openclaw");

    private string DatabasePath => System.IO.Path.Combine(StateFolder, "agents", "main", "agent", OpenClawPaths.DatabaseName);

    private long TodayStart => _now - 3_600_000;

    [Fact]
    public void AGrowingDatabaseIsReadIncrementallyLikeTheGatewayWritesIt()
    {
        var t = _now - 300_000;
        using var writer = new OpenClawWriter(DatabasePath);
        writer.Window(Main, "agent:main:main", t).User(Main, "u1", t + 1000);
        using var core = Core();
        var report = core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((1, 1), (report.FilesDiscovered, report.FilesRead));

        var agent = Assert.Single(Build(core).Agents);
        Assert.Equal((DatabasePath + "#" + Main, SourceKind.OpenClaw, AgentPhase.Generating, "relay"), (agent.Id, agent.Source, agent.Phase, agent.Project));

        writer.Assistant(Main, "a1", t + 1005, t + 4000, 300, "toolUse", toolCall: "c1");
        Assert.True(core.ProcessPaths([DatabasePath + "-wal"], _now, TodayStart));
        Assert.Equal(AgentPhase.Tools, Assert.Single(Build(core).Agents).Phase);

        writer.ToolResult(Main, "r1", t + 4500, "c1").Assistant(Main, "a2", t + 4505, t + 6505, 200);
        Assert.True(core.PollRecent(_now));
        var snapshot = Build(core);
        Assert.Equal(AgentPhase.Idle, Assert.Single(snapshot.Agents).Phase);
        var source = snapshot.Sources.Single(s => s.Kind == SourceKind.OpenClaw);
        Assert.Equal((500L, 2L, 1, true, StateFolder), (source.Today.Output, source.Today.Requests, source.SessionCount, source.Available, source.RootPath));
        var parser = Assert.Single(core.OpenClaw!.Parsers);
        Assert.Equal([("read", (bool?)false)], parser.WorkActions.Select(a => (a.Tool, a.Failed)));

        Assert.False(core.PollRecent(_now));
        Assert.False(core.ProcessPaths([DatabasePath], _now, TodayStart));
    }

    [Fact]
    public void ANewWindowARewriteAndADeletedWindowFollowTheDatabase()
    {
        var t = _now - 300_000;
        using var writer = new OpenClawWriter(DatabasePath);
        writer.Window(Main, "agent:main:main", t).User(Main, "u1", t + 1000).Assistant(Main, "a1", t + 1005, t + 3005, 100);
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);

        writer.Window(Child, "agent:main:subagent:5d1e", t + 10_000, spawnedBy: "agent:main:main")
            .User(Child, "u2", t + 10_100).Assistant(Child, "a2", t + 10_105, t + 12_105, 40, model: "claude-sonnet-5-5");
        core.ProcessPaths([DatabasePath + "-wal"], _now, TodayStart);
        var agents = Build(core).Agents.ToDictionary(a => a.Id);
        Assert.Equal(DatabasePath + "#" + Main, agents[DatabasePath + "#" + Child].ParentId);
        Assert.True(agents[DatabasePath + "#" + Child].IsSubagent);
        Assert.Equal(1, Source(core).SessionCount);

        // The rewrite keeps the same key for a1 (an update) and adds a3; the dedupe set is kept.
        writer.Rewrite(Main, "g2", t + 20_000).User(Main, "u1", t + 1000).Assistant(Main, "a1", t + 1005, t + 3005, 100)
            .User(Main, "u3", t + 20_000).Assistant(Main, "a3", t + 20_005, t + 22_005, 60);
        core.ProcessPaths([DatabasePath + "-wal"], _now, TodayStart);
        Assert.Equal((200L, 3L), (Source(core).Today.Output, Source(core).Today.Requests));

        writer.DeleteWindow(Child);
        core.ProcessPaths([DatabasePath + "-wal"], _now, TodayStart);
        var remaining = Assert.Single(Build(core).Agents);
        Assert.Equal(DatabasePath + "#" + Main, remaining.Id);
        Assert.Equal(200L, Source(core).Today.Output);
    }

    [Fact]
    public void DelegatedTurnsCountNothingAndTheAgentScopedCodexHomeIsReadAsCodex()
    {
        var t = _now - 300_000;
        using (var writer = new OpenClawWriter(DatabasePath))
        {
            writer.Window(Main, "agent:main:codex1", t, harness: "codex").User(Main, "u1", t + 100)
                .Assistant(Main, "m1", t + 200, t + 40_000, 510, api: "openai-chatgpt-responses", provider: "openai", model: "gpt-5.5");
        }

        var rollout = System.IO.Path.Combine(StateFolder, "agents", "main", "agent", "codex-home", "sessions", "2026", "10", "26", "rollout-a.jsonl");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(rollout)!);
        File.WriteAllLines(rollout,
        [
            CodexLine(t + 200, "session_meta", """{"id":"019a","cwd":"/w","originator":"openclaw","model_provider":"openai"}"""),
            CodexLine(t + 300, "event_msg", """{"type":"task_started"}"""),
            CodexLine(t + 20_000, "token_usage_record", """{"response_id":"resp-1","usage":{"input_tokens":100,"cached_input_tokens":0,"output_tokens":510,"reasoning_output_tokens":0}}"""),
        ]);

        var enabled = new HashSet<SourceKind> { SourceKind.Codex, SourceKind.OpenClaw };
        var locations = new SourceLocations(_temp.Combine("home", ".codex", "sessions"), string.Empty, string.Empty, string.Empty, [],
            OpenClawRoots: [StateFolder], OpenClawAutomatic: true);
        using var core = new MonitorCore(new MonitorConfiguration(enabled), locations, TodayStart) { AutomaticOpenClawRoots = () => [StateFolder] };
        core.Seed(_now, TodayStart, CancellationToken.None);

        var snapshot = Build(core);
        Assert.Equal(0L, snapshot.Sources.Single(s => s.Kind == SourceKind.OpenClaw).Today.Output);
        Assert.Equal(510L, snapshot.Sources.Single(s => s.Kind == SourceKind.Codex).Today.Output);
        Assert.Equal(AgentPhase.Idle, snapshot.Agents.Single(a => a.Source == SourceKind.OpenClaw).Phase);
    }

    [Fact]
    public void ADatabaseCreatedWhileRunningJoinsAndItsCodexHomeBecomesACodexRoot()
    {
        var t = _now - 300_000;
        Directory.CreateDirectory(StateFolder);
        var enabled = new HashSet<SourceKind> { SourceKind.Codex, SourceKind.OpenClaw };
        var locations = new SourceLocations(_temp.Combine("home", ".codex", "sessions"), string.Empty, string.Empty, string.Empty, [],
            OpenClawRoots: [StateFolder], OpenClawAutomatic: true);
        using var core = new MonitorCore(new MonitorConfiguration(enabled), locations, TodayStart) { AutomaticOpenClawRoots = () => [StateFolder] };
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.True(Source(core).Available);
        Assert.Empty(Build(core).Agents);

        using (var writer = new OpenClawWriter(DatabasePath))
        {
            writer.Window(Main, "agent:main:main", t).User(Main, "u1", t + 1000).Assistant(Main, "a1", t + 1005, t + 3005, 70);
        }

        Assert.True(core.Rediscover(_now, TodayStart, CancellationToken.None));
        Assert.Equal(70L, Source(core).Today.Output);
        var codexHome = System.IO.Path.Combine(StateFolder, "agents", "main", "agent", "codex-home", "sessions");
        Assert.Contains(core.Roots, r => r.Family == RootFamily.Codex && r.Path == codexHome);
        Assert.Contains(core.WatchRoots, r => r.Family == RootFamily.OpenClaw && r.Path == System.IO.Path.Combine(StateFolder, "agents"));
    }

    [Fact]
    public void AWindowLastUpdatedBeforeTheBacklogStartsAtItsEndAndReadsItsHeaderWhenItGrows()
    {
        var old = TodayStart - 7_200_000;
        using var writer = new OpenClawWriter(DatabasePath);
        writer.Window(Main, "agent:main:main", old).User(Main, "u0", old + 100).Assistant(Main, "a0", old + 105, old + 2105, 999);
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Empty(Build(core).Agents);

        var t = _now - 60_000;
        writer.User(Main, "u1", t).Assistant(Main, "a1", t + 5, t + 2005, 30);
        core.ProcessPaths([DatabasePath + "-wal"], _now, TodayStart);
        var agent = Assert.Single(Build(core).Agents);
        Assert.Equal(("relay", AgentPhase.Idle), (agent.Project, agent.Phase));
        Assert.Equal((30L, 1L), (Source(core).Today.Output, Source(core).Today.Requests));
    }

    [Fact]
    public void ADatabaseWithoutSessionTablesIsUnsupported()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath)!);
        using (var db = SqliteConnection.Open(DatabasePath, SqliteOpenMode.ReadWriteCreate))
        {
            db.Execute("CREATE TABLE schema_meta (meta_key TEXT PRIMARY KEY); CREATE TABLE auth_profile_store (store_key TEXT PRIMARY KEY, value_json TEXT)");
        }

        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Contains(new MonitorWarning.UnsupportedFormat(SourceKind.OpenClaw), core.Warnings());
        Assert.Empty(Build(core).Agents);
    }

    [Fact]
    public void HistoryStoresWindowsAsAgentsRereadsOnlyChangedDatabasesAndKeepsPrunedWindows()
    {
        var t = _now - 300_000;
        using var writer = new OpenClawWriter(DatabasePath);
        writer.Window(Main, "agent:main:main", t).User(Main, "u1", t + 1000).Assistant(Main, "a1", t + 1005, t + 3005, 100)
            .Window(Child, "agent:main:subagent:5d1e", t + 10_000, spawnedBy: "agent:main:main")
            .User(Child, "u2", t + 10_100).Assistant(Child, "a2", t + 10_105, t + 12_105, 40);
        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], OpenClawRoots: [StateFolder]);
        var enabled = new HashSet<SourceKind> { SourceKind.OpenClaw };
        using var store = History.HistoryStore.Open(_temp.Combine("store", "history.sqlite"));
        var analyzer = new History.HistoryAnalyzer(store);

        var first = analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None);
        var day = Assert.Single(analyzer.Days());
        Assert.Equal((1, 1, SourceKind.OpenClaw, 140L, 2L, 2), (first.FilesPlanned, first.FilesAnalyzed, day.Source, day.Totals.Output, day.Totals.Requests, day.Agents));
        Assert.Equal(0, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);

        writer.DeleteWindow(Child).User(Main, "u3", t + 20_000).Assistant(Main, "a3", t + 20_005, t + 22_005, 60);
        Assert.Equal(1, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        day = Assert.Single(analyzer.Days());
        Assert.Equal((200L, 3L, 2), (day.Totals.Output, day.Totals.Requests, day.Agents));
    }

    private static string CodexLine(long at, string type, string payload) =>
        $$"""{"timestamp":"{{OpenClawWriter.Iso(at)}}","type":"{{type}}","payload":{{payload}}}""";

    private MonitorCore Core() => new(
        new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.OpenClaw }),
        new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], OpenClawRoots: [StateFolder]),
        TodayStart);

    private SourceStatus Source(MonitorCore core) => Build(core).Sources.Single(s => s.Kind == SourceKind.OpenClaw);

    private SpeedSnapshot Build(MonitorCore core) =>
        core.BuildSnapshot(_now, TodayStart, MonitorState.Running, observeLive: false).Snapshot;
}
