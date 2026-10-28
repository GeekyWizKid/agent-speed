using System.Text.Json;
using System.Text.Json.Nodes;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary><c>predictions</c>, <c>records</c>, <c>calibration</c> (with the stationarity check) and <c>comparison</c> (METRICS 6.11).</summary>
public sealed partial class TaskScoreCaseTests
{
    private static readonly string[] ModelNames = ["success", "stall", "fix"];

    public static TheoryData<int, string> PredictionCases() => Indices("predictions");

    public static TheoryData<int, string> RecordCases() => Indices("records");

    public static TheoryData<int, string> CalibrationCases() => Indices("calibration");

    public static TheoryData<int, string> ComparisonCases() => Indices("comparison");

    [Theory]
    [MemberData(nameof(PredictionCases))]
    public void PredictionsMatch(int index, string name)
    {
        var item = Case("predictions", index);
        var models = Member(item, "models") is { ValueKind: JsonValueKind.Object } given ? Models(given) : TaskModels.Prior;
        var found = TaskPredictions.Of(Events(item.GetProperty("events")), OptionalLong(Member(item, "nowMs")), models);
        JsonObject? actual = null;
        if (found is not null)
        {
            var f = found.Features;
            actual = new JsonObject
            {
                ["success"] = found.Success,
                ["stall"] = found.Stall,
                ["fix"] = Number(found.Fix),
                ["features"] = new JsonObject
                {
                    ["passing"] = f.Passing,
                    ["failing"] = f.Failing,
                    ["conv"] = f.Conv,
                    ["ig"] = f.Ig,
                    ["loop"] = f.Loop,
                    ["osc"] = f.Osc,
                    ["size"] = f.Size,
                },
                ["fixFeatures"] = found.FixFeatures is { } x
                    ? new JsonObject { ["attempts"] = x.Attempts, ["errors"] = x.Errors, ["trend"] = x.Trend, ["osc"] = x.Osc, ["ig"] = x.Ig }
                    : null,
                ["successShown"] = found.SuccessShown,
                ["stallShown"] = found.StallShown,
                ["fixShown"] = Number(found.FixShown),
            };
        }

        AssertClose(item.GetProperty("expect"), actual, name);
    }

    [Theory]
    [MemberData(nameof(RecordCases))]
    public void RecordsMatch(int index, string name)
    {
        var item = Case("records", index);
        var notes = new List<TaskModelNote>();
        if (Member(item, "models") is { ValueKind: JsonValueKind.Array } given)
        {
            notes.AddRange(given.EnumerateArray().Select(n => new TaskModelNote(n[0].GetInt64(), OptionalString(n[1]), OptionalString(n[2]))));
        }

        var records = TaskRecords.Of(Events(item.GetProperty("events")), item.GetProperty("analysisMs").GetInt64(),
            item.GetProperty("lastLineMs").GetInt64(), notes);
        var actual = new JsonArray([.. records.Select(r => (JsonNode)new JsonObject
        {
            ["ordinal"] = r.Ordinal,
            ["startMs"] = r.StartMs,
            ["endMs"] = r.EndMs,
            ["complete"] = r.Complete,
            ["model"] = Text(r.Model),
            ["effort"] = Text(r.Effort),
            ["state"] = TaskStateNames.Name(r.State),
            ["score"] = Number(r.Score),
            ["dims"] = Dims(r.Dims),
            ["counts"] = Counts(r.Counts),
            ["firstPassMs"] = Number(r.FirstPassMs),
            ["success"] = r.Success,
            ["stalled"] = r.Stalled,
            ["snapshots"] = Rows(r.Snapshots),
            ["fixRows"] = Rows(r.FixRows),
        })]);
        AssertClose(item.GetProperty("expect"), actual, name);
    }

    [Theory]
    [MemberData(nameof(CalibrationCases))]
    public void CalibrationMatches(int index, string name)
    {
        var item = Case("calibration", index);
        var actual = new JsonObject();
        foreach (var source in item.GetProperty("records").EnumerateObject())
        {
            var models = TaskCalibration.Calibrate(HistoryRecords(source.Value));
            var modelsNode = new JsonObject();
            foreach (var model in ModelNames)
            {
                var m = Pick(models, model);
                modelsNode[model] = new JsonObject
                {
                    ["coefficients"] = new JsonArray([.. m.Coefficients.Select(c => (JsonNode)c)]),
                    ["calibrated"] = m.Calibrated,
                    ["tasks"] = m.Tasks,
                };
            }

            var probes = new JsonArray();
            if (Member(item, "probes") is { ValueKind: JsonValueKind.Array } given)
            {
                foreach (var probe in given.EnumerateArray())
                {
                    var model = probe.GetProperty("model").GetString()!;
                    var p = Pick(models, model).Probability(ProbeFeatures(model, probe.GetProperty("features")));
                    probes.Add(new JsonObject { ["model"] = model, ["p"] = p, ["shown"] = TaskRounding.HalfAwayFromZero(p * 100) });
                }
            }

            actual[source.Name] = new JsonObject { ["models"] = modelsNode, ["probes"] = probes };
        }

        AssertClose(item.GetProperty("expect"), actual, name);
    }

    /// <summary><c>stationarity</c>: the fixture's calibrated coefficients make the gradient vanish below 10⁻⁹.</summary>
    [Theory]
    [MemberData(nameof(CalibrationCases))]
    public void CalibratedCoefficientsAreStationary(int index, string name)
    {
        var item = Case("calibration", index);
        var checkedModels = 0;
        foreach (var source in item.GetProperty("records").EnumerateObject())
        {
            var records = HistoryRecords(source.Value);
            foreach (var model in item.GetProperty("expect").GetProperty(source.Name).GetProperty("models").EnumerateObject())
            {
                if (!model.Value.GetProperty("calibrated").GetBoolean())
                {
                    continue;
                }

                var coefficients = model.Value.GetProperty("coefficients").EnumerateArray().Select(c => c.GetDouble()).ToArray();
                var gradient = TaskCalibration.Gradient(records, ModelKindOf(model.Name), coefficients);
                var worst = gradient.Max(Math.Abs);
                Assert.True(worst <= 1e-9, name + " " + source.Name + " " + model.Name + ": gradient " + worst);
                checkedModels++;
            }
        }

        Assert.True(checkedModels > 0, name + " has no calibrated model");
    }

    [Theory]
    [MemberData(nameof(ComparisonCases))]
    public void ComparisonMatches(int index, string name)
    {
        var item = Case("comparison", index);
        var records = item.GetProperty("records").EnumerateArray().Select(r => new TaskRecord
        {
            Source = Source(r.GetProperty("source")),
            Model = OptionalString(Member(r, "model")),
            Day = OptionalString(Member(r, "day")) ?? "",
            Complete = Member(r, "complete").ValueKind == JsonValueKind.True,
            Score = Member(r, "score") is { ValueKind: JsonValueKind.Number } score ? score.GetDouble() : null,
            Success = Member(r, "success").ValueKind == JsonValueKind.True,
            Stalled = Member(r, "stalled").ValueKind == JsonValueKind.True,
            StartMs = OptionalLong(Member(r, "startMs")) ?? 0,
            FirstPassMs = OptionalLong(Member(r, "firstPassMs")),
            Counts = TaskCounts.Zero with
            {
                FixAttempts = (int)(OptionalLong(Member(Member(r, "counts"), "fixAttempts")) ?? 0),
                FixFailures = (int)(OptionalLong(Member(Member(r, "counts"), "fixFailures")) ?? 0),
                Recovered = (int)(OptionalLong(Member(Member(r, "counts"), "recovered")) ?? 0),
                RecoverAttempts = (int)(OptionalLong(Member(Member(r, "counts"), "recoverAttempts")) ?? 0),
            },
        }).ToList();
        var source = OptionalString(Member(item, "source")) is { } chosen ? AgentSpeed.Core.History.SourceNames.Parse(chosen) : null;
        var result = TaskComparison.Compare(records, item.GetProperty("first").GetString()!, item.GetProperty("last").GetString()!, source);
        var actual = new JsonObject
        {
            ["bySource"] = new JsonArray([.. result.BySource.Select(row =>
            {
                var node = Aggregate(row);
                node.Insert(0, "source", AgentSpeed.Core.History.SourceNames.Of(row.Source));
                return (JsonNode)node;
            })]),
            ["byModel"] = new JsonArray([.. result.ByModel.Select(row =>
            {
                var node = Aggregate(row);
                node.Insert(0, "model", row.Model);
                node.Insert(1, "sources", new JsonArray([.. row.Sources.Select(s => (JsonNode)AgentSpeed.Core.History.SourceNames.Of(s))]));
                return (JsonNode)node;
            })]),
        };
        AssertClose(item.GetProperty("expect"), actual, name);
    }

    private static JsonObject Aggregate(TaskAggregate a) => new()
    {
        ["tasks"] = a.Tasks,
        ["scoredTasks"] = a.ScoredTasks,
        ["medianScore"] = Number(a.MedianScore),
        ["medianShown"] = Number(a.MedianShown),
        ["lowerQuartile"] = Number(a.LowerQuartile),
        ["upperQuartile"] = Number(a.UpperQuartile),
        ["histogram"] = new JsonArray([.. a.Histogram.Select(b => (JsonNode)b)]),
        ["successes"] = a.Successes,
        ["stalls"] = a.Stalls,
        ["fixFailures"] = a.FixFailures,
        ["fixAttempts"] = a.FixAttempts,
        ["recovered"] = a.Recovered,
        ["successRate"] = Number(a.SuccessRate),
        ["stallRate"] = Number(a.StallRate),
        ["fixFailureRate"] = Number(a.FixFailureRate),
        ["recoverAttempts"] = Number(a.RecoverAttempts),
        ["timeToFirstPassMs"] = Number(a.TimeToFirstPassMs),
    };

    /// <summary>The fields of a stored record that calibration reads.</summary>
    private static List<TaskRecord> HistoryRecords(JsonElement array) =>
        [.. array.EnumerateArray().Select(r => new TaskRecord
        {
            AgentId = r.GetProperty("agentId").GetString()!,
            Ordinal = r.GetProperty("ordinal").GetInt32(),
            StartMs = r.GetProperty("startMs").GetInt64(),
            EndMs = r.GetProperty("endMs").GetInt64(),
            Complete = r.GetProperty("complete").GetBoolean(),
            Success = r.GetProperty("success").GetBoolean(),
            Snapshots = RowsOf(r.GetProperty("snapshots")),
            FixRows = RowsOf(r.GetProperty("fixRows")),
        })];

    private static List<double[]> RowsOf(JsonElement rows) =>
        [.. rows.EnumerateArray().Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray())];

    private static TaskModels Models(JsonElement given)
    {
        TaskModel Read(string name)
        {
            var m = given.GetProperty(name);
            return new TaskModel([.. m.GetProperty("coefficients").EnumerateArray().Select(c => c.GetDouble())],
                m.GetProperty("calibrated").GetBoolean(), m.GetProperty("tasks").GetInt32());
        }

        return new TaskModels(Read("success"), Read("stall"), Read("fix"));
    }

    private static TaskModel Pick(TaskModels models, string name) => name switch
    {
        "success" => models.Success,
        "stall" => models.Stall,
        _ => models.Fix,
    };

    private static TaskModelKind ModelKindOf(string name) => name switch
    {
        "success" => TaskModelKind.Success,
        "stall" => TaskModelKind.Stall,
        _ => TaskModelKind.Fix,
    };

    /// <summary>A probe's features in its model's order (<c>MODEL_FEATURES</c>).</summary>
    private static double[] ProbeFeatures(string model, JsonElement features)
    {
        string[] names = model switch
        {
            "success" => ["passing", "failing", "conv", "ig", "loop", "osc", "size"],
            "stall" => ["ig", "loop", "osc", "failing", "size"],
            _ => ["attempts", "errors", "trend", "osc", "ig"],
        };
        return [.. names.Select(n => features.GetProperty(n).GetDouble())];
    }
}
