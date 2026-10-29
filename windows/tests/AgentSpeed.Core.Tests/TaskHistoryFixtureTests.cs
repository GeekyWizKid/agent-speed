using System.Buffers;
using System.Text.Json;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The history analysis (METRICS 8, 6.11) over the end-to-end fixture tree: the stored task records, the calibration it
/// computes and the comparison over them equal <c>spec/fixtures/task-e2e-expected.json</c>. The tree is copied with its
/// files' times in the fixture's <c>order</c>, so the startup order (and with it every duplicate) is the fixture's.
/// </summary>
public sealed class TaskHistoryFixtureTests : IDisposable
{
    private static readonly Lazy<JsonElement> Expected = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("task-e2e-expected.json"))).RootElement);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TheHistoryStoresTheFixturesRecordsAndCalibratesOnThem()
    {
        var expected = Expected.Value;
        var root = _temp.Combine("fixtures");
        CopyTree(FixtureLocator.PathOf("tasks"), Path.Combine(root, "tasks"));
        StampInOrder(root, expected.GetProperty("order"));
        var tasks = Path.Combine(root, "tasks");
        var locations = new SourceLocations(
            Path.Combine(tasks, "codex", "sessions"), Path.Combine(tasks, "claude", "projects"), _temp.Combine("none", "dsh"), string.Empty, [],
            OmpRoots: [Path.Combine(tasks, "omp", "agent", "sessions")],
            PiRoots: [Path.Combine(tasks, "pi", "agent", "sessions")],
            OpenClawRoots: [Path.Combine(tasks, "openclaw", ".openclaw")],
            CodexAutomatic: false,
            DroidRoot: Path.Combine(tasks, "droid", ".factory", "sessions"));
        var enabled = new HashSet<SourceKind>(TaskSetView.ScoredSources);
        var analysisMs = expected.GetProperty("analysisMs").GetInt64();
        using var store = HistoryStore.Open(_temp.Combine("store", "history.sqlite"));
        var analyzer = new HistoryAnalyzer(store, new HistoryOptions { NowMs = () => analysisMs });
        analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None);

        string Label(string path)
        {
            var hash = path.IndexOf('#', StringComparison.Ordinal);
            var file = hash < 0 ? path : path[..hash];
            return Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + (hash < 0 ? string.Empty : path[hash..]);
        }

        var records = analyzer.TaskRecords().OrderBy(r => Label(r.AgentId), StringComparer.Ordinal).ThenBy(r => r.Ordinal).ToList();
        TaskEndToEndTests.AssertClose(expected.GetProperty("records"), Json(w =>
        {
            w.WriteStartArray();
            foreach (var record in records)
            {
                TaskDumpJson.WriteRecord(w, record, Label);
            }

            w.WriteEndArray();
        }), 1e-9, "history records");

        TaskEndToEndTests.AssertClose(expected.GetProperty("calibration"), Json(w =>
        {
            w.WriteStartObject();
            foreach (var source in TaskSetView.ScoredSources)
            {
                if (analyzer.Calibration.TryGetValue(source, out var models))
                {
                    w.WritePropertyName(SourceNames.Of(source));
                    TaskDumpJson.WriteModels(w, models);
                }
            }

            w.WriteEndObject();
        }), 1e-6, "history calibration");

        var range = expected.GetProperty("comparisonRange");
        var comparison = TaskComparison.Compare(records, range.GetProperty("first").GetString()!, range.GetProperty("last").GetString()!);
        TaskEndToEndTests.AssertClose(expected.GetProperty("comparison"), Json(w =>
        {
            w.WriteStartObject();
            TaskDumpJson.WriteComparison(w, comparison);
            w.WriteEndObject();
        }), 1e-9, "history comparison");

        // A second run finds every file unchanged and complete: nothing is analysed again.
        Assert.Equal(0, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal(records.Count, analyzer.TaskRecords().Count);
    }

    /// <summary>Every file old, then the fixture's logs one minute apart in its order (a database with its <c>-wal</c>).</summary>
    private static void StampInOrder(string root, JsonElement order)
    {
        var start = DateTime.UtcNow.AddHours(-2);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, start);
        }

        var minute = 1;
        foreach (var item in order.EnumerateArray())
        {
            var path = Path.Combine(root, item.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            var time = start.AddMinutes(minute++);
            File.SetLastWriteTimeUtc(path, time);
            foreach (var sibling in new[] { path + "-wal", Path.ChangeExtension(path, ".settings.json") })
            {
                if (File.Exists(sibling))
                {
                    File.SetLastWriteTimeUtc(sibling, time);
                }
            }
        }
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(from, to, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to, StringComparison.Ordinal));
        }
    }

    private static JsonElement Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return JsonDocument.Parse(buffer.WrittenMemory).RootElement.Clone();
    }
}
