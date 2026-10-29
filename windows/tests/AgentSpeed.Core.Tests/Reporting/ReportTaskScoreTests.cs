using System.Text.Json;
using System.Text.Json.Nodes;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>
/// The <c>taskScore</c> extension (METRICS 11.10) against <c>report-vectors.json</c> → <c>taskScore</c>: when it is active,
/// what a completed task adds, the live values and the model list, and the task ledger (watch, counting, builds, breaks).
/// </summary>
public sealed class ReportTaskScoreTests
{
    private static JsonElement Section => ReportSpec.Vector("taskScore");

    public static TheoryData<string> Names(string group)
    {
        var data = new TheoryData<string>();
        foreach (var item in Section.GetProperty(group).EnumerateArray())
        {
            data.Add(item.Text("name"));
        }

        return data;
    }

    public static TheoryData<string> CapabilityNames() => Names("capability");

    public static TheoryData<string> RecordNames() => Names("records");

    public static TheoryData<string> AssemblyNames() => Names("assembly");

    public static TheoryData<string> LedgerNames() => Names("ledger");

    [Fact]
    public void EveryClientCaseIsRun()
    {
        Assert.Equal((10, 6, 3, 8), (Section.GetProperty("capability").GetArrayLength(), Section.GetProperty("records").GetArrayLength(),
            Section.GetProperty("assembly").GetArrayLength(), Section.GetProperty("ledger").GetArrayLength()));
    }

    [Theory]
    [MemberData(nameof(CapabilityNames))]
    public void Capability(string name)
    {
        var item = Case("capability", name);
        var raw = new JsonObject
        {
            ["protocol"] = "agent-speed-report",
            ["versions"] = new JsonArray(1),
            ["service"] = new JsonObject { ["name"] = "x", ["terms"] = "https://example.com/t", ["privacy"] = "https://example.com/p" },
            ["api"] = "https://api.example.com",
        };
        foreach (var (key, value) in JsonNode.Parse(item.GetProperty("document").GetRawText())!.AsObject())
        {
            raw[key] = value?.DeepClone();
        }

        var document = ReportDiscovery.Parse(ReportSpec.Utf8(raw.ToJsonString()), ReportServiceUrl.Parse("https://example.com")!, out var problem);
        Assert.Null(problem);
        var switches = ReportSections.All.Where(s => s != ReportSection.Models || item.GetProperty("modelsSwitch").GetBoolean());
        var options = new ReportOptions(switches.Where(document!.Sections.Contains).ToHashSet(), document.Limits.MaxModels)
        {
            Extensions = document.Extensions,
        };
        Assert.Equal(item.GetProperty("expect").GetBoolean(), options.TaskScoreActive);
        Assert.Equal(document.Extensions, ReportDiscovery.Parse(document.ToJson().ToCompactBytes(), ReportServiceUrl.Parse("https://example.com")!, out _)!.Extensions);
    }

    [Theory]
    [MemberData(nameof(RecordNames))]
    public void Records(string name)
    {
        var item = Case("records", name);
        var record = item.GetProperty("record");
        var row = ReportTaskRow.Of(Record(record));
        var expect = item.GetProperty("expect");
        if (expect.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(row);
            return;
        }

        Assert.NotNull(row);
        Assert.Equal(Canonical(expect), Canonical(row.Json(Source(record.Text("source")))));
        Assert.Equal(ParseRow(expect), row);
    }

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void Assembly(string name)
    {
        var item = Case("assembly", name);
        var input = item.GetProperty("input");
        var counters = new ReportCounters();
        var generating = new Dictionary<ReportModelKey, int>();
        foreach (var model in input.GetProperty("models").EnumerateArray())
        {
            var key = new ReportModelKey(model.Text("id"), Source(model.GetProperty("sources")[0].GetString()!));
            counters.Add(new ReportUsage(model.GetProperty("requests").GetInt64(), model.GetProperty("output").GetInt64(),
                MeasuredOutput: model.GetProperty("measuredOutput").GetInt64(), MeasuredMs: model.GetProperty("measuredMs").GetInt64()), key);
            if (model.GetProperty("generating").GetInt32() is var count and > 0)
            {
                generating[key] = count;
            }
        }

        var agents = input.GetProperty("agents").EnumerateArray()
            .Where(a => a.Text("form") != "none")
            .Select(a => new ReportTaskAgent(Source(a.Text("source")), a.Text("model"), Form(a.Text("form")), a.GetProperty("score").GetDouble()))
            .ToList();
        var thinking = input.GetProperty("thinking");
        var modelThinking = thinking.GetProperty("models").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.Ordinal);
        var rows = new Dictionary<ReportModelKey, ReportTaskRow>();
        if (input.TryGetProperty("ledger", out var ledger))
        {
            foreach (var model in ledger.EnumerateObject())
            {
                foreach (var row in model.Value.EnumerateArray())
                {
                    rows[new ReportModelKey(model.Name, Source(row.Text("source")))] = ParseRow(row);
                }
            }
        }

        var facts = new ReportLiveFacts(new Dictionary<SourceKind, ReportAgentCounts>(), generating, counters.Copy(), 1)
        {
            Tasks = new ReportTaskFacts(agents, thinking.GetProperty("total").GetDouble(), modelThinking),
        };
        var sample = ReportSample.Empty with { Facts = facts };
        var content = new ReportWindow(ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Epoch = 1 } }).Content(sample) with { Tasks = rows };
        var options = new ReportOptions(new HashSet<ReportSection> { ReportSection.Models }, input.GetProperty("maxModels").GetInt32())
        {
            Extensions = new ReportExtensions(TaskScore: true),
        };
        var body = JsonDocument.Parse(ReportBody.Json(Envelope, content, sample, options).ToCompactBytes()).RootElement;
        var expect = item.GetProperty("expect");
        Assert.Equal(Canonical(expect.GetProperty("taskScore")), Canonical(body.GetProperty("taskScore")));
        Assert.Equal(Canonical(expect.GetProperty("models")), Canonical(body.GetProperty("models")));
    }

    [Theory]
    [MemberData(nameof(LedgerNames))]
    public void Ledger(string name)
    {
        var item = Case("ledger", name);
        var driver = new LedgerDriver();
        foreach (var e in item.GetProperty("events").EnumerateArray())
        {
            driver.Apply(e);
        }

        var expect = item.GetProperty("expect");
        Assert.Equal(expect.GetProperty("builds").GetArrayLength(), driver.Builds.Count);
        var index = 0;
        foreach (var build in expect.GetProperty("builds").EnumerateArray())
        {
            var (seq, stats) = driver.Builds[index++];
            Assert.Equal(build.GetProperty("seq").GetInt32(), seq);
            var want = build.GetProperty("taskStats");
            if (want.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(stats);
            }
            else
            {
                Assert.NotNull(stats);
                Assert.Equal(Canonical(want), Canonical(stats));
            }
        }

        Assert.Equal(expect.GetProperty("watched").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal),
            driver.Watched.Order(StringComparer.Ordinal));
        Assert.Equal(Canonical(expect.GetProperty("pending")), Canonical(driver.Pending()));
    }

    /// <summary>
    /// The task ledger as the app runs it: the composers' marks (here a dictionary), the engine's counting into
    /// <see cref="ReportCounters"/> and the report side's <see cref="ReportTaskWindow"/>.
    /// </summary>
    private sealed class LedgerDriver
    {
        private readonly Dictionary<string, long> _marks = new(StringComparer.Ordinal);
        private ReportLedger _engine = new();
        private ReportTaskWindow _window = new();
        private long _epoch = 1;

        public List<(int Seq, JsonObject? Stats)> Builds { get; } = [];

        public IEnumerable<string> Watched => _marks.Keys;

        public void Apply(JsonElement e)
        {
            var at = e.GetProperty("at").GetInt64();
            if (e.TryGetProperty("watch", out var watch))
            {
                _window.Update(watch.GetBoolean(), "https://service.example");

                // The monitor's next tick unwatches the tasks of another generation (TaskWatchContext.Tick).
                foreach (var (id, generation) in _marks.ToArray())
                {
                    if (!_window.Watch.Counts(generation))
                    {
                        _marks.Remove(id);
                    }
                }
            }
            else if (e.TryGetProperty("start", out var start))
            {
                foreach (var id in Ids(start.GetString()!, e))
                {
                    var seeding = e.TryGetProperty("seeding", out var s) && s.GetBoolean();
                    var generation = seeding ? 0 : _window.Watch.Mark(e.GetProperty("t").GetInt64(), at);
                    if (generation != 0)
                    {
                        _marks[id] = generation;
                    }
                }
            }
            else if (e.TryGetProperty("withdraw", out var withdrawn) || e.TryGetProperty("drop", out withdrawn))
            {
                _marks.Remove(withdrawn.GetString()!);
            }
            else if (e.TryGetProperty("complete", out var complete))
            {
                var record = e.GetProperty("record");
                foreach (var id in Ids(complete.GetString()!, e))
                {
                    if (_marks.Remove(id, out var generation))
                    {
                        var key = new ReportModelKey(record.Text("model"), Source(record.Text("source")));
                        _engine.CountTask(key, ReportTaskRow.Of(Record(record)), generation, _window.Watch, _epoch);
                    }
                }
            }
            else if (e.TryGetProperty("build", out var build))
            {
                var rows = _window.Take(Sample());
                var models = !e.TryGetProperty("models", out var m) || m.GetBoolean();
                Builds.Add((build.GetInt32(), models ? Group(rows) : null));
            }
            else if (e.TryGetProperty("relaunch", out _))
            {
                _engine = new ReportLedger();
                _window = new ReportTaskWindow();
                _marks.Clear();
                _epoch++;
            }

            // "discard", "newRun" and a lost build leave the task ledger alone, and so does the driver.
        }

        public JsonObject Pending() => Group(_window.Take(Sample()));

        private ReportSample Sample()
        {
            _engine.FollowTaskWatch(_window.Watch);
            return ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Counters = _engine.Counters.Copy(), Epoch = _epoch } };
        }

        private static IEnumerable<string> Ids(string id, JsonElement e) =>
            e.TryGetProperty("count", out var count) ? Enumerable.Range(0, count.GetInt32()).Select(i => $"{id}-{i}") : [id];

        private static JsonObject Group(IReadOnlyDictionary<ReportModelKey, ReportTaskRow> rows)
        {
            var output = new JsonObject();
            foreach (var model in rows.GroupBy(p => p.Key.ModelId).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var list = new JsonArray();
                foreach (var (key, row) in model.OrderBy(p => p.Key.Source))
                {
                    list.Add(JsonNode.Parse(row.Json(key.Source).ToCompactBytes()));
                }

                output[model.Key] = list;
            }

            return output;
        }
    }

    [Fact]
    public void AModelsDropForSizeKeepsTheTopLevelObject()
    {
        var counters = new ReportCounters();
        for (var i = 0; i < 16; i++)
        {
            counters.Add(new ReportUsage(1, 100 + i), new ReportModelKey($"model-{i:D2}-{new string('x', 50)}", SourceKind.Codex));
        }

        var facts = new ReportLiveFacts(new Dictionary<SourceKind, ReportAgentCounts>(), new Dictionary<ReportModelKey, int>(), counters.Copy(), 1)
        {
            Tasks = new ReportTaskFacts([new ReportTaskAgent(SourceKind.Codex, "model-00", TaskValueForm.Live, 71.5)], 12.34,
                new Dictionary<string, double>(StringComparer.Ordinal)),
        };
        var sample = ReportSample.Empty with { Facts = facts };
        var content = new ReportWindow(ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Epoch = 1 } }).Content(sample);
        var options = new ReportOptions(new HashSet<ReportSection> { ReportSection.Models, ReportSection.Sources }, 16, BodyLimit: 1200)
        {
            Extensions = new ReportExtensions(ModelHarnessRequests: true, ExecutionModes: true, TaskScore: true),
        };
        var (json, data) = ReportBody.Fitted(Envelope, content, sample, options);
        var body = JsonDocument.Parse(data).RootElement;
        Assert.False(body.TryGetProperty("models", out _));
        Assert.Equal("""{"score":72,"form":"live","thinkingTokS":12.3}""", body.GetProperty("taskScore").GetRawText());
        Assert.NotNull(json);

        var inactive = options with { Extensions = new ReportExtensions(), BodyLimit = 16_384 };
        Assert.False(JsonDocument.Parse(ReportBody.Data(Envelope, content, sample, inactive)).RootElement.TryGetProperty("taskScore", out _));
        var switchedOff = options with { Sections = new HashSet<ReportSection> { ReportSection.Sources }, BodyLimit = 16_384 };
        Assert.False(JsonDocument.Parse(ReportBody.Data(Envelope, content, sample, switchedOff)).RootElement.TryGetProperty("taskScore", out _));
    }

    [Fact]
    public void ABreakDropsPendingRowsAndARunChangeDoesNot()
    {
        var engine = new ReportLedger();
        var window = new ReportTaskWindow();
        Assert.True(window.Update(true, "a"));
        var key = new ReportModelKey("gpt-5.5", SourceKind.Codex);
        var row = ReportTaskRow.Of(new ReportTaskRecord(3, 80, true, false, 0, 0, 0, 0))!;
        ReportSample Sample() => ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Counters = engine.Counters.Copy(), Epoch = 7 } };

        Assert.True(engine.CountTask(key, row, window.Watch.Generation, window.Watch, 7));
        Assert.Single(window.Content(Sample()));
        Assert.False(window.Update(true, "a"));
        Assert.Single(window.Take(Sample()));
        Assert.Empty(window.Take(Sample()));
        Assert.Equal(1, window.Watch.Taken);

        Assert.True(engine.CountTask(key, row, window.Watch.Generation, window.Watch, 7));
        var marked = window.Watch.Generation;
        Assert.True(window.Update(true, "b"));
        Assert.Empty(window.Content(Sample()));
        Assert.False(engine.CountTask(key, row, marked, window.Watch, 7));
        Assert.Empty(window.Take(Sample()));
        Assert.True(engine.CountTask(key, row, window.Watch.Generation, window.Watch, 7));
        Assert.True(window.Update(false, null));
        Assert.Empty(window.Content(Sample()));
        Assert.False(engine.CountTask(key, row, window.Watch.Generation, window.Watch, 7));
    }

    [Fact]
    public void ACompletionIsAChangeButNoActivity()
    {
        var engine = new ReportLedger();
        var window = new ReportTaskWindow();
        window.Update(true, "a");
        ReportSample Sample() => ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Counters = engine.Counters.Copy(), Epoch = 3 } };
        var report = new ReportWindow(Sample());
        engine.CountTask(new ReportModelKey("m", SourceKind.Codex), ReportTaskRow.Of(new ReportTaskRecord(1, null, false, true, 0, 0, 0, 0)),
            window.Watch.Generation, window.Watch, 3);
        var sample = Sample();
        Assert.True(report.HasIncrement(sample, traffic: false));
        Assert.False(report.HasTokenIncrement(sample));
        report.Observe(sample);
        Assert.False(report.HasIncrement(sample, traffic: false));
    }

    [Fact]
    public void StreamValuesChangeWithARoundedScore()
    {
        ReportSample With(double score, double thinking) => ReportSample.Empty with
        {
            Facts = ReportLiveFacts.Empty with
            {
                Tasks = new ReportTaskFacts([new ReportTaskAgent(SourceKind.ClaudeCode, "claude-opus-5-5", TaskValueForm.Live, score)], thinking,
                    new Dictionary<string, double>(StringComparer.Ordinal) { ["claude-opus-5-5"] = thinking }),
            },
        };

        Assert.Equal(ReportStreamValues.Of(With(70.2, 1.01), true), ReportStreamValues.Of(With(69.6, 1.04), true));
        Assert.NotEqual(ReportStreamValues.Of(With(70.2, 1.01), true), ReportStreamValues.Of(With(70.6, 1.01), true));
        Assert.NotEqual(ReportStreamValues.Of(With(70.2, 1.01), true), ReportStreamValues.Of(With(70.2, 1.06), true));
        Assert.Equal(ReportStreamValues.Of(With(70.2, 1.01)), ReportStreamValues.Of(With(90, 9)));
    }

    private static readonly ReportEnvelope Envelope = new(new ReportClientInfo("1.3.0", "Windows", 11), "6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b",
        "tester", "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", 1, 1_792_656_930_500, 5000, ReportActivity.Active, 0);

    private static JsonElement Case(string group, string name) =>
        Section.GetProperty(group).EnumerateArray().Single(c => c.Text("name") == name);

    internal static SourceKind Source(string id) => Enum.GetValues<SourceKind>().Single(k => ReportBody.SourceId(k) == id);

    private static TaskValueForm Form(string form) => form switch
    {
        "live" => TaskValueForm.Live,
        "resting" => TaskValueForm.Resting,
        _ => TaskValueForm.None,
    };

    private static ReportTaskRecord Record(JsonElement record)
    {
        var counts = record.GetProperty("counts");
        var score = record.GetProperty("score");
        return new ReportTaskRecord(record.GetProperty("actions").GetInt32(), score.ValueKind == JsonValueKind.Number ? score.GetDouble() : null,
            record.GetProperty("success").GetBoolean(), record.GetProperty("stalled").GetBoolean(), counts.GetProperty("fixAttempts").GetInt32(),
            counts.GetProperty("fixFailures").GetInt32(), counts.GetProperty("recovered").GetInt32(), counts.GetProperty("recoverAttempts").GetInt32());
    }

    internal static ReportTaskRow ParseRow(JsonElement row) => new(
        row.GetProperty("completed").GetInt64(), row.GetProperty("scored").GetInt64(), row.GetProperty("scoreSum").GetInt64(),
        row.GetProperty("scoreBins").EnumerateArray().Select(b => b.GetInt64()).ToArray(), row.GetProperty("successes").GetInt64(),
        row.GetProperty("stalls").GetInt64(), row.GetProperty("fixAttempts").GetInt64(), row.GetProperty("fixFailures").GetInt64(),
        row.GetProperty("recovered").GetInt64(), row.GetProperty("recoverAttempts").GetInt64());

    /// <summary>Compact JSON with numbers by value (<c>0.0</c> and <c>0</c> agree), members in their order.</summary>
    private static string Canonical(JsonElement element) => Normalize(JsonNode.Parse(element.GetRawText()))?.ToJsonString() ?? "null";

    private static string Canonical(ReportJson json) => Canonical(JsonDocument.Parse(json.ToCompactBytes()).RootElement);

    private static string Canonical(JsonNode node) => Canonical(JsonDocument.Parse(node.ToJsonString()).RootElement);

    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, Normalize(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Normalize).ToArray()),
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => JsonValue.Create(v.GetValue<double>()),
        _ => node?.DeepClone(),
    };
}
