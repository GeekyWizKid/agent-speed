using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary>Group <c>edits</c> (TASK-PARSERS 6).</summary>
public sealed partial class TaskParserCaseTests
{
    public static TheoryData<string> EditsIds() => Ids("edits");

    [Theory]
    [MemberData(nameof(EditsIds))]
    public void Edits(string id)
    {
        var item = Case(id);
        var got = TaskParser.EditHunks(Source(item), Tool(item), Member(item, "input"), Cwd(item));
        var expect = item.GetProperty("expect");
        Assert.True(expect.GetArrayLength() == got.Count, id + ": " + got.Count + " hunks");
        var k = 0;
        foreach (var hunk in expect.EnumerateArray())
        {
            var actual = got[k];
            Assert.True(hunk.GetProperty("file").GetString() == actual.File, id + ": hunk " + k + " file " + actual.File);
            Assert.True(OptionalString(hunk.GetProperty("old")) == actual.Old, id + ": hunk " + k + " old " + actual.Old);
            Assert.True(hunk.GetProperty("new").GetString() == actual.New, id + ": hunk " + k + " new " + actual.New);
            k++;
        }
    }
}
