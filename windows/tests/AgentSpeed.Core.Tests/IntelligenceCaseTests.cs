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

    public static TheoryData<string> AgentWindowNames() => Names("agentWindows");

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
        var engine = Engine(item);
        foreach (var query in item.GetProperty("queries").EnumerateArray())
        {
            var nowMs = query.GetProperty("nowMs").GetInt64();
            var frame = engine.Compute(nowMs, 0, observeLive: false);
            var members = query.TryGetProperty("sources", out var sources)
                ? SourceSet.From(sources.EnumerateArray().Select(s => EngineCaseTests.ParseSource(s.GetString()!)))
                : SourceSet.Everything;
            var sums = members.Aggregate(IntelligenceSums.Zero, (total, kind) => total + frame.SourceIntelligence[(int)kind]);
            AssertSums(query.GetProperty("expect"), sums, $"[{name} @ {nowMs} {members}]");
        }
    }

    [Theory]
    [MemberData(nameof(AgentWindowNames))]
    public void AgentWindowMatches(string name)
    {
        var item = Case("agentWindows", name);
        var engine = Engine(item);
        foreach (var query in item.GetProperty("queries").EnumerateArray())
        {
            var nowMs = query.GetProperty("nowMs").GetInt64();
            var frame = engine.Compute(nowMs, 0, observeLive: false);
            var at = $"[{name} @ {nowMs}]";
            var set = frame.SourceIntelligence.Aggregate(IntelligenceSums.Zero, (total, sums) => total + sums);
            AssertSums(query.GetProperty("expect"), set, at);

            var expected = query.GetProperty("agents").EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), frame.Agents.Select(a => a.Info.Id).Order(StringComparer.Ordinal));
            var added = frame.Agents.Aggregate(IntelligenceSums.Zero, (total, agent) => total + agent.Intelligence.Sums);
            EngineCaseTests.AssertClose(set.Output, added.Output, at + " agents' output");
            EngineCaseTests.AssertClose(set.Graded, added.Graded, at + " agents' graded output");
            EngineCaseTests.AssertClose(set.Thinking, added.Thinking, at + " agents' reasoning");
            EngineCaseTests.AssertClose(set.ThinkingBasis, added.ThinkingBasis, at + " agents' reasoning basis");

            foreach (var agent in frame.Agents)
            {
                var want = expected[agent.Info.Id];
                var label = $"{at} {agent.Info.Id}";
                var intelligence = agent.Intelligence;
                Assert.True(want.GetProperty("form").GetString() == Form(intelligence.Form), $"{label} form {intelligence.Form}");
                Assert.Equal(NullableInt(want, "grade"), agent.Grade?.Value);
                Assert.Equal(agent.Grade, intelligence.Grade);
                Assert.Equal(NullableInt(want, "shown"), intelligence.Shown);
                if (want.GetProperty("intelligence").ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(intelligence.Value);
                }
                else
                {
                    EngineCaseTests.AssertClose(want.GetProperty("intelligence").GetDouble(), intelligence.Value!.Value, label + " intelligence");
                }

                EngineCaseTests.AssertClose(want.GetProperty("liveThinking").GetDouble(), intelligence.LiveThinking, label + " liveThinking");
                EngineCaseTests.AssertClose(want.GetProperty("thinkingShare").GetDouble(), intelligence.ThinkingShare, label + " thinkingShare");
            }
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
        Assert.Equal(124, agent.Grade!.Value.Value);
        Assert.Equal(AgentIntelligenceForm.Resting, agent.Intelligence.Form);
        Assert.Equal(124, agent.Intelligence.Shown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAgentWithoutAModelHasNoGradeButItsEstimateStillWeighsAsAnUnknownModel(string? model)
    {
        var engine = new SpeedEngine(0, new PriceBook([Table.Value]));
        engine.UpdateAgent(new AgentInfo("idle", SourceKind.ClaudeCode, null, model, false, AgentPhase.Idle, null, 1000));
        engine.UpdateAgent(new AgentInfo("busy", SourceKind.ClaudeCode, null, model, false, AgentPhase.Generating, 1000, 1000));
        engine.Upsert(Response("r", "other", "plain-model-1", 0, 1000, 100), isCompletion: true, seeded: true);
        var frame = engine.Compute(2000, 0, observeLive: false);
        var idle = frame.Agents.Single(a => a.Info.Id == "idle");
        Assert.Null(idle.Grade);
        Assert.Equal(AgentIntelligenceForm.None, idle.Intelligence.Form);
        Assert.Null(idle.Intelligence.Shown);
        var busy = frame.Agents.Single(a => a.Info.Id == "busy");
        Assert.Null(busy.Grade);
        Assert.Equal(AgentIntelligenceForm.Live, busy.Intelligence.Form);
        Assert.Equal(ModelGrade.Unknown.Value, busy.Intelligence.Shown);
    }

    [Fact]
    public void ASubAgentsOutputNeverCountsForItsParent()
    {
        var engine = new SpeedEngine(0, new PriceBook([Table.Value]));
        engine.UpdateAgent(new AgentInfo("parent", SourceKind.ClaudeCode, null, "claude-opus-5-5", false, AgentPhase.Tools, null, 4000)
        {
            Modes = Effort("xhigh"),
        });
        engine.UpdateAgent(new AgentInfo("child", SourceKind.ClaudeCode, null, "claude-haiku-5", true, AgentPhase.Idle, null, 4000, "parent"));
        engine.Upsert(Response("r", "child", "claude-haiku-5", 1000, 4000, 300), isCompletion: true, seeded: true);
        var frame = engine.Compute(4000, 0, observeLive: false);
        var parent = frame.Agents.Single(a => a.Info.Id == "parent");
        var child = frame.Agents.Single(a => a.Info.Id == "child");
        Assert.Equal(AgentIntelligenceForm.Resting, parent.Intelligence.Form);
        Assert.Equal(IntelligenceSums.Zero, parent.Intelligence.Sums);
        Assert.Equal(AgentIntelligenceForm.Live, child.Intelligence.Form);
        Assert.Equal(frame.SourceIntelligence[(int)SourceKind.ClaudeCode], child.Intelligence.Sums);
    }

    [Fact]
    public void ReasoningTokensWithoutOutputAndNothingInTheWindowLeaveTheIndexEmpty()
    {
        var sums = IntelligenceSums.Zero.Add(SourceKind.Codex, 0, 50, ModelGrade.Unknown);
        Assert.Equal(IntelligenceSums.Zero, sums);
        Assert.Null(sums.Index);
        Assert.Equal(0, sums.ThinkingShare);
    }

    private static SpeedEngine Engine(JsonElement item)
    {
        var engine = new SpeedEngine(0, new PriceBook([Table.Value]));
        foreach (var agent in item.GetProperty("agents").EnumerateArray())
        {
            engine.UpdateAgent(new AgentInfo(
                agent.GetProperty("id").GetString()!,
                EngineCaseTests.ParseSource(agent.GetProperty("source").GetString()!),
                null,
                Text(agent, "model"),
                Text(agent, "parentId") is not null,
                EngineCaseTests.ParsePhase(agent.GetProperty("phase").GetString()!),
                Number(agent, "inFlightStartMs"),
                Number(agent, "lastLineMs"),
                Text(agent, "parentId")) { Modes = Effort(Text(agent, "effort")) });
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

        return engine;
    }

    private static void AssertSums(JsonElement expect, IntelligenceSums sums, string at)
    {
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

    private static ParsedResponse Response(string key, string agentId, string model, long startMs, long endMs, long output) =>
        new(key, SourceKind.ClaudeCode, agentId, model, startMs, endMs, new TokenCounts(Input: 1000, Output: output), true);

    private static string Form(AgentIntelligenceForm form) => form switch
    {
        AgentIntelligenceForm.Live => "live",
        AgentIntelligenceForm.Resting => "resting",
        _ => "none",
    };

    private static int? NullableInt(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetInt32();

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
