using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary>A result's first line read from the raw text equals the first line of the whole decoded text (TASK-PARSERS 4.1).</summary>
public sealed class ResultFirstLineTests
{
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"Exit code 1\"")]
    [InlineData("\"Exit code 2\\nrest of the output\"")]
    [InlineData("\"first\\r\\nsecond\"")]
    [InlineData("\"a \\\"quoted\\\" \\\\ path\\tand tab\\nnext\"")]
    [InlineData("\"caf\\u00e9 \\u001b[31mred\\nnext\"")]
    [InlineData("\"line\\u000anext\"")]
    [InlineData("\"bad \\ud800 surrogate\\nnext\"")]
    [InlineData("\"     1→use std::io;\\n     2→fn main() {}\"")]
    [InlineData("\"日本語の出力\\n二行目\"")]
    [InlineData("\"trailing backslash \\\\\"")]
    [InlineData("[{\"type\":\"text\",\"text\":\"Command running in background with ID: b1\\nmore\"},{\"type\":\"text\",\"text\":\"x\"}]")]
    [InlineData("[{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"second block\\nmore\"}]")]
    [InlineData("[{\"type\":\"text\",\"text\":5},{\"type\":\"output_text\",\"text\":\"ok\"}]")]
    public void TheShortcutEqualsTheWholeText(string content)
    {
        var element = JsonDocument.Parse(content).RootElement;
        var text = ResultBlocks.Text(element).Split('\n')[0];
        var whole = element.ValueKind == JsonValueKind.Array ? FirstBlock(element) : TaskJsonForTests(element);
        var cut = whole.AsSpan().IndexOfAny('\r', '\n');
        var expected = cut < 0 ? whole : whole[..cut];
        Assert.Equal(expected, ResultBlocks.FirstLine(element));
        Assert.StartsWith(expected, text, StringComparison.Ordinal);
    }

    private static string FirstBlock(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var type) && type.GetString() is "text" or "input_text" or "output_text"
                && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return TaskJsonForTests(text);
            }
        }

        return "";
    }

    private static string TaskJsonForTests(JsonElement value) => TaskJson.String(value) ?? "";
}
