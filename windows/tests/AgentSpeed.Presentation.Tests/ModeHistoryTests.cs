using AgentSpeed.Core;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Menus;
using AgentSpeed.Presentation.Modes;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Tests;

public sealed class ModeHistoryTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);

    private static ExecutionModeUsage Effort(string model, string mode, long requests, long output, long ms = 0) =>
        new(model, SourceKind.Codex, ExecutionModeKind.Effort, mode, mode == "unknown" ? "unknown" : "recorded", null, requests, output, ms > 0 ? output : 0, ms);

    private static DaySummary Day(int day, long requests, long output, long measuredOutput, long measuredMs, ModelUsage[] models, ExecutionModeUsage[] modes) =>
        new(new DateOnly(2026, 10, day), requests, 0, 0, 0, output, 0, measuredOutput, measuredMs, 0, 1, null, models, []) { Modes = modes };

    [Fact]
    public void RowsSumModelsAndEvidenceInDisplayOrderAndAddUpToTheRange()
    {
        var days = new[]
        {
            Day(23, 4, 400, 300, 3000, [new("a", 3, 300), new("b", 1, 100)],
                [Effort("a", "xhigh", 2, 200, 2000), Effort("a", "ultra", 1, 100, 1000), Effort("b", "unknown", 1, 100)]),
            Day(24, 3, 600, 600, 6000, [new("a", 1, 100), new("b", 2, 500)],
                [Effort("a", "xhigh", 1, 100, 1000), Effort("b", "xhigh", 1, 400, 4000) with { Evidence = "selected" }, Effort("b", "persistent", 1, 100, 1000)]),
        };
        var section = ModeHistory.Build(days, ExecutionModeKind.Effort);
        Assert.True(section.HasKnown);
        Assert.Equal(["ultra", "xhigh", "persistent", "unknown"], section.Rows.Select(r => r.Mode));
        Assert.Equal(7, section.Rows.Sum(r => r.Requests));
        Assert.Equal(1000, section.Rows.Sum(r => r.Output));
        var xhigh = section.Rows[1];
        Assert.Equal((4, 700L, 0.7), (xhigh.Requests, xhigh.Output, xhigh.Share));
        Assert.Equal(100, xhigh.AverageSpeed);
        Assert.Equal([new ModeHistoryModel("b", 400.0 / 700), new ModeHistoryModel("a", 300.0 / 700)], xhigh.TopModels);
        var unknown = section.Rows[^1];
        Assert.True(unknown.IsUnknown);
        Assert.Equal((1, 100L, 0L), (unknown.Requests, unknown.Output, unknown.MeasuredMs));
        Assert.Null(unknown.AverageSpeed);
        Assert.Equal("b", Assert.Single(unknown.TopModels).Model);
    }

    [Fact]
    public void DaysWithoutAnyModeRowFallIntoTheUnknownBucketAndTheEmptyNote()
    {
        var legacy = Day(20, 5, 500, 0, 0, [new("old", 5, 500)], []);
        var section = ModeHistory.Build([legacy], ExecutionModeKind.Speed);
        Assert.False(section.HasKnown);
        var only = Assert.Single(section.Rows);
        Assert.True(only.IsUnknown);
        Assert.Equal((5, 500L, 1.0), (only.Requests, only.Output, only.Share));
        Assert.Equal("old", Assert.Single(only.TopModels).Model);
        Assert.True(Assert.Single(ModeHistory.Build([], ExecutionModeKind.Effort).Rows).IsUnknown);
    }

    [Fact]
    public void AFullyKnownRangeHasNoUnknownRow()
    {
        var day = Day(24, 2, 200, 0, 0, [new("m", 2, 200)],
        [
            new("m", SourceKind.Codex, ExecutionModeKind.Speed, "standard", "selected", "openai", 1, 50, 0, 0),
            new("m", SourceKind.Codex, ExecutionModeKind.Speed, "ultrafast", "selected", "openai", 1, 150, 0, 0),
        ]);
        var section = ModeHistory.Build([day], ExecutionModeKind.Speed);
        Assert.Equal(["ultrafast", "standard"], section.Rows.Select(r => r.Mode));
        Assert.Equal(ModeHistoryTone.Amber, section.Tone(section.Rows[0]));
        Assert.Equal(ModeHistoryTone.Neutral, section.Tone(section.Rows[1]));
        Assert.Equal(new EffortCapsule(null, "ultrafast"), section.Glyph(section.Rows[0]));
    }

    [Fact]
    public void EffortBarsAreNeutralUpToXhighThenMaxThenTheTopRung()
    {
        var section = new ModeHistorySection(ExecutionModeKind.Effort, [], HasKnown: true);
        ModeHistoryRow Row(string mode) => new(mode, 1, 1, 0, 0, 1, []);
        Assert.Equal(ModeHistoryTone.Neutral, section.Tone(Row("xhigh")));
        Assert.Equal(ModeHistoryTone.Max, section.Tone(Row("max")));
        Assert.Equal(ModeHistoryTone.TopRung, section.Tone(Row("ultracode")));
        Assert.Equal(ModeHistoryTone.Unknown, section.Tone(Row("unknown")));
        Assert.Null(section.Glyph(Row("unknown")));
        Assert.Equal(new EffortCapsule("max", null), section.Glyph(Row("max")));
    }

    [Fact]
    public void TheOrbMenuCarriesTheCapsuleUnderItsHeaderOrMixedOrNothing()
    {
        var settings = AppSettings.Default with { Orbs = new OrbsDocument([new OrbDefinition("o0")]) };
        AgentStatus Agent(string effort) => new("a" + effort, SourceKind.Codex, "p", "gpt-5", AgentPhase.Generating, false, default, null,
            DateTimeOffset.UnixEpoch) { Modes = ExecutionModes.Of(ExecutionModeNative.Effort(effort, ExecutionModeEvidence.Recorded, ExecutionModeScope.Turn)) };

        var menu = OrbMenuModel.Build("o0", settings, SourceAvailability.All, En, agents: [Agent("ultra")])!;
        var row = Assert.IsType<MenuRow>(menu.Entries[1]);
        Assert.Equal(new EffortCapsule("ultra", null), row.Capsule);
        Assert.False(row.IsEnabled);
        Assert.Equal("Reasoning effort: ultra, Speed: Not recorded", row.Label);
        Assert.Contains(En["effort.ultra.detail"], row.ToolTip, StringComparison.Ordinal);

        var mixed = Assert.IsType<MenuRow>(OrbMenuModel.Build("o0", settings, SourceAvailability.All, En, agents: [Agent("high"), Agent("max")])!.Entries[1]);
        Assert.Null(mixed.Capsule);
        Assert.Equal(En["mode.mixed"], mixed.Detail);

        Assert.IsType<MenuSeparator>(OrbMenuModel.Build("o0", settings, SourceAvailability.All, En, agents: [Agent("auto")])!.Entries[1]);
        Assert.IsType<MenuSeparator>(OrbMenuModel.Build("o0", settings, SourceAvailability.All, En)!.Entries[1]);
    }
}
