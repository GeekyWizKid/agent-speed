using System.Text.Json;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;
using AgentSpeed.Presentation.Tests.TestSupport;

namespace AgentSpeed.Presentation.Tests;

public sealed class EffortCapsuleTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);

    /// <summary>Six DIP per character, so widths are easy to read.</summary>
    private static double Word(string word) => word.Length * 6;

    private static AgentStatus Agent(AgentPhase phase, ExecutionModes modes) =>
        new("a" + Guid.NewGuid(), SourceKind.Codex, "p", "gpt-5", phase, false, default, null, DateTimeOffset.UnixEpoch) { Modes = modes };

    private static ExecutionModes Modes(string? effort, string? tier = null) =>
        ExecutionModes.Of(ExecutionModeNative.Effort(effort, ExecutionModeEvidence.Recorded, ExecutionModeScope.Turn))
            .Merge(ExecutionModeNative.OpenAiTier(tier, ExecutionModeEvidence.Selected, ExecutionModeScope.Session));

    [Fact]
    public void LevelTopRungAndBoltsFollowTheSharedTables()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(RepositoryPaths.Spec("fixtures/execution-mode-cases.json")));
        foreach (var item in document.RootElement.GetProperty("effortDisplay").EnumerateArray())
        {
            var capsule = new EffortCapsule(item.GetProperty("value").GetString(), null);
            var expected = item.GetProperty("expected");
            Assert.Equal(expected.GetProperty("level").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("level").GetInt32(), capsule.Level);
            Assert.Equal(expected.GetProperty("top").GetBoolean(), capsule.IsTopRung);
        }

        foreach (var item in document.RootElement.GetProperty("speedBolts").EnumerateArray())
        {
            Assert.Equal(item.GetProperty("bolts").GetInt32(), new EffortCapsule(null, item.GetProperty("value").GetString()).Bolts);
        }
    }

    [Fact]
    public void TonesSeparateNeutralMaxAndTheTopRung()
    {
        Assert.Equal(EffortTone.Neutral, new EffortCapsule("xhigh", null).Tone);
        Assert.Equal(EffortTone.Max, new EffortCapsule("max", null).Tone);
        Assert.Equal(EffortTone.TopRung, new EffortCapsule("ultra", null).Tone);
        Assert.Equal(EffortTone.TopRung, new EffortCapsule("ultracode", null).Tone);
        Assert.Equal(EffortTone.Neutral, new EffortCapsule("persistent", null).Tone);
    }

    [Fact]
    public void ACapsuleShowsForAKnownEffortOrBoltsAndNeverForUnknownOrStandard()
    {
        Assert.False(new EffortCapsule(null, null).IsVisible);
        Assert.False(new EffortCapsule(null, "standard").IsVisible);
        Assert.False(new EffortCapsule(null, "flex").IsVisible);
        Assert.True(new EffortCapsule(null, "standard").IsKnown);
        Assert.True(new EffortCapsule(null, "fast").IsVisible);
        Assert.True(new EffortCapsule("off", null).IsVisible);
        Assert.Equal(new EffortCapsule("xhigh", "ultrafast"), EffortCapsule.Of(Modes("xhigh", "ultrafast")));
        Assert.Equal(new EffortCapsule(null, null), EffortCapsule.Of(ExecutionModes.Empty));
    }

    [Fact]
    public void WidthAddsPaddingMeterWordAndBoltsWithFivePointGaps()
    {
        var meter = EffortCapsule.MeterWidth;
        Assert.Equal(18.5, meter);
        Assert.Equal(12 + meter + 5 + 30, new EffortCapsule("xhigh", null).Width(compact: false, Word));
        Assert.Equal(12 + meter, new EffortCapsule("xhigh", null).Width(compact: true, Word));
        Assert.Equal(12 + meter + 5 + 30 + 5 + 15, new EffortCapsule("xhigh", "ultrafast").Width(compact: false, Word));
        Assert.Equal(12 + 9, new EffortCapsule(null, "fast").Width(compact: false, Word));
        Assert.Equal(0, new EffortCapsule(null, "standard").Width(compact: false, Word));
        Assert.Equal(15, EffortCapsule.BoltsWidth(2));
    }

    [Fact]
    public void CompactDropsTheWordWholeUnlessItIsAllTheCapsuleShows()
    {
        Assert.False(new EffortCapsule("max", null).ShowsWord(compact: true));
        Assert.True(new EffortCapsule("max", null).ShowsWord(compact: false));
        Assert.True(new EffortCapsule("persistent", null).ShowsWord(compact: true));
        Assert.False(new EffortCapsule("persistent", "fast").ShowsWord(compact: true));
        Assert.False(new EffortCapsule(null, "fast").ShowsWord(compact: false));
    }

    [Fact]
    public void TheColumnTakesTheWidestCapsuleAndDropsEveryWordBelowSeventyTwoPointsOfName()
    {
        EffortCapsule[] rows = [new("xhigh", null), new(null, null), new("max", "fast")];
        var full = 12 + 18.5 + 5 + 18 + 5 + 9;
        var wide = EffortCapsuleColumn.For(rows, nameRoom: 300, Word);
        Assert.Equal(new EffortCapsuleColumn(full, Compact: false), wide);
        Assert.Equal(full + 8, wide.Footprint);
        Assert.Equal(new EffortCapsuleColumn(full, Compact: false), EffortCapsuleColumn.For(rows, nameRoom: 72 + full + 8, Word));
        var narrow = EffortCapsuleColumn.For(rows, nameRoom: 72 + full + 7, Word);
        Assert.Equal(new EffortCapsuleColumn(12 + 18.5 + 5 + 9, Compact: true), narrow);
        Assert.Equal(EffortCapsuleColumn.None, EffortCapsuleColumn.For([new(null, "standard"), new(null, null)], nameRoom: 10, Word) with { Compact = false });
        Assert.Equal(0, EffortCapsuleColumn.For([], nameRoom: 300, Word).Footprint);
    }

    [Fact]
    public void TooltipSpellsTokensAndExplainsTheTopRung()
    {
        Assert.Equal(["Reasoning effort: xhigh", "Speed: Ultrafast"], new EffortCapsule("xhigh", "ultrafast").TooltipLines(En));
        Assert.Equal(["Reasoning effort: Not recorded", "Speed: flex"], new EffortCapsule(null, "flex").TooltipLines(En));
        Assert.Equal(["Reasoning effort: ultra", "Speed: Not recorded", En["effort.ultra.detail"]], new EffortCapsule("ultra", null).TooltipLines(En));
        Assert.Equal(En["effort.ultracode.detail"], new EffortCapsule("ultracode", "standard").TooltipLines(En)[2]);
    }

    [Fact]
    public void AccessibilityJoinsTheTwoLinesPerLanguage()
    {
        var capsule = new EffortCapsule("ultracode", "fast");
        Assert.Equal("Reasoning effort: ultracode, Speed: Fast", capsule.AccessibilityText(En));
        var zh = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        Assert.Equal("推理強度：ultracode，加速：Fast", capsule.AccessibilityText(zh));
    }

    [Fact]
    public void AGroupSharesTheGeneratingAgentsCapsuleOrReadsMixed()
    {
        var shared = SharedEffortCapsule.Of([Agent(AgentPhase.Generating, Modes("xhigh", "ultrafast")), Agent(AgentPhase.Idle, Modes("max"))]);
        Assert.Equal(new EffortCapsule("xhigh", "ultrafast"), shared!.Capsule);
        Assert.False(shared.Mixed);
        Assert.True(SharedEffortCapsule.Of([Agent(AgentPhase.Idle, Modes("xhigh")), Agent(AgentPhase.Tools, Modes("max"))])!.Mixed);
        Assert.Equal(new EffortCapsule("high", null),
            SharedEffortCapsule.Of([Agent(AgentPhase.Idle, Modes("high")), Agent(AgentPhase.Idle, ExecutionModes.Empty)])!.Capsule);
        Assert.Null(SharedEffortCapsule.Of([Agent(AgentPhase.Idle, ExecutionModes.Empty)]));
        Assert.Null(SharedEffortCapsule.Of([Agent(AgentPhase.Generating, Modes(null, "default"))]));
        Assert.Null(SharedEffortCapsule.Of([]));
    }
}
