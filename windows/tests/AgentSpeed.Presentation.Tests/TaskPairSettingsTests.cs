using System.Text.Json;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The history window remembers the task performance pair (DESIGN 12, <i>Header</i>).</summary>
public class TaskPairSettingsTests
{
    [Fact]
    public void ThePairRoundTripsAsTwoSourceKeys()
    {
        var settings = AppSettings.Default with { HistoryTaskPair = new TaskPair(SourceKind.Pi, SourceKind.Codex) };
        var json = SettingsSerializer.Serialize(settings);
        using var document = JsonDocument.Parse(json);
        var pair = document.RootElement.GetProperty("history").GetProperty("taskPair");
        Assert.Equal(["pi", "codex"], pair.EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new TaskPair(SourceKind.Pi, SourceKind.Codex), SettingsSerializer.Deserialize(json).HistoryTaskPair);
    }

    [Fact]
    public void NoPairIsWrittenByDefaultAndInvalidOnesAreDropped()
    {
        using var document = JsonDocument.Parse(SettingsSerializer.Serialize(AppSettings.Default));
        Assert.False(document.RootElement.GetProperty("history").TryGetProperty("taskPair", out _));
        Assert.Null((AppSettings.Default with { HistoryTaskPair = new TaskPair(SourceKind.Codex, SourceKind.Codex) }).Validated().HistoryTaskPair);
        Assert.Null((AppSettings.Default with { HistoryTaskPair = new TaskPair(SourceKind.Codex, SourceKind.Gemini) }).Validated().HistoryTaskPair);

        var json = SettingsSerializer.Serialize(AppSettings.Default)
            .Replace("\"modeKind\"", "\"taskPair\": [\"codex\", \"nope\"], \"modeKind\"", StringComparison.Ordinal);
        Assert.Null(SettingsSerializer.Deserialize(json).HistoryTaskPair);
    }
}
