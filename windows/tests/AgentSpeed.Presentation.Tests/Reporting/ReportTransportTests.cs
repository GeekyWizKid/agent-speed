using System.Text.Json;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Reporting;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests.Reporting;

/// <summary>The per-service transport choice (DESIGN 11, item 2b) and what the Transport row says under its control.</summary>
public class ReportTransportTests
{
    private const string StreamingDocument = """
        {"protocol":"agent-speed-report","versions":[1],
         "service":{"name":"Live board","terms":"https://example.com/terms","privacy":"https://example.com/privacy"},
         "api":"https://api.example.com/agent-speed",
         "extensions":{"reportWebSocket":1},
         "websocket":{"path":"/v1/reports/ws","subprotocol":"agent-speed-report.v1","minIntervalMs":MIN}}
        """;

    private static readonly ReportServiceUrl Radar = ReportSettingsTests.Service("https://deng.codexradar.com");
    private static readonly ReportServiceUrl Board = ReportSettingsTests.Service("https://board.example.com");

    private static ReportDiscovery Streaming(int minIntervalMs) =>
        ReportSettingsTests.Document(StreamingDocument.Replace("MIN", minIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal), "https://board.example.com");

    private static ReportPreferences Read(string reportJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        return ReportSettingsJson.Read(document.RootElement.Clone());
    }

    private static ReportPreferences RoundTrip(ReportPreferences report) =>
        SettingsSerializer.Deserialize(SettingsSerializer.Serialize(AppSettings.Default with { Report = report })).Report;

    [Fact]
    public void EveryServiceUsesHttpUntilTheUserChoosesWebSocket()
    {
        var preferences = ReportPreferences.Default;
        Assert.Equal(ReportTransportKind.Http, preferences.TransportOf(Radar));
        Assert.Equal(ReportTransportKind.Http, preferences.TransportOf(Board));
        Assert.Equal(ReportTransportKind.Http, preferences.TransportOf(null));
    }

    [Fact]
    public void SettingsOfAnEarlierBuildKeepHttp()
    {
        var preferences = Read("""
            {"enabled":true,"preset":"custom","customUrl":"https://board.example.com","sections":["sources"],"identities":[]}
            """);
        Assert.True(preferences.Enabled);
        Assert.Empty(preferences.WebSocketServices);
        Assert.Equal(ReportTransportKind.Http, preferences.TransportOf(Board));
    }

    [Fact]
    public void TheChoiceBelongsToOneServiceAndCanBeUndone()
    {
        var chosen = ReportPreferences.Default.WithTransport(Board.Origin, ReportTransportKind.WebSocket);
        Assert.Equal(ReportTransportKind.WebSocket, chosen.TransportOf(Board));
        Assert.Equal(ReportTransportKind.Http, chosen.TransportOf(Radar));
        Assert.Equal(chosen, chosen.WithTransport(Board.Origin, ReportTransportKind.WebSocket));
        Assert.NotEqual(ReportPreferences.Default, chosen);

        var undone = chosen.WithTransport(Board.Origin, ReportTransportKind.Http);
        Assert.Equal(ReportTransportKind.Http, undone.TransportOf(Board));
        Assert.Equal(ReportPreferences.Default, undone);
    }

    [Fact]
    public void TheChoiceSurvivesTheSettingsFile()
    {
        var chosen = ReportPreferences.Default with { Preset = ReportPreset.Custom, CustomUrl = Board.Text };
        chosen = chosen.WithTransport(Board.Origin, ReportTransportKind.WebSocket).WithTransport(Radar.Origin, ReportTransportKind.WebSocket);
        var loaded = RoundTrip(chosen);
        Assert.Equal([Board.Origin, Radar.Origin], loaded.WebSocketServices.ToArray());
        Assert.Equal(ReportTransportKind.WebSocket, loaded.TransportOf(Board));
        Assert.Equal(chosen, loaded);
    }

    [Fact]
    public void AStoredListIsCleanedAndBounded()
    {
        var origins = Enumerable.Range(0, ReportPreferences.MaxWebSocketServices + 4).Select(i => $"\"https://s{i}.example.com\"");
        var preferences = Read($$"""
            {"webSocketServices":["https://board.example.com",7,"",null,{{string.Join(',', origins)}},"https://board.example.com"]}
            """);
        Assert.Equal(ReportPreferences.MaxWebSocketServices, preferences.WebSocketServices.Length);
        Assert.Equal("https://board.example.com", preferences.WebSocketServices[^1]);
        Assert.Single(preferences.WebSocketServices, o => o == "https://board.example.com");
        Assert.DoesNotContain("https://s0.example.com", preferences.WebSocketServices);
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(Board));

        Assert.Empty(Read("""{"webSocketServices":"https://board.example.com"}""").WebSocketServices);
    }

    [Fact]
    public void TheOldestChoiceGoesFirstWhenTheListIsFull()
    {
        var preferences = ReportPreferences.Default;
        for (var i = 0; i <= ReportPreferences.MaxWebSocketServices; i++)
        {
            preferences = preferences.WithTransport($"https://s{i}.example.com", ReportTransportKind.WebSocket);
        }

        Assert.Equal(ReportPreferences.MaxWebSocketServices, preferences.WebSocketServices.Length);
        Assert.DoesNotContain("https://s0.example.com", preferences.WebSocketServices);
        Assert.Equal($"https://s{ReportPreferences.MaxWebSocketServices}.example.com", preferences.WebSocketServices[^1]);
        Assert.Equal(preferences.WebSocketServices, preferences.Validated().WebSocketServices, StringComparer.Ordinal);
    }

    [Fact]
    public void HttpExplainsBothTransports()
    {
        Assert.Equal(new ReportTransportNotes("report.transport.hint", false, null), ReportTransportNotes.For(ReportTransportKind.Http, null));
        Assert.Equal(new ReportTransportNotes("report.transport.hint", false, null),
            ReportTransportNotes.For(ReportTransportKind.Http, ReportSettingsTests.Document(ReportSettingsTests.CodexRadarDocument, "https://deng.codexradar.com")));
    }

    [Fact]
    public void WebSocketSaysWhereTheDataComesFrom()
    {
        Assert.Equal(new ReportTransportNotes("report.transport.source", false, null), ReportTransportNotes.For(ReportTransportKind.WebSocket, null));
        Assert.Equal(new ReportTransportNotes("report.transport.source", false, null), ReportTransportNotes.For(ReportTransportKind.WebSocket, Streaming(50)));
    }

    [Fact]
    public void ADocumentWithoutTheCapabilityReplacesTheCaption()
    {
        var radar = ReportSettingsTests.Document(ReportSettingsTests.CodexRadarDocument, "https://deng.codexradar.com");
        Assert.Null(radar.WebSocket);
        Assert.Equal(new ReportTransportNotes(null, true, null), ReportTransportNotes.For(ReportTransportKind.WebSocket, radar));
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(50, null)]
    [InlineData(51, 51)]
    [InlineData(250, 250)]
    [InlineData(1000, 1000)]
    public void AServiceSlowerThanLiveIsNamed(int minIntervalMs, int? throttled)
    {
        Assert.Equal(throttled, ReportTransportNotes.For(ReportTransportKind.WebSocket, Streaming(minIntervalMs)).ThrottledMs);
        Assert.Null(ReportTransportNotes.For(ReportTransportKind.Http, Streaming(minIntervalMs)).ThrottledMs);
    }

    [Fact]
    public void TheTransportStringsExistInEveryLanguage()
    {
        var table = Localization.Localizer.LoadEmbeddedTable();
        foreach (var language in Localization.LanguageCodes.All)
        {
            var loc = new Localization.Localizer(table, language);
            foreach (var key in new[] { "report.transport", "report.transport.http", "report.transport.websocket", "report.transport.hint",
                "report.transport.source", "report.status.wsUnsupported", "report.preview.onChange" })
            {
                Assert.NotEqual(key, loc[key]);
            }

            Assert.Contains("250", loc.Format("report.transport.throttled", 250), StringComparison.Ordinal);
        }
    }
}
