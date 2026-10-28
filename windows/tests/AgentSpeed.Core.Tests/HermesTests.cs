using System.Text;
using System.Text.Json;
using AgentSpeed.Core.Hermes;
using AgentSpeed.Core.History;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Traffic.Engine;

namespace AgentSpeed.Core.Tests;

public sealed class HermesTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private const string Timestamp = "2026-10-20 12:00:04,000";
    private const string Usage = "API call #1: model=claude-sonnet-4 provider=anthropic in=1000 out=200 total=1200 latency=4.0s cache=600/1000 (60%) write=100";

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void CompletedUsageUsesMeasuredDurationAndDoesNotAddCacheToInput()
    {
        var parser = Parser("logs/agent.log");
        var sink = new RecordingSink();
        Feed(parser, sink, $"{Timestamp} INFO[session-a] agent.conversation_loop: {Usage}");
        var response = Assert.Single(sink.Latest);
        Assert.Equal("hermes:session-a:1792497604000:1", response.Key);
        Assert.Equal(4000, response.EndMs - response.StartMs);
        Assert.Equal(new TokenCounts(1000, 600, 100, 200), response.Tokens);
        Assert.Equal("anthropic", response.Provider);
        Assert.Equal(AgentPhase.Idle, Assert.Single(parser.Agents).Phase);
    }

    [Fact]
    public void SharedFixturesMatchTheIndependentReferenceWithStableRotationKeys()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(FixtureLocator.PathOf("hermes/expected.json")));
        var deduper = new Deduper();
        foreach (var file in expected.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = FixtureLocator.PathOf(file.GetProperty("path").GetString()!);
            var parser = new HermesParser(path, deduper, TimeZoneInfo.Utc);
            var sink = new RecordingSink();
            foreach (var line in File.ReadLines(path)) Feed(parser, sink, line);
            var rows = file.GetProperty("responses").EnumerateArray().ToArray();
            Assert.Equal(rows.Length, sink.Latest.Count);
            foreach (var (row, response) in rows.Zip(sink.Latest))
            {
                AssertResponse(row, response, Path.GetDirectoryName(path)!);
            }
        }
    }

    [Fact]
    public void SharedEdgeCasesAndTimeZonesAgreeAcrossPlatforms()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(FixtureLocator.PathOf("hermes/expected.json")));
        var path = _temp.Combine("logs/agent.log");
        foreach (var test in expected.RootElement.GetProperty("lineCases").EnumerateArray())
        {
            var parser = new HermesParser(path, new Deduper(), TimeZoneInfo.Utc);
            var sink = new RecordingSink();
            var line = test.GetProperty("line").GetString()!;
            if (test.TryGetProperty("padToBytes", out var pad)) line += new string(' ', pad.GetInt32() - Encoding.UTF8.GetByteCount(line));
            Feed(parser, sink, line);
            if (test.GetProperty("response").ValueKind == JsonValueKind.Null) Assert.Empty(sink.Latest);
            else AssertResponse(test.GetProperty("response"), Assert.Single(sink.Latest), Path.GetDirectoryName(path)!);
        }

        foreach (var test in expected.RootElement.GetProperty("timeZones").EnumerateArray())
        {
            var parser = new HermesParser(path, new Deduper(), TimeZoneInfo.FindSystemTimeZoneById(test.GetProperty("zone").GetString()!));
            var sink = new RecordingSink();
            Feed(parser, sink, test.GetProperty("line").GetString()!);
            if (test.GetProperty("endMs").ValueKind == JsonValueKind.Null) Assert.Empty(sink.Latest);
            else Assert.Equal(test.GetProperty("endMs").GetInt64(), Assert.Single(sink.Latest).EndMs);
        }
    }

    [Theory]
    [InlineData("0.0")]
    [InlineData("901.0")]
    public void UnknownDurationRetainsUsageWithoutInventingSpeed(string latency)
    {
        var parser = Parser("logs/agent.log");
        var sink = new RecordingSink();
        Feed(parser, sink, $"{Timestamp} INFO agent.turn_usage: {Usage.Replace("4.0s", latency + "s", StringComparison.Ordinal)}");
        var response = Assert.Single(sink.Latest);
        Assert.Null(response.StartMs);
        Assert.Equal(200, response.Tokens.Output);
        Assert.StartsWith("hermes:" + _temp.Combine("logs") + ":", response.Key, StringComparison.Ordinal);
    }

    [Fact]
    public void RotationDedupAndCallCounterReuseKeepDistinctSessionsAndResponses()
    {
        var deduper = new Deduper();
        var first = Parser("logs/agent.log.1", deduper);
        var second = Parser("logs/agent.log", deduper);
        var sink = new RecordingSink();
        var row = $"{Timestamp} INFO[session-a] run_agent: {Usage}";
        Feed(first, sink, row);
        Feed(second, sink, row);
        Feed(second, sink, row.Replace("04,000", "05,000", StringComparison.Ordinal));
        Feed(second, sink, row.Replace("session-a", "session-b", StringComparison.Ordinal));
        Assert.Equal(3, sink.Emissions.Count);
        Assert.Equal(2, second.Agents.Count());
        Assert.Equal(first.Agents.Single().Id, second.Agents.First().Id);
        second.Reset();
        Feed(second, sink, row);
        Assert.Equal(3, sink.Emissions.Count);
    }

    [Fact]
    public void NoiseMissingUsageAndUntrustedLoggerAreIgnored()
    {
        var parser = Parser("logs/agent.log");
        var sink = new RecordingSink();
        Feed(parser, sink, $"{Timestamp} INFO[session] tool.execute: {Usage}");
        Feed(parser, sink, $"{Timestamp} INFO[session] agent.turn_usage: {Usage.Replace("in=1000", "in=?", StringComparison.Ordinal)}");
        Feed(parser, sink, "prompt content mentioning API call #1");
        Feed(parser, sink, "2026-99-99 12:00:04,000 INFO[s] agent.turn_usage: " + Usage);
        Assert.Empty(sink.Latest);
        Assert.Empty(parser.Agents);
    }

    [Fact]
    public void OversizedCountersClampAndLiteralUnknownSessionRemainsATag()
    {
        var parser = Parser("logs/agent.log");
        var sink = new RecordingSink();
        Feed(parser, sink, $"{Timestamp} INFO[unknown] agent.turn_usage: API call #1: model=unknown provider=unknown in=999999999999999999999 out=999999999999999999999 total=999999999999999999999 latency=1.0s cache=999999999999999999999/1000 (100%) write=999999999999999999999");
        var response = Assert.Single(sink.Latest);
        Assert.StartsWith("hermes:unknown:", response.Key, StringComparison.Ordinal);
        Assert.Null(response.Model);
        Assert.Null(response.Provider);
        Assert.Equal(new TokenCounts(TokenCounts.MaxValue, TokenCounts.MaxValue, TokenCounts.MaxValue, TokenCounts.MaxValue), response.Tokens);
        Feed(parser, sink, $"{Timestamp} INFO[another] agent.turn_usage: {Usage} " + new string('a', 64 * 1024));
        Assert.Single(sink.Latest);
    }

    [Fact]
    public void TailerWaitsForACompleteLineBeforeEmittingUsage()
    {
        var path = _temp.WriteFile("logs/agent.log", $"{Timestamp} INFO[s] agent.turn_usage: {Usage}");
        var parser = new HermesParser(path, new Deduper(), TimeZoneInfo.Utc);
        var tailer = TranscriptTailer.For(path);
        var sink = new RecordingSink();
        var batch = parser.NewBatch();
        tailer.ReadAppended(batch);
        batch.ApplyTo(sink);
        Assert.Empty(sink.Latest);
        File.AppendAllText(path, "\n");
        batch = parser.NewBatch();
        tailer.ReadAppended(batch);
        batch.ApplyTo(sink);
        Assert.Single(sink.Latest);
    }

    [Fact]
    public void HermesTailerLimitsPartialLinesAndRecoversAfterAnOversizedLine()
    {
        var path = _temp.WriteFile("logs/agent.log", new string('x', HermesParser.MaxLineBytes + 1));
        var tailer = Families.CreateTailer(RootFamily.Hermes, path);
        var parser = new HermesParser(path, new Deduper(), TimeZoneInfo.Utc);
        var sink = new RecordingSink();
        var batch = parser.NewBatch();
        tailer.ReadAppended(batch);
        batch.ApplyTo(sink);
        File.AppendAllText(path, $"\n{Timestamp} INFO[s] agent.turn_usage: {Usage}\n");
        batch = parser.NewBatch();
        tailer.ReadAppended(batch);
        batch.ApplyTo(sink);
        Assert.Equal(1, tailer.OversizedLines);
        Assert.Single(sink.Latest);
    }

    [Fact]
    public void DiscoveryIncludesProfilesAndRotationsButExcludesOtherLogsAndSymlinks()
    {
        var current = _temp.WriteFile("logs/agent.log", "");
        var rotated = _temp.WriteFile("logs/agent.log.1", "");
        var profile = _temp.WriteFile("profiles/work/logs/agent.log", "");
        _temp.WriteFile("logs/gateway.log", "");
        _temp.WriteFile("logs/agent.log.bak", "");
        _temp.WriteFile("artifacts/logs/agent.log", "");
        _temp.WriteFile("profiles/.hidden/logs/agent.log", "");
        Assert.Equal(new[] { current, rotated, profile }.Order(), HermesPaths.Enumerate(_temp.Path, int.MaxValue, default).Select(file => file.Path).Order());
        Assert.Equal(2, HermesPaths.Enumerate(_temp.Combine("logs"), int.MaxValue, default).Count);
        Assert.False(TranscriptDiscovery.IsAcceptablePath(_temp.Path, _temp.Combine("artifacts/logs/agent.log"), TranscriptNaming.HermesLog));
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(_temp.Combine("logs/agent.log.2"), current);
            Assert.Equal(3, HermesPaths.Enumerate(_temp.Path, int.MaxValue, default).Count);
        }
    }

    [Fact]
    public void MultiSessionLogsReachMonitorAndPersistedHistoryOnlyOnceAcrossRotation()
    {
        var one = $"{Timestamp} INFO[session-a] agent.turn_usage: {Usage}\n";
        _temp.WriteFile("logs/agent.log.1", one);
        var current = _temp.WriteFile("logs/agent.log", one + one.Replace("session-a", "session-b", StringComparison.Ordinal));
        var paths = new SourceLocations("", "", "", "", [], HermesRoot: _temp.Path);
        var enabled = new HashSet<SourceKind> { SourceKind.Hermes };
        var end = new DateTimeOffset(new DateTime(2026, 10, 20, 12, 0, 4, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var start = end - 86_400_000;
        using (var monitor = new MonitorCore(new MonitorConfiguration(enabled), paths, start))
        {
            monitor.Seed(end + 1000, start, default);
            var (snapshot, _) = monitor.BuildSnapshot(end + 1000, start, MonitorState.Running, observeLive: false);
            Assert.Equal(2, snapshot.Today.Requests);
            Assert.Equal(400, snapshot.Today.Output);
            Assert.Equal(2, monitor.SessionCount(SourceKind.Hermes, start));
            Assert.Equal(2, snapshot.Agents.Count(agent => agent.Source == SourceKind.Hermes));
        }

        using var store = HistoryStore.Open(_temp.Combine("history.sqlite"));
        var history = new HistoryAnalyzer(store);
        Assert.Equal(0, history.Run(paths, enabled, TimeZoneInfo.Utc, default).FilesUnreadable);
        Assert.Equal(2, history.Days().Sum(day => day.Totals.Requests));
        Assert.Equal(2, history.Days().Sum(day => day.Agents));
        Assert.Equal(0, history.Run(paths, enabled, TimeZoneInfo.Utc, default).FilesPlanned);
        File.AppendAllText(current, one.Replace("04,000", "05,000", StringComparison.Ordinal));
        history.Run(paths, enabled, TimeZoneInfo.Utc, default);
        Assert.Equal(3, history.Days().Sum(day => day.Totals.Requests));
        Assert.Equal(2, history.Days().Sum(day => day.Agents));
    }

    [Fact]
    public void HistoryRetainsUsageAfterRotationReusesEveryFilenameAndTheOldestSegmentExpires()
    {
        var row = $"{Timestamp} INFO[session-a] agent.turn_usage: {Usage}\n";
        var rotated = _temp.WriteFile("logs/agent.log.1", row);
        var current = _temp.WriteFile("logs/agent.log", row.Replace("04,000", "05,000", StringComparison.Ordinal));
        var paths = new SourceLocations("", "", "", "", [], HermesRoot: _temp.Path);
        var enabled = new HashSet<SourceKind> { SourceKind.Hermes };
        var storePath = _temp.Combine("history.sqlite");
        using (var store = HistoryStore.Open(storePath))
        {
            var history = new HistoryAnalyzer(store);
            history.Run(paths, enabled, TimeZoneInfo.Utc, default);
            Assert.Equal(2, history.Days().Sum(day => day.Totals.Requests));
            for (var second = 6; second <= 8; second++)
            {
                File.Delete(rotated);
                File.Move(current, rotated);
                File.WriteAllText(current, row.Replace("04,000", $"{second:00},000", StringComparison.Ordinal));
                history.Run(paths, enabled, TimeZoneInfo.Utc, default);
                Assert.Equal(second - 3, history.Days().Sum(day => day.Totals.Requests));
                Assert.Equal(1, history.Days().Sum(day => day.Agents));
            }
        }

        using (var store = HistoryStore.Open(storePath))
        {
            var history = new HistoryAnalyzer(store);
            Assert.Equal(5, history.Days().Sum(day => day.Totals.Requests));
            Assert.Equal(1, history.Days().Sum(day => day.Agents));
            Assert.Equal(0, history.Run(paths, enabled, TimeZoneInfo.Utc, default).FilesPlanned);
        }
    }

    [Fact]
    public void ExistingHistoryGainsOnlyHashedHermesSessionCountsWithoutChangingOtherSources()
    {
        var storePath = _temp.Combine("history.sqlite");
        using (var store = HistoryStore.Open(storePath))
        {
            store.Save([(new StoredFile
            {
                Path = "existing-codex", Family = "Codex", Version = HistoryAnalyzer.AnalysisVersion,
                Days = new Dictionary<DayKey, DayAggregate>
                {
                    [new DayKey("2026-10-20", SourceKind.Codex)] = new() { Requests = 3, Output = 30, Agents = 3 },
                },
            }, [])]);
        }

        using (var connection = SqliteConnection.Open(storePath, SqliteOpenMode.ReadWriteCreate))
        {
            connection.Execute("DROP TABLE contribution_hermes_agents");
        }

        var row = $"{Timestamp} INFO[session-a] agent.turn_usage: {Usage}\n";
        _temp.WriteFile("logs/agent.log", row + row.Replace("session-a", "session-b", StringComparison.Ordinal));
        using (var store = HistoryStore.Open(storePath))
        {
            var history = new HistoryAnalyzer(store);
            Assert.Equal(3, Assert.Single(history.Days()).Agents);
            history.Run(new SourceLocations("", "", "", "", [], HermesRoot: _temp.Path),
                new HashSet<SourceKind> { SourceKind.Hermes }, TimeZoneInfo.Utc, default);
            Assert.Equal(2, Assert.Single(history.Days(new HashSet<SourceKind> { SourceKind.Hermes })).Agents);
            Assert.Equal(3, Assert.Single(history.Days(new HashSet<SourceKind> { SourceKind.Codex })).Agents);
        }

        using (var store = HistoryStore.Open(storePath))
        {
            Assert.Equal(2, Assert.Single(new HistoryAnalyzer(store).Days(new HashSet<SourceKind> { SourceKind.Hermes })).Agents);
        }

        using (var connection = SqliteConnection.Open(storePath, SqliteOpenMode.ReadOnly))
        using (var rows = connection.Prepare("SELECT hashes FROM contribution_hermes_agents"))
        {
            Assert.True(rows.Step());
            Assert.Equal(2 * sizeof(ulong), rows.GetBlob(0).Length);
        }
    }

    [Fact]
    public void TrafficRecognizesHermesEntryPointsWithoutMatchingUnrelatedPythonArguments()
    {
        var rules = new AgentProcessRules(new TrafficPlatform(TrafficPlatformKind.Windows, @"C:\Users\test"));
        Assert.Equal(SourceKind.Hermes, rules.Source(@"C:\bin\hermes.exe", () => null, []));
        Assert.Equal(SourceKind.Hermes, rules.Source(@"C:\Python\python.exe", () => ["python", "-m", "hermes_cli.main"], []));
        Assert.True(rules.NeedsArguments(@"C:\Python\python.exe", () => null));
        Assert.Null(rules.Source(@"C:\Python\python.exe", () => ["python", "other.py", "hermes_cli"], []));
    }

    private static void AssertResponse(JsonElement row, ParsedResponse response, string logDirectory)
    {
        Assert.Equal(row.GetProperty("key").GetString()!.Replace("@LOG_ROOT@", logDirectory, StringComparison.Ordinal)
            .Replace("fixture-root", logDirectory, StringComparison.Ordinal), response.Key);
        Assert.Equal(row.GetProperty("startMs").ValueKind == JsonValueKind.Null ? (long?)null : row.GetProperty("startMs").GetInt64(), response.StartMs);
        Assert.Equal(row.GetProperty("endMs").GetInt64(), response.EndMs);
        Assert.Equal(row.GetProperty("model").GetString(), response.Model);
        Assert.Equal(row.GetProperty("provider").GetString(), response.Provider);
        Assert.Equal(row.GetProperty("input").GetInt64(), response.Tokens.Input);
        Assert.Equal(row.GetProperty("output").GetInt64(), response.Tokens.Output);
        Assert.Equal(row.GetProperty("cachedInput").GetInt64(), response.Tokens.CachedInput);
        Assert.Equal(row.GetProperty("cacheWriteInput").GetInt64(), response.Tokens.CacheWriteInput);
        Assert.Equal(0, response.Tokens.Reasoning);
        Assert.Equal(1, response.Requests);
        Assert.DoesNotContain("PRIVATE_SYNTHETIC", response.ToString(), StringComparison.Ordinal);
    }

    private HermesParser Parser(string relative, IDeduper? deduper = null) => new(_temp.Combine(relative), deduper ?? new Deduper(), TimeZoneInfo.Utc);
    private static void Feed(HermesParser parser, RecordingSink sink, string line) => parser.Feed(Encoding.UTF8.GetBytes(line), sink);
}
