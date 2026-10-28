using System.Text.Json;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The task parsers (TASK-PARSERS) against every case of <c>spec/fixtures/task-parser-cases.json</c>, as
/// <c>spec/tools/task_parsers.py</c> checks them. One partial file per group.
/// </summary>
public sealed partial class TaskParserCaseTests
{
    private static readonly string[] Groups =
        ["actionKind", "command", "outcome", "resultText", "session", "notification", "codeScript", "codeItems", "edits", "failures"];

    private static readonly Lazy<JsonElement[]> AllCases = new(() =>
        [.. JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("task-parser-cases.json"))).RootElement.GetProperty("cases").EnumerateArray()]);

    private static readonly Lazy<Dictionary<string, JsonElement>> ById = new(() =>
        AllCases.Value.ToDictionary(c => c.GetProperty("id").GetString()!, StringComparer.Ordinal));

    [Fact]
    public void EveryCaseBelongsToACheckedGroup()
    {
        Assert.True(AllCases.Value.Length >= 522, "the fixture holds " + AllCases.Value.Length + " cases");
        foreach (var item in AllCases.Value)
        {
            Assert.Contains(item.GetProperty("group").GetString(), Groups);
        }

        Assert.Equal(AllCases.Value.Length, ById.Value.Count);
    }

    internal static TheoryData<string> Ids(string group)
    {
        var data = new TheoryData<string>();
        foreach (var item in AllCases.Value)
        {
            if (item.GetProperty("group").GetString() == group)
            {
                data.Add(item.GetProperty("id").GetString()!);
            }
        }

        return data;
    }

    internal static JsonElement Case(string id) => ById.Value[id];

    internal static SourceKind Source(JsonElement item) =>
        AgentSpeed.Core.History.SourceNames.Parse(item.GetProperty("source").GetString()) ?? throw new InvalidDataException("unknown source in " + item);

    internal static string? Cwd(JsonElement item) =>
        item.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String ? cwd.GetString() : null;

    internal static string? Tool(JsonElement item) =>
        item.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.String ? tool.GetString() : null;

    /// <summary>A member as the parsers receive it; absent reads as <see cref="JsonValueKind.Undefined"/>.</summary>
    internal static JsonElement Member(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value : default;

    internal static long? OptionalLong(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();

    internal static bool? OptionalBool(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetBoolean();

    internal static string? OptionalString(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    /// <summary>Structural JSON equality: objects by member set, numbers by value.</summary>
    internal static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                return left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var other) && JsonEquals(p.Value, other));
            case JsonValueKind.Array:
                return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => JsonEquals(p.First, p.Second));
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Number:
                return a.TryGetInt64(out var x) && b.TryGetInt64(out var y) ? x == y : a.GetDouble().Equals(b.GetDouble());
            default:
                return true;
        }
    }
}
