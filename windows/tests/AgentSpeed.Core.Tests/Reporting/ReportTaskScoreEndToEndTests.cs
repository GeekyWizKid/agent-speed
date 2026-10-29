using System.Net.Http;
using System.Text.Json;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>
/// The task score extension end to end (METRICS 11.10) against <c>spec/tools/report_server.py --task-score</c> on loopback:
/// real transcripts read by the live monitor with the watch on, the composer's completions counted by the engine, the
/// report side's task window, the body builder, signing and both transports. The server's merged model table shows every
/// completed task counted once — a retried report is a duplicate — and the live values of the latest report. Never
/// reaches beyond 127.0.0.1.
/// </summary>
[Collection("report server")]
public sealed class ReportTaskScoreEndToEndTests
{
    private static readonly ReportClientInfo Client = new("1.3.0", "Windows", 11);

    [Fact]
    public async Task OverHttpIncrementsCountOnceAndLiveValuesAreAccepted()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--task-score", "--api-path", "/agent-speed");
        using var transport = new HttpReportTransport();
        var api = new ReportApi(transport, Client.UserAgent);
        var device = await TaskDevice.Register(api, server, "task-http");
        Assert.True(device.Document.Extensions.TaskScore);
        using var pipeline = new TaskPipeline(device.Document);

        pipeline.CompleteATask("fix the parser", passing: true);
        var first = pipeline.Build(device, seq: 1);
        Assert.Equal(1, Completed(first));
        Assert.Equal("accepted", (await device.Post(api, first)).Result);
        Assert.Equal("duplicate", (await device.Post(api, first)).Result);
        await AssertBoard(device.Document, first, completed: 1);

        var second = pipeline.Build(device, seq: 2);
        Assert.Equal(0, Completed(second));
        Assert.Equal("accepted", (await device.Post(api, second)).Result);
        await AssertBoard(device.Document, second, completed: 1);

        pipeline.CompleteATask("fix the docs", passing: false, thenWork: true);
        var third = pipeline.Build(device, seq: 3);
        Assert.Equal("live", JsonDocument.Parse(third).RootElement.GetProperty("taskScore").GetProperty("form").GetString());
        Assert.Equal(1, Completed(third));
        Assert.Equal("accepted", (await device.Post(api, third)).Result);
        await AssertBoard(device.Document, third, completed: 2);

        // A service that advertises the extension rejects broken task fields and uses up no sequence number (REPORTING 8.3).
        var broken = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(third)!;
        broken["run"] = JsonDocument.Parse($$"""{"id":"{{device.RunId}}","seq":4}""").RootElement;
        broken["taskScore"] = JsonDocument.Parse("""{"score":50,"form":"none","thinkingTokS":0}""").RootElement;
        var rejected = await device.Post(api, JsonSerializer.SerializeToUtf8Bytes(broken));
        Assert.Equal((422, "payload_invalid"), (rejected.Status, rejected.Code));
        Assert.Equal(new ReportReaction.Strike(false), ReportOutcome.Classify(rejected, false).Reaction);
        await AssertBoard(device.Document, third, completed: 2);
        Assert.Contains("+1 tasks", server.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverWebSocketIncrementsCountOnceAndLiveValuesAreAccepted()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--task-score", "--websocket", "--ws-min-interval", "50");
        using var transport = new HttpReportTransport();
        var api = new ReportApi(transport, Client.UserAgent);
        var device = await TaskDevice.Register(api, server, "task-ws");
        Assert.True(device.Document.Extensions.TaskScore);
        Assert.NotNull(device.Document.WebSocket);
        using var pipeline = new TaskPipeline(device.Document);

        var events = new ReportStreamSocketTests.EventLog();
        var socket = device.Open(events);
        Assert.IsType<ReportStreamEvent.Opened>(await events.Next());
        Assert.IsType<ReportServiceMessage.Ready>(Message(await events.Next()));

        pipeline.CompleteATask("fix the parser", passing: true);
        var first = pipeline.Build(device, seq: 1, stream: true);
        Assert.Equal(1, Completed(first));
        Assert.True(socket.Write(device.Message(first)));
        Assert.Equal(ReportAckStatus.Accepted, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Status);
        Assert.True(socket.Write(device.Message(first)));
        Assert.Equal(ReportAckStatus.Duplicate, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Status);
        await AssertBoard(device.Document, first, completed: 1);

        pipeline.CompleteATask("fix the docs", passing: false, thenWork: true);
        var second = pipeline.Build(device, seq: 2, stream: true);
        Assert.NotEqual("none", JsonDocument.Parse(second).RootElement.GetProperty("taskScore").GetProperty("form").GetString());
        Assert.Equal(1, Completed(second));
        Assert.True(socket.Write(device.Message(second)));
        Assert.Equal(ReportAckStatus.Accepted, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Status);
        await AssertBoard(device.Document, second, completed: 2);
        socket.Close();
    }

    private static long Completed(byte[] body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("models").EnumerateArray()
            .SelectMany(m => m.GetProperty("taskStats").EnumerateArray()).Sum(r => r.GetProperty("completed").GetInt64());

    /// <summary>
    /// The server's merged model table (REPORTING-EXTENSIONS 9.6): <paramref name="completed"/> tasks in total, and the live
    /// values of the device's latest accepted report, <paramref name="latest"/>.
    /// </summary>
    private static async Task AssertBoard(ReportDiscovery document, byte[] latest, long completed)
    {
        using var http = new HttpClient();
        var board = JsonDocument.Parse(await http.GetStringAsync(new Uri(document.Api.Text + "/public/models"))).RootElement;
        var body = JsonDocument.Parse(latest).RootElement;
        Assert.Equal(completed, board.GetProperty("models").EnumerateArray().Sum(m => m.GetProperty("stats").GetProperty("completed").GetInt64()));
        var top = body.GetProperty("taskScore");
        var live = board.GetProperty("live");
        Assert.Equal(top.GetProperty("form").GetString(), live.GetProperty("form").GetString());
        Assert.Equal(top.GetProperty("score").GetRawText(), live.GetProperty("score").GetRawText());
        Assert.Equal(top.GetProperty("thinkingTokS").GetDouble(), live.GetProperty("thinkingTokS").GetDouble(), 6);
        foreach (var entry in body.GetProperty("models").EnumerateArray())
        {
            var row = board.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("id").GetString() == entry.GetProperty("id").GetString());
            var value = row.GetProperty("live");
            Assert.Equal(entry.GetProperty("taskScore").GetProperty("form").GetString(), value.GetProperty("form").GetString());
            Assert.Equal(entry.GetProperty("taskScore").GetProperty("score").GetRawText(), value.GetProperty("score").GetRawText());
            Assert.Equal(1, value.GetProperty("devices").GetInt32());
        }
    }

    private static ReportServiceMessage Message(ReportStreamEvent e) => Assert.IsType<ReportStreamEvent.Message>(e).Value;

    /// <summary>
    /// The app's path for one service: a live monitor over a Claude transcript with the task watch on, the report window and
    /// the task window of the controller, and the body builder with the document's options.
    /// </summary>
    private sealed class TaskPipeline : IDisposable
    {
        private readonly TempDirectory _temp = new();
        private readonly MonitorCore _core;
        private readonly ReportDiscovery _document;
        private readonly ReportTaskWindow _tasks = new();
        private readonly string _path;
        private readonly long _todayStart;
        private ReportWindow? _window;
        private long _clock = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60_000;
        private int _messages;

        public TaskPipeline(ReportDiscovery document)
        {
            _document = document;
            _todayStart = _clock - 3_600_000;
            var claude = _temp.Combine("claude", "projects");
            _path = Path.Combine(claude, "p", "a.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, string.Empty);
            Directory.CreateDirectory(_temp.Combine("codex", "sessions"));
            _core = new MonitorCore(MonitorConfiguration.Default, _temp.Combine("codex", "sessions"), claude, _temp.Combine("dsh"), _todayStart)
            {
                Clock = () => _clock,
            };
            _core.Seed(_clock, _todayStart, CancellationToken.None);
            Assert.True(_tasks.Update(on: true, "service"));
            _core.TaskWatch = _tasks.Watch;
        }

        /// <summary>
        /// A prompt with an edit and a test run, then the next prompt settles it: one watched task completes. With
        /// <paramref name="thenWork"/> the next task edits too, so the agent has a live score (that task never completes here).
        /// </summary>
        public void CompleteATask(string prompt, bool passing, bool thenWork = false)
        {
            var output = passing ? "test result: ok. 3 passed; 0 failed; 0 ignored" : "test result: FAILED. 2 passed; 1 failed; 0 ignored";
            Append(ClaudeLines.UserText(Tick(), prompt), Call(Tick(), "Edit", """{"file_path":"/w/src/a.rs","old_string":"a","new_string":"b"}"""),
                Result(Tick(), "ok", error: false), Call(Tick(), "Bash", """{"command":"cargo test"}"""), Result(Tick(), output, !passing),
                ClaudeLines.UserText(Tick(), "next: " + prompt), ClaudeLines.Assistant(Tick(), "m" + ++_messages, 30));
            if (thenWork)
            {
                Append(Call(Tick(), "Edit", """{"file_path":"/w/docs/a.md","old_string":"a","new_string":"b"}"""), Result(Tick(), "ok", error: false));
            }
        }

        public byte[] Build(TaskDevice device, int seq, bool stream = false)
        {
            var sample = ReportSample.From(_core.BuildSnapshot(_clock, _todayStart, MonitorState.Running, observeLive: true).Snapshot,
                ReportTrafficSample.Off);
            _window ??= new ReportWindow(sample);
            var content = _window.Take(sample) with { Tasks = _tasks.Take(sample) };
            _core.TaskWatch = _tasks.Watch;
            var envelope = new ReportEnvelope(Client, device.DeviceId, device.Username, device.RunId, seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                5000, ReportActivity.Active, 2);
            var options = new ReportOptions(_document.Sections, _document.Limits.MaxModels, _document.Limits.SendLimitBytes)
            {
                Extensions = _document.Extensions,
                Stream = stream ? new ReportStreamBody(30, _document.WebSocket!.MaxMessageBytes) : null,
            };
            Assert.True(options.TaskScoreActive);
            return ReportBody.Data(envelope, content, sample, options);
        }

        public void Dispose()
        {
            _core.Dispose();
            _temp.Dispose();
        }

        private long Tick() => _clock += 1000;

        private void Append(params string[] lines)
        {
            File.AppendAllText(_path, string.Concat(lines.Select(l => l + "\n")));
            Assert.True(_core.ProcessPaths([_path], _clock, _todayStart));
        }

        private string Call(long ms, string tool, string inputJson)
        {
            var id = "call" + ++_messages;
            _lastCall = id;
            return ClaudeLines.Assistant(ms, "m" + _messages, 10, "\"tool_use\"").Replace(
                """[{"type":"text","text":"x"}]""",
                $$$"""[{"type":"tool_use","id":"{{{id}}}","name":"{{{tool}}}","input":{{{inputJson}}} }]""", StringComparison.Ordinal);
        }

        private string _lastCall = string.Empty;

        private string Result(long ms, string content, bool error) =>
            ClaudeLines.User(ms, $$"""[{"type":"tool_result","tool_use_id":"{{_lastCall}}","is_error":{{(error ? "true" : "false")}},"content":"{{content}}"} ]""");
    }

    /// <summary>A registered device of one run, sending over HTTP or writing <c>report</c> messages.</summary>
    private sealed class TaskDevice(ReportDiscovery document, string username)
    {
        private readonly SoftwareReportSigner _signer = new();

        public ReportDiscovery Document { get; } = document;

        public string DeviceId { get; } = ReportCanonical.NewUuid();

        public string Username { get; } = username;

        public string RunId { get; } = ReportCanonical.NewUuid();

        private ReportRequestBuilder Builder => new(Client.UserAgent, DeviceId, null);

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static async Task<TaskDevice> Register(ReportApi api, ReferenceServer server, string username)
        {
            var document = Assert.IsType<ReportDiscoveryOutcome.Document>(await api.DiscoverAsync(server.Service)).Discovery;
            var device = new TaskDevice(document, username);
            var request = device.Builder.Make("POST", document.Api.Endpoint(ReportEndpoint.Devices),
                ReportDocuments.Registration(device.DeviceId, username, device._signer.PublicKey, Client), device._signer, Now);
            Assert.Equal("created", (await api.CallAsync(request)).Reply?.Result);
            return device;
        }

        public async Task<ReportReply> Post(ReportApi api, byte[] body)
        {
            var result = await api.CallAsync(Builder.Make("POST", Document.Api.Endpoint(ReportEndpoint.Reports), body, _signer, Now));
            Assert.True(result.HasReply, result.Error?.Reason);
            return result.Reply!;
        }

        public IReportStreamSocket Open(ReportStreamSocketTests.EventLog events) =>
            new WebSocketReportTransport().Open(ReportWebSocketProtocol.Handshake(Builder, Document.Api, _signer, Now), Document.WebSocket!, events.Add);

        public string Message(byte[] body) =>
            ReportWebSocketProtocol.ReportMessage(Builder.Make("POST", Document.Api.Endpoint(ReportEndpoint.Reports), body, _signer, Now).Headers, body);
    }
}
