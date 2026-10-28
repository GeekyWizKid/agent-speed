using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary>Groups <c>codeScript</c> and <c>codeItems</c> (TASK-PARSERS 4.4).</summary>
public sealed partial class TaskParserCaseTests
{
    public static TheoryData<string> CodeScriptIds() => Ids("codeScript");

    public static TheoryData<string> CodeItemsIds() => Ids("codeItems");

    [Theory]
    [MemberData(nameof(CodeScriptIds))]
    public void CodeScript(string id)
    {
        var item = Case(id);
        var got = TaskParser.CodeScript(Source(item), Tool(item), Member(item, "input"));
        var expect = item.GetProperty("expect");
        if (expect.ValueKind == JsonValueKind.Null)
        {
            Assert.True(got is null, id + ": expected no script");
            return;
        }

        Assert.True(got is not null, id + ": expected a script");
        var calls = expect.GetProperty("calls");
        Assert.True(calls.GetArrayLength() == got.Calls.Count, id + ": " + got.Calls.Count + " calls");
        var k = 0;
        foreach (var call in calls.EnumerateArray())
        {
            var actual = got.Calls[k];
            Assert.True(call.GetProperty("tool").GetString() == actual.Tool, id + ": call " + k + " tool " + actual.Tool);
            Assert.True(call.GetProperty("digest").GetString() == actual.Digest, id + ": call " + k + " digest " + actual.Digest);
            var input = call.GetProperty("input");
            var inputMatches = input.ValueKind == JsonValueKind.Null
                ? actual.Input is null || actual.Input.Value.ValueKind == JsonValueKind.Null
                : actual.Input is { } value && JsonEquals(input, value);
            Assert.True(inputMatches, id + ": call " + k + " input " + actual.Input?.GetRawText());
            k++;
        }

        Assert.True(expect.GetProperty("plain").GetBoolean() == got.Plain, id + ": plain " + got.Plain);
        var prints = expect.GetProperty("prints");
        Assert.True(prints.GetArrayLength() == got.Prints.Count, id + ": " + got.Prints.Count + " prints");
        k = 0;
        foreach (var print in prints.EnumerateArray())
        {
            var actual = got.Prints[k];
            Assert.True(print[0].GetInt32() == actual.Call && print[1].GetString() == TaskWire.Name(actual.Mode), id + ": print " + k + " " + actual);
            k++;
        }
    }

    [Theory]
    [MemberData(nameof(CodeItemsIds))]
    public void CodeItems(string id)
    {
        var item = Case(id);
        var got = TaskParser.CodeItems(Source(item), Member(item, "result"));
        var expect = item.GetProperty("expect");
        if (expect.ValueKind == JsonValueKind.Null)
        {
            Assert.True(got is null, id + ": expected no items");
            return;
        }

        Assert.True(got is not null, id + ": expected items");
        Assert.True(expect.GetArrayLength() == got.Count, id + ": " + got.Count + " items");
        var k = 0;
        foreach (var text in expect.EnumerateArray())
        {
            Assert.True(OptionalString(text) == got[k], id + ": item " + k + " " + JsonSerializer.Serialize(got[k]));
            k++;
        }
    }
}
