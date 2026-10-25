using AgentSpeed.Core.Reporting;
using AgentSpeed.Core.Traffic;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>
/// The WebSocket transport against <c>spec/tools/report_server.py --websocket</c> on loopback: the real ClientWebSocket, the
/// signed upgrade and report messages, and the service's answers. Never reaches beyond 127.0.0.1.
/// </summary>
[Collection("report server")]
public sealed class ReportStreamEndToEndTests
{
    private static readonly ReportClientInfo Client = new("1.3.0", "Windows", 11);

    [Fact]
    public async Task ReadyThenAckedAndDuplicateReports()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket", "--api-path", "/agent-speed", "--ws-min-interval", "50");
        var device = await Device.Register(server, "ws-e2e");
        Assert.Equal(ReportStreamSettings.Default, device.Document.WebSocket);
        Assert.Equal($"ws://127.0.0.1:{server.Port}/agent-speed/v1/reports/ws", ReportWebSocketProtocol.Url(device.Document.Api).AbsoluteUri);

        var events = new ReportStreamSocketTests.EventLog();
        var socket = device.Open(events);
        Assert.IsType<ReportStreamEvent.Opened>(await events.Next());
        var ready = Assert.IsType<ReportServiceMessage.Ready>(Message(await events.Next()));
        Assert.Equal(device.Document.WebSocket, ready.Limits);

        var body = device.Body(1);
        Assert.True(socket.Write(device.Message(body)));
        var ack = Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next()));
        Assert.Equal((device.RunId, 1L, ReportAckStatus.Accepted, ReportHash.Sha256Hex(body)), (ack.RunId, ack.Seq, ack.Status, ack.BodySha256));

        Assert.True(socket.Write(device.Message(body)));
        Assert.Equal(ReportAckStatus.Duplicate, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Status);

        var second = device.Body(2);
        Assert.True(socket.Write(device.Message(second)));
        Assert.Equal(2, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Seq);
        socket.Close();
        await Task.Delay(300);
        Assert.Equal(5, events.All.Count);
        Assert.Contains("ws: ", server.Log, StringComparison.Ordinal);
        Assert.DoesNotContain(device.Signature, server.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedHandshakeIsClassifiedFromItsV1ErrorBody()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket");
        var device = await Device.Register(server, "ws-refused");
        var events = new ReportStreamSocketTests.EventLog();
        device.Open(events, new SoftwareReportSigner());
        var rejection = Assert.IsType<ReportStreamEvent.Rejected>(await events.Next()).Rejection;
        Assert.Equal((401, "signature_invalid"), (rejection.Status, rejection.Code));

        var unknown = new Device(device.Document, ReportCanonical.NewUuid(), "nobody");
        unknown.Open(events);
        rejection = Assert.IsType<ReportStreamEvent.Rejected>(await events.Next()).Rejection;
        Assert.Equal((404, "unknown_device"), (rejection.Status, rejection.Code));
    }

    [Fact]
    public async Task ANewerConnectionReplacesTheOlderWith4009()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket");
        var device = await Device.Register(server, "ws-replaced");
        var first = new ReportStreamSocketTests.EventLog();
        var older = device.Open(first);
        await first.Next();
        await first.Next();
        var second = new ReportStreamSocketTests.EventLog();
        var newer = device.Open(second);
        await second.Next();
        Assert.IsType<ReportServiceMessage.Ready>(Message(await second.Next()));
        Assert.Equal(new ReportStreamEvent.Closed(older.Generation, 4009), await first.Next());

        Assert.True(newer.Write(device.Message(device.Body(1))));
        Assert.Equal(ReportAckStatus.Accepted, Assert.IsType<ReportServiceMessage.Ack>(Message(await second.Next())).Status);
        newer.Abort();
    }

    private static ReportServiceMessage Message(ReportStreamEvent e) => Assert.IsType<ReportStreamEvent.Message>(e).Value;

    /// <summary>A registered device and the bodies and messages of its one run.</summary>
    private sealed class Device(ReportDiscovery document, string deviceId, string username)
    {
        private readonly SoftwareReportSigner _signer = new();

        public ReportDiscovery Document { get; } = document;

        public string RunId { get; } = ReportCanonical.NewUuid();

        /// <summary>The last message's signature (it must never reach the server's log).</summary>
        public string Signature { get; private set; } = "-";

        public static async Task<Device> Register(ReferenceServer server, string username)
        {
            using var transport = new HttpReportTransport();
            var api = new ReportApi(transport, Client.UserAgent);
            var document = Assert.IsType<ReportDiscoveryOutcome.Document>(await api.DiscoverAsync(server.Service)).Discovery;
            var device = new Device(document, ReportCanonical.NewUuid(), username);
            var request = device.Builder.Make("POST", document.Api.Endpoint(ReportEndpoint.Devices),
                ReportDocuments.Registration(device.DeviceId, username, device._signer.PublicKey, Client), device._signer, Now);
            var result = await api.CallAsync(request);
            Assert.Equal("created", result.Reply?.Result);
            return device;
        }

        public string DeviceId { get; } = deviceId;

        private ReportRequestBuilder Builder => new(Client.UserAgent, DeviceId, null);

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public IReportStreamSocket Open(ReportStreamSocketTests.EventLog events, IReportSigner? signer = null) =>
            new WebSocketReportTransport().Open(ReportWebSocketProtocol.Handshake(Builder, Document.Api, signer ?? _signer, Now),
                Document.WebSocket!, events.Add);

        public string Message(byte[] body)
        {
            var request = Builder.Make("POST", Document.Api.Endpoint(ReportEndpoint.Reports), body, _signer, Now);
            Signature = request.Header(ReportCanonical.SignatureHeader)!;
            return ReportWebSocketProtocol.ReportMessage(request.Headers, body);
        }

        public byte[] Body(int seq)
        {
            var counters = new ReportCounters();
            counters.Add(new ReportUsage(1, 120, 9000, 8000, 0, 0, 120, 900, 300), new ReportModelKey("gpt-5.5", SourceKind.Codex));
            var facts = new ReportLiveFacts(
                new Dictionary<SourceKind, ReportAgentCounts> { [SourceKind.Codex] = new(1, 0, 1) },
                new Dictionary<ReportModelKey, int> { [new("gpt-5.5", SourceKind.Codex)] = 1 }, counters, 1);
            var sample = new ReportSample(new Rate(133.3, 9000), 0, new LiveCost(900, 133.3),
                new Dictionary<SourceKind, ReportSourceSample> { [SourceKind.Codex] = new(new Rate(133.3, 9000), Rate.Zero, new LiveCost(900, 133.3)) },
                facts, ReportTrafficSample.Unavailable);
            var envelope = new ReportEnvelope(Client, DeviceId, username, RunId, seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 120,
                ReportActivity.Active, 3);
            var baseline = ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Epoch = 1 } };
            var options = new ReportOptions(Document.Sections, Document.Limits.MaxModels, Document.Limits.SendLimitBytes)
            {
                Stream = new ReportStreamBody(40, Document.WebSocket!.MaxMessageBytes),
            };
            return ReportBody.Fitted(envelope, new ReportWindow(baseline).Content(sample), sample, options).Data;
        }
    }
}
