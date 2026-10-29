using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// A history worker's own compiled pattern copies (<see cref="ThreadRegex"/>) read every failures and outcome case of
/// <c>task-parser-cases.json</c> exactly as the shared source-generated patterns do.
/// </summary>
public sealed class ThreadRegexTests
{
    [Fact]
    public void OwnCopiesGiveTheSameFailuresAndOutcomes()
    {
        var failures = TaskParserCaseTests.Ids("failures").Cast<object[]>().Select(row => (string)row[0]).ToList();
        var outcomes = TaskParserCaseTests.Ids("outcome").Cast<object[]>().Select(row => (string)row[0]).ToList();
        Assert.NotEmpty(failures);
        Assert.NotEmpty(outcomes);
        var shared = Read(failures, outcomes);
        List<string>? copied = null;
        var worker = new Thread(() =>
        {
            ThreadRegex.UseOwnCopies();
            copied = Read(failures, outcomes);
        });
        worker.Start();
        worker.Join();
        Assert.Equal(shared, copied);
    }

    private static List<string> Read(List<string> failures, List<string> outcomes)
    {
        var lines = new List<string>();
        foreach (var id in failures)
        {
            var item = TaskParserCaseTests.Case(id);
            var cwd = TaskParserCaseTests.Cwd(item);
            var command = TaskParserCaseTests.Member(item, "command") is { ValueKind: JsonValueKind.String } c ? c.GetString()! : "";
            var failed = TaskParserCaseTests.Member(item, "failed") is { ValueKind: JsonValueKind.True or JsonValueKind.False } f ? f.GetBoolean() : (bool?)null;
            var (report, canonical) = FailureParser.Detail(ShellCommands.Info(command, cwd), item.GetProperty("output").GetString() ?? "", cwd, failed);
            lines.Add($"{id} {string.Join(',', report.Fingerprints)} {report.FailedCount} {report.PassedCount} {report.Framework} {string.Join('|', canonical)}");
        }

        foreach (var id in outcomes)
        {
            var item = TaskParserCaseTests.Case(id);
            var source = TaskParserCaseTests.Source(item);
            var result = TaskParserCaseTests.Member(item, "result");
            var outcome = TaskParser.Outcome(source, result, 1, TaskParserCaseTests.Tool(item), TaskParserCaseTests.Member(item, "input"),
                TaskParserCaseTests.Cwd(item));
            lines.Add($"{id} {outcome} {TaskParser.ResultText(source, result)} {TaskParser.RunningSession(source, result)}");
        }

        return lines;
    }
}
