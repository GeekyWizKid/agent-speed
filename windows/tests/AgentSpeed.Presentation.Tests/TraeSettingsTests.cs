using AgentSpeed.Core;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Tests;

public sealed class TraeSettingsTests
{
    [Fact]
    public void ToggleAndOverrideRoundTripIntoMonitorWithoutChangingOtherSources()
    {
        var path = Path.Combine(Path.GetTempPath(), "Trae CN");
        var settings = (AppSettings.Default with { TraeRoot = path }).WithSourceEnabled(SourceKind.Trae, false);
        var back = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings));
        Assert.Equal(path, back.TraeRoot);
        Assert.Equal(path, back.ToMonitorConfiguration().TraeRoot);
        Assert.DoesNotContain(SourceKind.Trae, back.ToMonitorConfiguration().EnabledSources);
        Assert.Contains(SourceKind.Codex, back.ToMonitorConfiguration().EnabledSources);
        Assert.NotEqual(AppSettings.Default.ToMonitorConfiguration(), back.ToMonitorConfiguration());
    }

    [Fact]
    public void TraeIdentifierIsStableAcrossEveryExportAndOrbSelection()
    {
        Assert.Equal("trae", ReportBody.SourceId(SourceKind.Trae));
        Assert.Equal("trae", HistoryCsv.SourceId(SourceKind.Trae));
        Assert.Equal("source.trae", DisplayText.SourceKey(SourceKind.Trae));
        Assert.Equal("trae", OrbSources.KeyOf(SourceKind.Trae));
        Assert.Equal(SourceSet.Of(SourceKind.Trae), OrbSources.FromKeys(["trae"]).Known);
    }
}
