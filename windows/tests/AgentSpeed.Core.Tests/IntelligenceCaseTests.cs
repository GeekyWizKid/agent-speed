using System.Text.Json;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Core.Tests;

/// <summary>The live intelligence index (METRICS 6.9) against <c>spec/fixtures/intelligence-cases.json</c>.</summary>
public sealed class IntelligenceCaseTests
{
    private static readonly Lazy<JsonElement> Document = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("intelligence-cases.json"))).RootElement);

    private static readonly Lazy<PriceTable> Table = new(() =>
        PriceTable.Load(File.ReadAllBytes(FixtureLocator.PathOf(Document.Value.GetProperty("table").GetString()!))).Table!);

    public static TheoryData<string> GradeNames() => Names("grades");

    public static TheoryData<string> WindowNames() => Names("windows");

    [Theory]
    [MemberData(nameof(GradeNames))]
    public void GradeMatches(string name)
    {
        var item = Case("grades", name);
        var expect = item.GetProperty("expect");
        var grade = ModelGrade.Of(Table.Value, Text(item, "model"), Text(item, "provider"), Text(item, "effort"));
        Assert.Equal(expect.GetProperty("class").GetInt32(), grade.ClassPoints);
        Assert.Equal(expect.GetProperty("level").GetInt32(), grade.Level);
        Assert.Equal(expect.GetProperty("grade").GetInt32(), grade.Value);
    }

    [Theory]
    [MemberData(nameof(WindowNames))]
    public void WindowMatches(string name)
    {
        var item = Case("windows", name);
        var engine = new SpeedEngine(0, new PriceBook([Table.Value]));
        foreach (var agent in item.GetProperty("agents").EnumerateArray())
        {
            engine.UpdateAgent(new AgentInfo(
                agent.GetProperty("id").GetString()!,
                EngineCaseTests.ParseSource(agent.GetProperty("source").GetString()!),
                null,
                Text(agent, "model"),
                false,
                EngineCaseTests.ParsePhase(agent.GetProperty("phase").GetString()!),
                Number(agent, "inFlightStartMs"),
                Number(agent, "lastLineMs")) { Modes = Effort(Text(agent, "effort")) });
        }

        foreach (var response in item.GetProperty("responses").EnumerateArray())
        {
            var tokens = new TokenCounts(
                Input: response.GetProperty("input").GetInt64(),
                Output: response.GetProperty("output").GetInt64(),
                Reasoning: response.GetProperty("reasoning").GetInt64());
            engine.Upsert(
                new ParsedResponse(
                    response.GetProperty("key").GetString()!,
                    EngineCaseTests.ParseSource(response.GetProperty("source").GetString()!),
                    response.GetProperty("agentId").GetString()!,
                    Text(response, "model"),
                    Number(response, "startMs"),
                    response.GetProperty("endMs").GetInt64(),
                    tokens,
                    response.GetProperty("final").GetBoolean(),
                    Text(response, "provider")) { Modes = Effort(Text(response, "effort")) },
                isCompletion: true,
                seeded: true);
        }

        foreach (var query in item.GetProperty("queries").EnumerateArray())
        {
            var nowMs = query.GetProperty("nowMs").GetInt64();
            var frame = engine.Compute(nowMs, 0, observeLive: false);
            var members = query.TryGetProperty("sources", out var sources)
                ? SourceSet.From(sources.EnumerateArray().Select(s => EngineCaseTests.ParseSource(s.GetString()!)))
                : SourceSet.Everything;
            var sums = members.Aggregate(IntelligenceSums.Zero, (total, kind) => total + frame.SourceIntelligence[(int)kind]);
            var expect = query.GetProperty("expect");
            var at = $"[{name} @ {nowMs} {members}]";
            if (expect.GetProperty("intelligence").ValueKind == JsonValueKind.Null)
            {
                Assert.Null(sums.Index);
                Assert.Null(sums.Shown);
            }
            else
            {
                EngineCaseTests.AssertClose(expect.GetProperty("intelligence").GetDouble(), sums.Index!.Value, at + " intelligence");
                Assert.Equal(expect.GetProperty("shown").GetInt32(), sums.Shown);
            }

            EngineCaseTests.AssertClose(expect.GetProperty("liveThinking").GetDouble(), sums.LiveThinking, at + " liveThinking");
            EngineCaseTests.AssertClose(expect.GetProperty("thinkingShare").GetDouble(), sums.ThinkingShare, at + " thinkingShare");
        }
    }

    [Fact]
    public void AnAgentCarriesTheGradeOfItsCurrentModelAndEffort()
    {
        var engine = new SpeedEngine(0, new PriceBook([Table.Value]));
        engine.UpdateAgent(new AgentInfo("a", SourceKind.ClaudeCode, null, "claude-opus-5-5", false, AgentPhase.Tools, null, 1000)
        {
            Modes = Effort("xhigh"),
        });
        var agent = Assert.Single(engine.Compute(2000, 0, observeLive: false).Agents);
        Assert.Equal(new ModelGrade(ModelClass.Flagship, 4), agent.Grade);
        Assert.Equal(124, agent.Grade.Value);
    }

    [Fact]
    public void ReasoningTokensWithoutOutputAndNothingInTheWindowLeaveTheIndexEmpty()
    {
        var sums = IntelligenceSums.Zero.Add(SourceKind.Codex, 0, 50, ModelGrade.Unknown);
        Assert.Equal(IntelligenceSums.Zero, sums);
        Assert.Null(sums.Index);
        Assert.Equal(0, sums.ThinkingShare);
    }

    private static ExecutionModes Effort(string? effort) =>
        ExecutionModes.Of(ExecutionModeNative.Effort(effort, ExecutionModeEvidence.Recorded, ExecutionModeScope.Request));

    private static TheoryData<string> Names(string group)
    {
        var data = new TheoryData<string>();
        foreach (var item in Document.Value.GetProperty(group).EnumerateArray())
        {
            data.Add(item.GetProperty("name").GetString()!);
        }

        return data;
    }

    private static JsonElement Case(string group, string name) =>
        Document.Value.GetProperty(group).EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
}
