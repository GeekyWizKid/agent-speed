using AgentSpeed.Core;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Tests;

public sealed class HarnessSourceSettingsTests
{
    [Fact]
    public void NewSourceFoldersAndTogglesRoundTripAndReachTheMonitorIndependently()
    {
        var settings = AppSettings.Default with
        {
            ZCodeRoot = Path.Combine(Path.GetTempPath(), ".zcode", "cli", "db"),
            KimiRoot = Path.Combine(Path.GetTempPath(), ".kimi-code", "sessions"),
            GrokBuildRoot = Path.Combine(Path.GetTempPath(), ".grok", "sessions"),
        };
        settings = settings.WithSourceEnabled(SourceKind.Kimi, false);
        var back = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings));
        Assert.Equal(settings.ZCodeRoot, back.ZCodeRoot);
        Assert.Equal(settings.KimiRoot, back.KimiRoot);
        Assert.Equal(settings.GrokBuildRoot, back.GrokBuildRoot);
        var config = back.ToMonitorConfiguration();
        Assert.Equal(settings.ZCodeRoot, config.ZCodeRoot);
        Assert.Equal(settings.KimiRoot, config.KimiRoot);
        Assert.Equal(settings.GrokBuildRoot, config.GrokBuildRoot);
        Assert.DoesNotContain(SourceKind.Kimi, config.EnabledSources);
        Assert.Contains(SourceKind.ZCode, config.EnabledSources);
        Assert.Contains(SourceKind.GrokBuild, config.EnabledSources);
        Assert.NotEqual(AppSettings.Default.ToMonitorConfiguration(), config);
    }

    [Fact]
    public void ThePiFolderAndToggleRoundTripAndReachTheMonitor()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pi-sessions");
        var settings = AppSettings.Default.WithSourceEnabled(SourceKind.Pi, false) with { PiRoot = $"  {folder}  " };
        var back = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings.Validated()));

        Assert.Equal((false, folder), (back.PiEnabled, back.PiRoot));
        var config = back.ToMonitorConfiguration();
        Assert.Equal(folder, config.PiRoot);
        Assert.DoesNotContain(SourceKind.Pi, config.EnabledSources);
        Assert.NotEqual(AppSettings.Default.ToMonitorConfiguration(), (AppSettings.Default with { PiRoot = folder }).ToMonitorConfiguration());
        Assert.Null((AppSettings.Default with { PiRoot = Path.Combine("relative", "sessions") }).Validated().PiRoot);
    }

    [Fact]
    public void OlderSettingsFilesKeepPiOnWithItsAutomaticRoots()
    {
        var s = SettingsSerializer.Deserialize("""
            { "version": 1, "sources": { "codex": true, "omp": false, "grokBuild": true, "grokBuildRoot": null } }
            """);

        Assert.True(s.PiEnabled);
        Assert.Null(s.PiRoot);
        var config = s.ToMonitorConfiguration();
        Assert.Contains(SourceKind.Pi, config.EnabledSources);
        Assert.Null(config.PiRoot);
    }

    [Theory]
    [InlineData(SourceKind.ZCode, "zcode")]
    [InlineData(SourceKind.Kimi, "kimi")]
    [InlineData(SourceKind.GrokBuild, "grokBuild")]
    [InlineData(SourceKind.Pi, "pi")]
    public void EverySurfaceUsesTheSameStableSourceIdentifier(SourceKind kind, string id)
    {
        Assert.Equal(id, ReportBody.SourceId(kind));
        Assert.Equal(id, OrbSources.KeyOf(kind));
        Assert.Equal("source." + id, DisplayText.SourceKey(kind));
        Assert.Equal(id, HistoryCsv.SourceId(kind));
        Assert.Equal([id], OrbSources.Of(kind).ToKeys());
        Assert.Equal(SourceSet.Of(kind), OrbSources.FromKeys([id]).Known);
        Assert.Equal("deepseek", ReportBody.SourceId(SourceKind.DeepSeek));
    }
}
