using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary><c>failures</c> (TASK-PARSERS 5) against the <c>failures</c> cases: the canonical strings first, then the report.</summary>
public sealed partial class TaskParserCaseTests
{
    public static TheoryData<string> FailureIds() => Ids("failures");

    [Theory]
    [MemberData(nameof(FailureIds))]
    public void FailuresMatch(string id)
    {
        var item = Case(id);
        var cwd = Cwd(item);
        var output = item.GetProperty("output").GetString() ?? "";
        var failed = Member(item, "failed") is { ValueKind: JsonValueKind.True or JsonValueKind.False } f ? f.GetBoolean() : (bool?)null;
        var expect = item.GetProperty("expect");

        // As the reference: the case's own info when it has one, else command_info of its command.
        var info = Member(item, "info") is { ValueKind: JsonValueKind.Object } given
            ? new CommandInfo(
                CommandClass.Other,
                Member(given, "suite") is { ValueKind: JsonValueKind.String } s ? s.GetString() : null,
                false,
                TaskText.EmptyDigest,
                false,
                false,
                Member(given, "errorFilter").ValueKind == JsonValueKind.True)
            : ShellCommands.Info(Member(item, "command") is { ValueKind: JsonValueKind.String } c ? c.GetString()! : "", cwd);
        var (report, canonical) = FailureParser.Detail(info, output, cwd, failed);

        var expectedCanonical = expect.GetProperty("canonical").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.True(
            expectedCanonical.SequenceEqual(canonical),
            id + " canonical:\n  expected " + string.Join("\n           ", expectedCanonical) + "\n  actual   " + string.Join("\n           ", canonical));

        var expectedFingerprints = expect.GetProperty("fingerprints").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.True(expectedFingerprints.SequenceEqual(report.Fingerprints), id + " fingerprints: " + string.Join(",", report.Fingerprints));
        Assert.True(OptionalLong(expect.GetProperty("failedCount")) == report.FailedCount, id + " failedCount: " + report.FailedCount);
        Assert.True(OptionalLong(expect.GetProperty("passedCount")) == report.PassedCount, id + " passedCount: " + report.PassedCount);
        Assert.True(OptionalString(expect.GetProperty("framework")) == report.Framework, id + " framework: " + report.Framework);

        var plain = TaskParser.Failures(info, output, cwd, failed);
        Assert.Equal(report.Fingerprints, plain.Fingerprints);
        Assert.Equal((report.FailedCount, report.PassedCount, report.Framework), (plain.FailedCount, plain.PassedCount, plain.Framework));
    }

    // Expected values below come from spec/tools/task_parsers.py.
    [Fact]
    public void FailureBudgetCutsOnCharactersAndLines()
    {
        var text = string.Concat(Enumerable.Repeat(new string('a', 99) + "\n", 1000)) + new string('中', 70000) + "\n"
            + string.Concat(Enumerable.Repeat(new string('b', 50) + "é\n", 2000));
        var segments = FailureBudget.Segments(text);
        Assert.Equal([65500, 104000], segments.Select(s => s.Length));
        Assert.EndsWith("a\n", segments[0], StringComparison.Ordinal);
        Assert.StartsWith("bbb", segments[1], StringComparison.Ordinal);

        var astral = new string('x', 65535) + "\U0001F600\n" + new string('y', 200000);
        Assert.Equal([new string('x', 65535), new string('y', 196608)], FailureBudget.Segments(astral));
    }

    [Fact]
    public void FailureMaskKeepsNamesAndDropsVolatileParts()
    {
        Assert.Equal(
            @"src\a.cs(N): error CS0103: x at <time> took <dur> pid N",
            FailureMask.Mask(@"C:\USERS\dev\proj\src\a.cs(12,5): error CS0103: x at 12:30:01 took 1.5s pid 42", "c:/users/DEV/proj"));
        Assert.Equal(
            "/opt/mirror/home/dev/orbit/x.py:N y.py:N <tmp> 0x?",
            FailureMask.Mask("/opt/mirror/home/dev/orbit/x.py:3 /home/dev/orbit/y.py:4 /tmp/pytest-of-dev/z 0xdeadbeef", "/home/dev/orbit"));
        Assert.Equal("test_a with ~/x", FailureMask.MaskName("test_a  \t with /home/bob/x", "/home/dev/orbit"));
        Assert.Equal("src/a.py", FailureMask.NormFile("'file://./src//a.py'", null));
        Assert.Equal("src/a.cs", FailureMask.NormFile(@" C:\Users\dev\proj\src\a.cs", "c:/users/DEV/proj"));
    }

    [Fact]
    public void FailureTextReadsLoneSurrogatesAsReplacementCharacters()
    {
        var (report, canonical) = FailureParser.Detail(null, "error: bad \ud800 thing\n", null, true);
        Assert.Equal(["generic|error|||bad \ufffd thing"], canonical);
        Assert.Equal(["4cf416d08b73f03f"], report.Fingerprints);
        Assert.Equal((1L, (long?)null, "generic"), (report.FailedCount.GetValueOrDefault(), report.PassedCount, report.Framework));
    }

    [Fact]
    public void FailureCountsHoldAtTheLargestLong()
    {
        // The reference adds without bound (9 999 999 999 999 999 990 here); a long holds at its maximum.
        var line = "test result: FAILED. 0 passed; 999999999999999999 failed; 0 ignored; 0 measured; 0 filtered out\n";
        var report = FailureParser.Failures(null, string.Concat(Enumerable.Repeat(line, 10)), null, true);
        Assert.Equal((long.MaxValue, 0L, "cargo"), (report.FailedCount.GetValueOrDefault(), report.PassedCount.GetValueOrDefault(), report.Framework));
    }
}
