using System.Buffers;
using System.Globalization;
using System.Text.Json;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The task score end to end (METRICS 6.9 <i>Composing the stream</i>, <i>Values</i>; 6.11) against
/// <c>spec/fixtures/task-e2e-expected.json</c>: the fixture's logs read in its order with one dedupe set, then every
/// agent's events and stitched sessions, the records, the calibration, the values at <c>nowMs</c>, the sets and groups and
/// the comparison. Numbers within 1e-9, calibration within 1e-6.
/// </summary>
public sealed class TaskEndToEndTests
{
    private static readonly Lazy<JsonElement> Expected = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("task-e2e-expected.json"))).RootElement);

    private static readonly Lazy<Run> Result = new(Compute);

    public static TheoryData<string> AgentIds()
    {
        var data = new TheoryData<string>();
        foreach (var agent in Expected.Value.GetProperty("agents").EnumerateObject())
        {
            data.Add(agent.Name);
        }

        return data;
    }

    [Fact]
    public void ReadsEveryAgent() =>
        Assert.Equal(Expected.Value.GetProperty("agents").EnumerateObject().Select(p => p.Name), Result.Value.Agents.Keys);

    [Theory]
    [MemberData(nameof(AgentIds))]
    public void AgentStreamMatches(string id)
    {
        var want = Expected.Value.GetProperty("agents").GetProperty(id);
        Assert.True(Result.Value.Agents.TryGetValue(id, out var got), id + " not read");
        AssertClose(want, got, 1e-9, id);
    }

    [Fact]
    public void RecordsMatch() => AssertClose(Expected.Value.GetProperty("records"), Result.Value.Records, 1e-9, "records");

    [Fact]
    public void CalibrationMatches() => AssertClose(Expected.Value.GetProperty("calibration"), Result.Value.Calibration, 1e-6, "calibration");

    [Fact]
    public void ValuesMatch()
    {
        var want = Expected.Value.GetProperty("values").EnumerateArray().ToDictionary(v => v.GetProperty("agentId").GetString()!);
        var got = Result.Value.Values.EnumerateArray().ToDictionary(v => v.GetProperty("agentId").GetString()!);
        Assert.Equal(want.Keys.Order(StringComparer.Ordinal), got.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, value) in want)
        {
            AssertClose(value, got[id], 1e-9, "value " + id);
        }
    }

    [Fact]
    public void SetsAndGroupsMatch() => AssertClose(Expected.Value.GetProperty("taskSets"), Result.Value.Sets, 1e-9, "taskSets");

    [Fact]
    public void ComparisonMatches() => AssertClose(Expected.Value.GetProperty("comparison"), Result.Value.Comparison, 1e-9, "comparison");

    private static Run Compute()
    {
        var root = FixtureLocator.FixturesDirectory;
        var expected = Expected.Value;
        var roots = expected.GetProperty("roots");
        var logs = expected.GetProperty("order").EnumerateArray().Select(item =>
        {
            var source = SourceNames.Parse(item.GetProperty("source").GetString())!.Value;
            var name = item.GetProperty("source").GetString()!;
            var rootPath = roots.TryGetProperty(name, out var r) ? FixtureLocator.PathOf(r.GetString()!) : string.Empty;
            return new TaskLog(FixtureLocator.PathOf(item.GetProperty("path").GetString()!), source, rootPath);
        }).ToList();
        var agents = TaskAgentsReader.Read(logs, new Deduper());
        string Label(string path)
        {
            var hash = path.IndexOf('#', StringComparison.Ordinal);
            var file = hash < 0 ? path : path[..hash];
            var window = hash < 0 ? string.Empty : path[hash..];
            return Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + window;
        }

        var nowMs = expected.GetProperty("nowMs").GetInt64();
        var analysisMs = expected.GetProperty("analysisMs").GetInt64();
        var labeler = new DayLabeler(TimeZoneInfo.Utc);

        var streams = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            streams[Label(agent.AgentId)] = Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("source", SourceNames.Of(agent.Source));
                w.WriteNumber("stitched", agent.Tasks!.Stats.Stitched);
                w.WriteStartArray("events");
                foreach (var e in agent.Tasks.Events)
                {
                    TaskDumpJson.WriteEvent(w, e);
                }

                w.WriteEndArray();
                w.WriteEndObject();
            });
        }

        var records = agents.SelectMany(a => TaskAgentRecords.Of(a, analysisMs, labeler.DayOf))
            .OrderBy(r => r.AgentId, StringComparer.Ordinal).ThenBy(r => r.Ordinal).ToList();
        var calibration = new List<(SourceKind Source, TaskModels Models)>();
        foreach (var source in TaskSetView.ScoredSources)
        {
            var part = records.Where(r => r.Source == source).ToList();
            if (part.Count > 0)
            {
                calibration.Add((source, TaskCalibration.Calibrate(part)));
            }
        }

        var parents = TaskAgentsReader.Parents(agents);
        var rows = agents.Select(a => TaskAgentRow.Of(
            new TaskAgentView(a.AgentId, a.Source, TaskAgentRow.EffectivePhase(a.Phase, a.LastLineMs, nowMs), a.LastLineMs,
                parents.GetValueOrDefault(a.AgentId), a.Tasks!.Events),
            nowMs,
            calibration.FirstOrDefault(c => c.Source == a.Source).Models ?? TaskModels.Prior)).ToList();
        var sets = expected.GetProperty("sets").EnumerateArray()
            .Select(s => s.ValueKind == JsonValueKind.Null ? null
                : (IReadOnlyList<SourceKind>)s.EnumerateArray().Select(x => SourceNames.Parse(x.GetString())!.Value).ToList())
            .ToList();
        var range = expected.GetProperty("comparisonRange");
        var comparison = TaskComparison.Compare(records, range.GetProperty("first").GetString()!, range.GetProperty("last").GetString()!);

        return new Run(
            streams,
            Json(w =>
            {
                w.WriteStartArray();
                foreach (var record in records)
                {
                    TaskDumpJson.WriteRecord(w, record, Label);
                }

                w.WriteEndArray();
            }),
            Json(w =>
            {
                w.WriteStartObject();
                foreach (var (source, models) in calibration)
                {
                    w.WritePropertyName(SourceNames.Of(source));
                    TaskDumpJson.WriteModels(w, models);
                }

                w.WriteEndObject();
            }),
            Json(w =>
            {
                w.WriteStartArray();
                foreach (var row in rows)
                {
                    TaskDumpJson.WriteAgentRow(w, row, Label);
                }

                w.WriteEndArray();
            }),
            Json(w => TaskDumpJson.WriteSets(w, TaskSetView.Of(rows, nowMs, sets), Label)),
            Json(w =>
            {
                w.WriteStartObject();
                TaskDumpJson.WriteComparison(w, comparison);
                w.WriteEndObject();
            }));
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

    internal static void AssertClose(JsonElement want, JsonElement got, double tolerance, string label)
    {
        var diffs = new List<string>();
        Diff(want, got, tolerance, label, diffs);
        Assert.True(diffs.Count == 0, string.Join("\n", diffs.Take(30)));
    }

    private static void Diff(JsonElement want, JsonElement got, double tolerance, string path, List<string> diffs)
    {
        if (want.ValueKind == JsonValueKind.Number && got.ValueKind == JsonValueKind.Number)
        {
            var a = want.GetDouble();
            var b = got.GetDouble();
            if (Math.Abs(a - b) > tolerance)
            {
                diffs.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: fixture {a:R}, port {b:R}"));
            }

            return;
        }

        if (want.ValueKind != got.ValueKind)
        {
            diffs.Add($"{path}: fixture {want.GetRawText()}, port {got.GetRawText()}");
            return;
        }

        switch (want.ValueKind)
        {
            case JsonValueKind.Object:
                var wantKeys = want.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var gotKeys = got.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var key in wantKeys.Union(gotKeys).Order(StringComparer.Ordinal))
                {
                    if (!wantKeys.Contains(key) || !gotKeys.Contains(key))
                    {
                        diffs.Add($"{path}.{key}: present on one side only");
                    }
                    else
                    {
                        Diff(want.GetProperty(key), got.GetProperty(key), tolerance, path + "." + key, diffs);
                    }
                }

                break;
            case JsonValueKind.Array:
                var wa = want.EnumerateArray().ToList();
                var ga = got.EnumerateArray().ToList();
                if (wa.Count != ga.Count)
                {
                    diffs.Add($"{path}: fixture {wa.Count} items, port {ga.Count}");
                    return;
                }

                for (var i = 0; i < wa.Count; i++)
                {
                    Diff(wa[i], ga[i], tolerance, $"{path}[{i}]", diffs);
                }

                break;
            case JsonValueKind.String:
                if (want.GetString() != got.GetString())
                {
                    diffs.Add($"{path}: fixture {want.GetRawText()}, port {got.GetRawText()}");
                }

                break;
        }
    }

    private sealed record Run(
        SortedDictionary<string, JsonElement> Agents,
        JsonElement Records,
        JsonElement Calibration,
        JsonElement Values,
        JsonElement Sets,
        JsonElement Comparison);
}
