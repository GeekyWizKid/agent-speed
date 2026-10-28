using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary>Groups <c>outcome</c>, <c>resultText</c>, <c>session</c> and <c>notification</c> (TASK-PARSERS 4).</summary>
public sealed partial class TaskParserCaseTests
{
    public static TheoryData<string> OutcomeIds() => Ids("outcome");

    public static TheoryData<string> ResultTextIds() => Ids("resultText");

    public static TheoryData<string> SessionIds() => Ids("session");

    public static TheoryData<string> NotificationIds() => Ids("notification");

    [Theory]
    [MemberData(nameof(OutcomeIds))]
    public void Outcome(string id)
    {
        var item = Case(id);
        var t = Member(item, "t");
        var got = ResultParser.Outcome(
            TaskFamilies.Of(Source(item)),
            Member(item, "result"),
            t.ValueKind == JsonValueKind.Number ? t.GetInt64() : null,
            Tool(item),
            Member(item, "input"),
            Cwd(item));
        var expect = item.GetProperty("expect");
        Assert.True(OptionalLong(expect.GetProperty("exitCode")) == got.ExitCode, id + ": exitCode " + got.ExitCode);
        Assert.True(OptionalBool(expect.GetProperty("failed")) == got.Failed, id + ": failed " + got.Failed);
        Assert.True(OptionalLong(expect.GetProperty("t")) == got.T, id + ": t " + got.T);
    }

    [Theory]
    [MemberData(nameof(ResultTextIds))]
    public void ResultText(string id)
    {
        var item = Case(id);
        var got = ResultParser.ResultText(TaskFamilies.Of(Source(item)), Member(item, "result"));
        Assert.True(item.GetProperty("expect").GetString() == got, id + ": " + JsonSerializer.Serialize(got));
    }

    [Theory]
    [MemberData(nameof(SessionIds))]
    public void Session(string id)
    {
        var item = Case(id);
        var family = TaskFamilies.Of(Source(item));
        var continuation = ResultParser.Continuation(family, Tool(item), Member(item, "input"));
        var running = item.TryGetProperty("result", out var result) ? ResultParser.RunningSession(family, result) : null;
        var expect = item.GetProperty("expect");
        Assert.True(OptionalString(expect.GetProperty("continuation")) == continuation, id + ": continuation " + continuation);
        Assert.True(OptionalString(expect.GetProperty("running")) == running, id + ": running " + running);
    }

    [Theory]
    [MemberData(nameof(NotificationIds))]
    public void Notification(string id)
    {
        var item = Case(id);
        var got = ResultParser.Notification(TaskFamilies.Of(Source(item)), Member(item, "line"));
        var expect = item.GetProperty("expect");
        if (expect.ValueKind == JsonValueKind.Null)
        {
            Assert.True(got is null, id + ": expected no notice, got " + got);
            return;
        }

        Assert.True(got is not null, id + ": expected a notice");
        Assert.True(expect.GetProperty("session").GetString() == got.Value.Session, id + ": session " + got.Value.Session);
        Assert.True(expect.GetProperty("exitCode").GetInt64() == got.Value.ExitCode, id + ": exitCode " + got.Value.ExitCode);
    }
}
