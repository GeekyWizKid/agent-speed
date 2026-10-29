using System.Collections.Concurrent;
using System.Diagnostics;
using AgentSpeed.Core.History;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.OpenClaw;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The task records of the daily history (METRICS 6.11 <i>Store</i>): the side table, its version check, the re-analysis
/// of files whose last task is incomplete, and the calibration and publication after every run.
/// </summary>
public sealed class HistoryTaskRecordTests : IDisposable
{
    private const long Minute = 60_000;
    private const string OpenClawMain = "0f1c2d3e-0001-4a00-8000-00000000c001";

    private static readonly IReadOnlySet<SourceKind> Claude = new HashSet<SourceKind> { SourceKind.ClaudeCode };
    private static readonly IReadOnlySet<SourceKind> All = new HashSet<SourceKind>(Enum.GetValues<SourceKind>());

    private readonly TempDirectory _temp = new();
    private readonly long _t = ClaudeLines.T0;

    public void Dispose() => _temp.Dispose();

    private string StorePath => _temp.Combine("store", "history.sqlite");

    private SourceLocations Locations => new(
        _temp.Combine("codex", "sessions"), _temp.Combine("claude", "projects"), _temp.Combine("dsh", "sessions"),
        _temp.Combine("opencode"), []);

    private string OpenClawDatabase => Path.Combine(_temp.Combine("home", ".openclaw"), "agents", "main", "agent", OpenClawPaths.DatabaseName);

    [Fact]
    public void RecordsRoundTripThroughTheStoreWithEveryField()
    {
        const string path = "/logs/a.jsonl";
        TaskRecord full = Record(path, 0) with
        {
            Model = "gpt-6.1-sol",
            Effort = "high",
            State = TaskState.Failing,
            Score = 61.123456789012345,
            Dims = new TaskDims(12.5, 0.1, 15, 7.333333333333333, 1e-9),
            Counts = new TaskCounts(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12),
            FirstPassMs = _t + 4000,
            Success = true,
            Stalled = true,
            Snapshots = [[1, 0, 1, 0.1, 0.30000000000000004, 0, 0, 1.0 / 3, 1], [2, 1, 0, 1, 0.5, 0.25, -0.0, 0.17609125905568124, 0]],
            FixRows = [[0, 0.125, 1, 0, 2.0 / 3, 1]],
        };
        var bare = Record(path, 1) with
        {
            Source = SourceKind.ClaudeDesktop,
            Day = string.Empty,
            Complete = false,
            Model = null,
            Effort = null,
            State = TaskState.None,
            Score = null,
            FirstPassMs = null,
            Snapshots = [],
            FixRows = [],
        };
        using (var store = HistoryStore.Open(StorePath))
        {
            store.Save([(Entry(path, full, bare), [])]);
        }

        Assert.Equal(
            """{"actions":1,"counted":2,"edits":3,"oscillations":4,"loopActions":5,"runs":6,"failingRuns":7,"episodes":8,"recovered":9,"recoverAttempts":10,"fixAttempts":11,"fixFailures":12}""",
            Text("SELECT counts FROM task_records WHERE ordinal = 0"));
        Assert.Equal("[[0,0.125,1,0,0.6666666666666666,1]]", Text("SELECT fix_rows FROM task_records WHERE ordinal = 0"));
        Assert.Equal(("failing", "codex", "[]"), (Text("SELECT state FROM task_records WHERE ordinal = 0"),
            Text("SELECT source FROM task_records WHERE ordinal = 0"), Text("SELECT snapshots FROM task_records WHERE ordinal = 1")));
        Assert.Equal(("none", "claudeDesktop"), (Text("SELECT state FROM task_records WHERE ordinal = 1"),
            Text("SELECT source FROM task_records WHERE ordinal = 1")));
        Assert.Equal(1, Scalar("SELECT count(*) FROM task_records WHERE ordinal = 1 AND score IS NULL AND model IS NULL AND effort IS NULL AND first_pass_ms IS NULL"));
        Assert.Equal(HistoryAnalyzer.AnalysisVersion, Scalar("SELECT min(version) FROM task_records"));

        var entry = Load()[path];
        var loaded = entry.TaskRecords;
        Assert.Equal(2, loaded.Count);
        AssertSame(full, loaded[0]);
        AssertSame(bare, loaded[1]);
        Assert.Same(entry.Path, loaded[1].AgentId);
    }

    [Fact]
    public void SavingAFileAgainReplacesItsRecordsAndLeavesOtherFilesAlone()
    {
        var a = Entry("/logs/a.jsonl", Record("/logs/a.jsonl", 0), Record("/logs/a.jsonl", 1), Record("/logs/a.jsonl", 2));
        var b = Entry("/logs/b.jsonl", Record("/logs/b.jsonl", 0));
        using (var store = HistoryStore.Open(StorePath))
        {
            store.Save([(a, []), (b, [])]);
            a.TaskRecords = [Record("/logs/a.jsonl", 0) with { Score = 99 }];
            store.Save([(a, [])]);
        }

        var files = Load();
        Assert.Equal(99, Assert.Single(files["/logs/a.jsonl"].TaskRecords).Score);
        Assert.Single(files["/logs/b.jsonl"].TaskRecords);
        Assert.Equal(2, Scalar("SELECT count(*) FROM task_records"));

        using (var store = HistoryStore.Open(StorePath))
        {
            a.TaskRecords = [];
            store.Save([(a, [])]);
        }

        Assert.Empty(Load()["/logs/a.jsonl"].TaskRecords);
        Assert.Equal(1, Scalar("SELECT count(*) FROM task_records"));
    }

    [Fact]
    public void OnlyRowsOfTheFilesAnalysisVersionAreReadAndMalformedRowsAreSkipped()
    {
        const string path = "/logs/a.jsonl";
        using (var store = HistoryStore.Open(StorePath))
        {
            store.Save([(Entry(path, Record(path, 0), Record(path, 1)), [])]);
        }

        Execute("UPDATE task_records SET version = 7 WHERE ordinal = 1");
        Assert.Equal([0], Load()[path].TaskRecords.Select(r => r.Ordinal));

        // An older build analysed the file again: its version no longer matches any row.
        Execute("UPDATE files SET version = 6");
        Assert.Empty(Load()[path].TaskRecords);
        Assert.Equal(2, Scalar("SELECT count(*) FROM task_records"));

        Execute("UPDATE files SET version = 8; UPDATE task_records SET version = 8");
        Execute("""
            INSERT INTO task_records SELECT file_id, 2, source, model, effort, start_ms, end_ms, day, complete, 'confused', score, v, c, r, e, k, counts, first_pass_ms, success, stalled, snapshots, fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 3, 'nobody', model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, counts, first_pass_ms, success, stalled, snapshots, fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 4, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, '{"actions":1', first_pass_ms, success, stalled, snapshots, fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 5, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, replace(counts, '"fixFailures"', '"other"'), first_pass_ms, success, stalled, snapshots, fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 6, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, counts, first_pass_ms, success, stalled, '[[1,"x"]]', fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 7, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, counts, first_pass_ms, success, stalled, snapshots, '[1,2]', version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 8, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, counts, first_pass_ms, success, stalled, '[] []', fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 9, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, '{"actions":1.5,"counted":2,"edits":3,"oscillations":4,"loopActions":5,"runs":6,"failingRuns":7,"episodes":8,"recovered":9,"recoverAttempts":10,"fixAttempts":11,"fixFailures":12}', first_pass_ms, success, stalled, snapshots, fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records SELECT file_id, 10, source, model, effort, start_ms, end_ms, day, complete, state, score, v, c, r, e, k, '{"extra":[1,{"a":2}],"actions":1,"counted":2,"edits":3,"oscillations":4,"loopActions":5,"runs":6,"failingRuns":7,"episodes":8,"recovered":9,"recoverAttempts":10,"fixAttempts":11,"fixFailures":12}', first_pass_ms, success, stalled, snapshots, fix_rows, version FROM task_records WHERE ordinal = 0;
            INSERT INTO task_records VALUES (999, 0, 'codex', NULL, NULL, 1, 2, '2026-10-20', 1, 'none', NULL, 0, 0, 0, 0, 0, '{}', NULL, 0, 0, '[]', '[]', 8);
            """);
        var records = Load()[path].TaskRecords;
        Assert.Equal([0, 1, 10], records.Select(r => r.Ordinal));
        Assert.Equal(12, records[2].Counts.FixFailures);
    }

    [Fact]
    public void TheTableIsCreatedOnAnExistingStoreWithoutChangingTheSchemaVersion()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        using (HistoryStore.Open(StorePath))
        {
        }

        Execute("""
            DROP TABLE task_records;
            INSERT INTO files VALUES (50, '/gone/a.jsonl', 'Claude', 1, 1, 7, x'');
            INSERT INTO contributions VALUES (50, '2026-10-20', 'claudeCode', 5, 10, 0, 0, 30, 0, 30, 1000, 0, 1, 7, 0);
            """);
        var kept = Load()["/gone/a.jsonl"];
        Assert.Empty(kept.TaskRecords);
        Assert.Equal(30, kept.Days.Values.Single().Output);
        Assert.Equal(HistoryStore.SchemaVersion, Scalar("PRAGMA user_version"));
        var sql = Text("SELECT sql FROM sqlite_master WHERE name = 'task_records'");
        Assert.Contains("PRIMARY KEY (file_id, ordinal)) WITHOUT ROWID", sql, StringComparison.Ordinal);
        Assert.Equal(23, Scalar("SELECT count(*) FROM pragma_table_info('task_records')"));
    }

    [Fact]
    public void AnUnchangedFileWithAnIncompleteTaskIsAnalysedAgainOnceQuietForThirtyMinutes()
    {
        var modified = WriteClaude("p/a.jsonl");
        var now = modified + Minute;
        var options = new HistoryOptions { NowMs = () => now };
        using (var store = HistoryStore.Open(StorePath))
        {
            Assert.Equal(1, new HistoryAnalyzer(store, options).Run(Locations, Claude, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        }

        InsertRow(FileId(), complete: false);
        using (var store = HistoryStore.Open(StorePath))
        {
            var analyzer = new HistoryAnalyzer(store, options);
            var entry = Assert.Single(analyzer.Entries.Values);
            Assert.True(entry.HasIncompleteTasks);
            Assert.Equal(entry.Path, Assert.Single(entry.TaskRecords).AgentId);

            now = modified + (29 * Minute) + 59_999;
            Assert.Equal((0, 0), Counts(analyzer.Run(Locations, Claude, TimeZoneInfo.Utc, CancellationToken.None)));
            now = modified + (30 * Minute);
            Assert.Equal((1, 1), Counts(analyzer.Run(Locations, Claude, TimeZoneInfo.Utc, CancellationToken.None)));

            // The new records (none until the composer fills them) replace the incomplete one.
            Assert.False(Assert.Single(analyzer.Entries.Values).HasIncompleteTasks);
            Assert.Equal(0, Scalar("SELECT count(*) FROM task_records"));
            now += 10 * Minute;
            Assert.Equal((0, 0), Counts(analyzer.Run(Locations, Claude, TimeZoneInfo.Utc, CancellationToken.None)));
        }
    }

    [Fact]
    public void AnUnchangedFileWhoseTasksAreCompleteIsNotAnalysedAgain()
    {
        var modified = WriteClaude("p/a.jsonl");
        var now = modified + Minute;
        var options = new HistoryOptions { NowMs = () => now };
        using (var store = HistoryStore.Open(StorePath))
        {
            new HistoryAnalyzer(store, options).Run(Locations, Claude, TimeZoneInfo.Utc, CancellationToken.None);
        }

        InsertRow(FileId(), complete: true);
        using (var store = HistoryStore.Open(StorePath))
        {
            var analyzer = new HistoryAnalyzer(store, options);
            now = modified + (45 * Minute);
            Assert.Equal((0, 0), Counts(analyzer.Run(Locations, Claude, TimeZoneInfo.Utc, CancellationToken.None)));
            Assert.Single(Assert.Single(analyzer.Entries.Values).TaskRecords);
        }
    }

    [Fact]
    public void AnOpenClawDatabaseWithAnIncompleteWindowIsAnalysedAgainOnceQuiet()
    {
        var t = _t - (3 * 3_600_000);
        using var writer = new OpenClawWriter(OpenClawDatabase);
        writer.Window(OpenClawMain, "agent:main:main", t).User(OpenClawMain, "u1", t + 1000).Assistant(OpenClawMain, "a1", t + 1005, t + 3005, 100);
        var stamp = t + Minute;
        foreach (var file in (string[])[OpenClawDatabase, OpenClawDatabase + "-wal"])
        {
            if (File.Exists(file))
            {
                File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddMilliseconds(stamp));
            }
        }

        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], OpenClawRoots: [_temp.Combine("home", ".openclaw")]);
        var enabled = new HashSet<SourceKind> { SourceKind.OpenClaw };
        var now = stamp + Minute;
        var options = new HistoryOptions { NowMs = () => now };
        using (var store = HistoryStore.Open(StorePath))
        {
            Assert.Equal(1, new HistoryAnalyzer(store, options).Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        }

        var window = OpenClawDatabase + "#" + OpenClawMain;
        InsertRow(FileId(window), complete: false, source: "openclaw");
        using (var store = HistoryStore.Open(StorePath))
        {
            var analyzer = new HistoryAnalyzer(store, options);
            Assert.Equal(window, Assert.Single(analyzer.TaskRecords()).AgentId);
            now = stamp + (29 * Minute);
            Assert.Equal((0, 0), Counts(analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None)));
            now = stamp + (30 * Minute);
            Assert.Equal((1, 1), Counts(analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None)));
            Assert.Empty(analyzer.TaskRecords());
            Assert.Equal(100, Assert.Single(analyzer.Days()).Totals.Output);
            now += 10 * Minute;
            Assert.Equal((0, 0), Counts(analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None)));
        }
    }

    [Fact]
    public void EveryCompletedRunCalibratesEachScoredSourceOnItsStoredRecords()
    {
        var codex = CalibrationSet("/logs/codex.jsonl", SourceKind.Codex, 50);
        var claude = CalibrationSet("/logs/claude.jsonl", SourceKind.ClaudeCode, 3);
        var gemini = Entry("/logs/gemini.json", Record("/logs/gemini.json", 0) with { Source = SourceKind.Gemini });
        using var store = HistoryStore.Open(StorePath);
        store.Save([(codex, []), (claude, []), (gemini, [])]);
        var analyzer = new HistoryAnalyzer(store);
        Assert.Equal([SourceKind.Codex, SourceKind.ClaudeCode], analyzer.Calibration.Keys.Order());

        using (var other = HistoryStore.Open(_temp.Combine("empty", "history.sqlite")))
        {
            Assert.Empty(new HistoryAnalyzer(other).Calibration);
        }

        var report = analyzer.Run(Locations, All, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.False(report.Cancelled);
        var records = analyzer.TaskRecords();
        Assert.Equal(54, records.Count);
        Assert.Equal(["/logs/claude.jsonl", "/logs/codex.jsonl", "/logs/gemini.json"], records.Select(r => r.AgentId).Distinct());

        var expected = TaskCalibration.Calibrate(codex.TaskRecords);
        var actual = analyzer.Calibration[SourceKind.Codex];
        Assert.True(actual.Success.Calibrated);
        Assert.True(actual.Stall.Calibrated);
        Assert.False(actual.Fix.Calibrated);
        Assert.Equal(49, actual.Success.Tasks);
        Assert.Equal(expected.Success.Coefficients, actual.Success.Coefficients);
        Assert.Equal(expected.Stall.Coefficients, actual.Stall.Coefficients);
        Assert.NotEqual(TaskModels.Prior.Success.Coefficients, actual.Success.Coefficients);
        Assert.False(analyzer.Calibration[SourceKind.ClaudeCode].Success.Calibrated);
        Assert.Equal(2, analyzer.Calibration[SourceKind.ClaudeCode].Success.Tasks);
        Assert.False(analyzer.Calibration.ContainsKey(SourceKind.Gemini));
    }

    [Fact]
    public async Task TheServicePublishesTheRecordsAndTheCalibrationWithTheDays()
    {
        using (var store = HistoryStore.Open(StorePath))
        {
            store.Save([
                (CalibrationSet("/logs/codex.jsonl", SourceKind.Codex, 50), []),
                (CalibrationSet("/logs/claude.jsonl", SourceKind.ClaudeCode, 3), []),
            ]);
        }

        var hub = await Task.Run(() => new HistoryHub());
        var published = new ConcurrentQueue<(HistoryStateKind Kind, IReadOnlyList<TaskRecord> Records, IReadOnlyDictionary<SourceKind, TaskModels> Calibration)>();
        hub.Changed += (_, _) => published.Enqueue((hub.State.Kind, hub.TaskRecords, hub.TaskCalibration));
        var updates = new ConcurrentQueue<IReadOnlyDictionary<SourceKind, TaskModels>>();
        var configuration = new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Codex }, _temp.Combine("codex", "sessions"),
            _temp.Combine("claude", "projects"), _temp.Combine("dsh", "sessions"), _temp.Combine("opencode"), _temp.Combine("gemini", "tmp"));
        Assert.Empty(hub.TaskRecords);
        Assert.Empty(hub.TaskCalibration);
        using (var service = new HistoryService(hub, configuration, StorePath, TimeZoneInfo.Utc, new HistoryOptions { Workers = 1 }))
        {
            service.CalibrationUpdated = updates.Enqueue;
            service.Start();
            var ready = await WaitForAsync(published, p => p.Kind == HistoryStateKind.Ready);
            Assert.Equal(50, ready.Records.Count);
            Assert.All(ready.Records, r => Assert.Equal(SourceKind.Codex, r.Source));
            Assert.True(ready.Calibration[SourceKind.Codex].Success.Calibrated);
            Assert.True(ready.Calibration.ContainsKey(SourceKind.ClaudeCode));
            Assert.Contains(updates, u => u.TryGetValue(SourceKind.Codex, out var models) && models.Success.Calibrated);

            var comparison = hub.CompareTasks("2026-01-01", "2026-12-31", null);
            var row = Assert.Single(comparison.BySource);
            Assert.Equal((SourceKind.Codex, 49), (row.Source, row.Tasks));
            Assert.Empty(hub.CompareTasks("2026-01-01", "2026-12-31", SourceKind.ClaudeCode).BySource);

            var mark = published.Count;
            hub.Clear();
            var cleared = await WaitForAsync(published, p => p.Kind == HistoryStateKind.Ready, after: mark);
            Assert.Empty(cleared.Records);
            Assert.Empty(cleared.Calibration);
        }

        Assert.Equal(0, Scalar("SELECT count(*) FROM task_records"));
    }

    [Fact]
    public void ADayLabelerGivesRecordsTheirLocalDateOrNone()
    {
        var labeler = new DayLabeler(TimeZoneInfo.Utc);
        Assert.Equal("2026-10-20", labeler.DayOf(ClaudeLines.T0));
        Assert.Equal(string.Empty, labeler.DayOf(long.MaxValue));
        Assert.Equal(string.Empty, labeler.DayOf(-62_135_596_800_000));
    }

    private static (int Planned, int Analyzed) Counts(HistoryReport report) => (report.FilesPlanned, report.FilesAnalyzed);

    private static void AssertSame(TaskRecord expected, TaskRecord actual)
    {
        var none = Array.Empty<double[]>();
        Assert.Equal(expected with { Snapshots = none, FixRows = none }, actual with { Snapshots = none, FixRows = none });
        Assert.Equal(expected.Snapshots.Count, actual.Snapshots.Count);
        Assert.Equal(expected.FixRows.Count, actual.FixRows.Count);
        foreach (var (want, have) in expected.Snapshots.Zip(actual.Snapshots).Concat(expected.FixRows.Zip(actual.FixRows)))
        {
            Assert.Equal(want.Select(BitConverter.DoubleToInt64Bits), have.Select(BitConverter.DoubleToInt64Bits));
        }
    }

    private static async Task<T> WaitForAsync<T>(ConcurrentQueue<T> queue, Func<T, bool> predicate, int after = 0)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (queue.Skip(after).LastOrDefault(predicate) is { } found && predicate(found))
            {
                return found;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("expected history state was not published");
    }

    private static StoredFile Entry(string path, params TaskRecord[] records) => new()
    {
        Path = path,
        Family = "Claude",
        Version = HistoryAnalyzer.AnalysisVersion,
        Days = [],
        TaskRecords = records,
    };

    private TaskRecord Record(string agent, int ordinal) => new()
    {
        AgentId = agent,
        Source = SourceKind.Codex,
        Day = "2026-10-20",
        Ordinal = ordinal,
        StartMs = _t + (ordinal * Minute),
        EndMs = _t + (ordinal * Minute) + 30_000,
        Complete = true,
        Model = "gpt-6.1-sol",
        State = TaskState.Unverified,
        Score = 40,
        Dims = new TaskDims(10, 10, 10, 5, 5),
        Counts = new TaskCounts(3, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    };

    /// <summary>Complete records whose snapshots carry both labels of the success and stall models, plus one incomplete record.</summary>
    private StoredFile CalibrationSet(string path, SourceKind source, int count)
    {
        var records = new TaskRecord[count];
        for (var i = 0; i < count; i++)
        {
            var success = i % 2 == 0;
            var stall = i % 3 == 0;
            records[i] = Record(path, i) with
            {
                Source = source,
                Complete = i < count - 1,
                Success = success,
                Stalled = stall,
                State = success ? TaskState.Passing : TaskState.Failing,
                Snapshots =
                [
                    [1, success ? 1 : 0, success ? 0 : 1, (i % 5) / 5.0, (i % 7) / 7.0, stall ? 0.4 : 0.1, (i % 4) / 8.0, 0.2, stall ? 1 : 0],
                ],
            };
        }

        return Entry(path, records);
    }

    private long WriteClaude(string relative)
    {
        var path = _temp.WriteFile("claude/projects/" + relative,
            ClaudeLines.UserText(_t, "go") + "\n" + ClaudeLines.Assistant(_t + 2000, "msg_a", 100) + "\n");
        var modified = _t + Minute;
        File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddMilliseconds(modified));
        return modified;
    }

    private Dictionary<string, StoredFile> Load()
    {
        using var store = HistoryStore.Open(StorePath);
        return store.LoadFiles(StringComparer.Ordinal);
    }

    private long FileId(string? path = null)
    {
        using var raw = SqliteConnection.Open(StorePath, SqliteOpenMode.ReadOnly);
        if (path is null)
        {
            return raw.ScalarInt64("SELECT id FROM files")!.Value;
        }

        using var statement = raw.Prepare("SELECT id FROM files WHERE path = ?1");
        statement.Bind(1, path);
        Assert.True(statement.Step());
        return statement.GetInt64(0);
    }

    private void InsertRow(long fileId, bool complete, string source = "claudeCode")
    {
        using var raw = SqliteConnection.Open(StorePath, SqliteOpenMode.ReadWriteCreate);
        using var insert = raw.Prepare(
            "INSERT INTO task_records VALUES (?1, 0, ?2, 'claude-opus-5-5', NULL, ?3, ?4, '2026-10-20', ?5, 'unverified', 40.5, " +
            "10, 10, 10, 5, 5.5, ?6, NULL, 0, 0, '[[1,0,0,0.4,1,0,0,0.1,0]]', '[]', ?7)");
        insert.Bind(1, fileId).Bind(2, source).Bind(3, _t).Bind(4, _t + 2000).Bind(5, complete ? 1L : 0L)
            .Bind(6, """{"actions":1,"counted":1,"edits":1,"oscillations":0,"loopActions":0,"runs":0,"failingRuns":0,"episodes":0,"recovered":0,"recoverAttempts":0,"fixAttempts":0,"fixFailures":0}""")
            .Bind(7, HistoryAnalyzer.AnalysisVersion).Run();
    }

    private void Execute(string sql)
    {
        using var raw = SqliteConnection.Open(StorePath, SqliteOpenMode.ReadWriteCreate);
        raw.Execute(sql);
    }

    private long? Scalar(string sql)
    {
        using var raw = SqliteConnection.Open(StorePath, SqliteOpenMode.ReadOnly);
        return raw.ScalarInt64(sql);
    }

    private string? Text(string sql)
    {
        using var raw = SqliteConnection.Open(StorePath, SqliteOpenMode.ReadOnly);
        using var statement = raw.Prepare(sql);
        return statement.Step() ? statement.GetString(0) : null;
    }
}
