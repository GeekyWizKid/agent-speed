using System.Text.Json;
using System.Text.Json.Nodes;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary><c>continuations</c>, <c>promptLines</c> and <c>segmentation</c> (METRICS 6.9 <i>Tasks</i>).</summary>
public sealed partial class TaskScoreCaseTests
{
    public static TheoryData<int, string> ContinuationCases() => Indices("continuations");

    public static TheoryData<int, string> PromptLineCases() => Indices("promptLines");

    public static TheoryData<int, string> SegmentationCases() => Indices("segmentation");

    [Theory]
    [MemberData(nameof(ContinuationCases))]
    public void ContinuationMatches(int index, string text)
    {
        var item = Case("continuations", index);
        Assert.Equal(item.GetProperty("expect").GetBoolean(), Continuations.IsContinuation(text));
    }

    [Theory]
    [MemberData(nameof(PromptLineCases))]
    public void PromptLineMatches(int index, string name)
    {
        var item = Case("promptLines", index);
        var found = PromptLines.Classify(Source(item.GetProperty("source")), item.GetProperty("line"));
        JsonObject? actual = null;
        if (found is { } line)
        {
            actual = new JsonObject
            {
                ["kind"] = line.Kind switch
                {
                    PromptLineKind.Prompt => "prompt",
                    PromptLineKind.Command => "command",
                    _ => "commandOutput",
                },
            };
            if (line.Kind == PromptLineKind.Prompt)
            {
                actual["continuation"] = line.Continuation;
            }
            else
            {
                Assert.False(line.Continuation);
            }

            if (line.T is { } t)
            {
                actual["t"] = t;
            }
        }

        AssertClose(item.GetProperty("expect"), actual, name);
    }

    [Theory]
    [MemberData(nameof(SegmentationCases))]
    public void SegmentationMatches(int index, string name)
    {
        var item = Case("segmentation", index);
        var lines = item.GetProperty("lines").EnumerateArray().Select(line => new SegmentLine(
            line.GetProperty("t").GetInt64(),
            line.GetProperty("line").GetString() switch
            {
                "prompt" => SegmentLineKind.Prompt,
                "command" => SegmentLineKind.Command,
                "commandOutput" => SegmentLineKind.CommandOutput,
                "assistant" => SegmentLineKind.Assistant,
                var other => throw new InvalidDataException("unknown line " + other),
            },
            Member(line, "continuation").ValueKind == JsonValueKind.True,
            Member(line, "duplicate").ValueKind == JsonValueKind.True));
        var actual = new JsonArray([.. Segmentation.Segment(lines).Select(p => (JsonNode)new JsonObject
        {
            ["t"] = p.T,
            ["type"] = "prompt",
            ["continuation"] = p.Continuation,
        })]);
        AssertClose(item.GetProperty("expect"), actual, name);
    }
}
