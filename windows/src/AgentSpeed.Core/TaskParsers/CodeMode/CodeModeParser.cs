using System.Buffers;
using System.Text;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Codex code mode (TASK-PARSERS 4.4): the tool calls and printing statements of a JavaScript cell run by the
/// <c>exec</c> custom tool, and the printed results of a cell that completed.
/// </summary>
internal static class CodeModeParser
{
    private const string Completed = "Script completed";
    private const int StackLimit = 1024;

    /// <summary>
    /// <c>code_script</c>: for Codex <c>exec</c> with a string input that is not a JSON object, its calls in source
    /// order, whether it is plain and its printing statements; null when it cannot be tokenised or calls no tool.
    /// </summary>
    public static CodeScript? Script(TaskFamily family, string? tool, JsonElement input)
    {
        if (family != TaskFamily.Codex || tool != "exec" || TaskJson.String(input) is not { } text || TaskJson.AsObject(input) is not null)
        {
            return null;
        }

        if (JsLexer.Tokenize(text) is not { } toks)
        {
            return null;
        }

        int[]? callAt = null;
        var starts = new List<int>();
        var closes = new List<int>();
        for (var k = 0; k < toks.Count; k++)
        {
            if (!toks.IsId(k, "tools") || (k > 0 && toks.IsPunct(k - 1, '.')))
            {
                continue;
            }

            if (!(k + 3 < toks.Count && toks.IsPunct(k + 1, '.') && toks[k + 2].Kind == JsTokenKind.Identifier && toks.IsPunct(k + 3, '(')))
            {
                continue;
            }

            var close = toks.Close(k + 3);
            if (close < 0)
            {
                continue;
            }

            if (callAt is null)
            {
                callAt = new int[toks.Count];
                Array.Fill(callAt, -1);
            }

            callAt[k] = starts.Count;
            starts.Add(k);
            closes.Add(close);
        }

        if (callAt is null)
        {
            return null;
        }

        var plain = new CodePlainReader(toks, callAt, closes);
        var isPlain = plain.Read();
        var calls = new CodeCall[starts.Count];
        for (var c = 0; c < calls.Length; c++)
        {
            var k = starts[c];
            var close = closes[c];
            var literal = isPlain && plain.Refreshed[c] ? plain.Inputs[c] : CallInput(toks, k, close, null);
            var start = toks[k].Start;
            calls[c] = new CodeCall(toks[k + 2].Text!, literal?.ToElement(), Sha16(text.AsSpan(start, toks[close].End - start)));
        }

        return new CodeScript(calls, isPlain, isPlain ? plain.Prints : []);
    }

    /// <summary>
    /// <c>code_items</c>: the texts printed by a script whose output's first item reads <c>Script completed</c>,
    /// LF-normalised (null for an item without a string <c>text</c>); null for anything else.
    /// </summary>
    public static IReadOnlyList<string?>? Items(TaskFamily family, JsonElement result)
    {
        if (family != TaskFamily.Codex || result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var output = TaskJson.Has(result, "type") && TaskJson.Has(result, "output") ? TaskJson.Get(result, "output") : result;
        if (output.ValueKind != JsonValueKind.Array || output.GetArrayLength() == 0)
        {
            return null;
        }

        var items = new string?[output.GetArrayLength() - 1];
        var k = -1;
        foreach (var item in output.EnumerateArray())
        {
            var text = TaskJson.GetString(item, "text");
            if (k < 0)
            {
                if (text is null)
                {
                    return null;
                }

                var cut = text.AsSpan().IndexOfAny('\r', '\n');
                if (!(cut < 0 ? text.AsSpan() : text.AsSpan(0, cut)).SequenceEqual(Completed))
                {
                    return null;
                }
            }
            else
            {
                items[k] = text is null ? null : TaskText.Lf(text);
            }

            k++;
        }

        return items;
    }

    /// <summary>The single literal argument of the call <c>tools . NAME ( … )</c> at token <paramref name="k"/>, or null.</summary>
    internal static JsLiteral? CallInput(JsTokens toks, int k, int close, IReadOnlyDictionary<string, JsLiteral>? env)
    {
        if (close == k + 4)
        {
            return null;
        }

        var literal = JsLiteralReader.Read(toks, k + 4, close, env, out var after);
        return after == close ? literal : null;
    }

    private static string Sha16(ReadOnlySpan<char> text)
    {
        var max = Encoding.UTF8.GetMaxByteCount(text.Length);
        byte[]? rented = null;
        var buffer = max <= StackLimit ? stackalloc byte[max] : (rented = ArrayPool<byte>.Shared.Rent(max));
        try
        {
            return TaskText.Sha16(buffer[..Encoding.UTF8.GetBytes(text, buffer)]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
