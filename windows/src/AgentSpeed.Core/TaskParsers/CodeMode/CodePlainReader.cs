namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The <i>plain</i> reading of a code-mode script (TASK-PARSERS 4.4): only <c>;</c> and the statements <c>const</c> /
/// <c>let</c> / <c>var</c> NAME <c>=</c> CALL or LITERAL, CALL alone, and the printing statements <c>text(…)</c>, each
/// CALL being <c>await tools.NAME(…)</c>.
/// </summary>
internal sealed class CodePlainReader
{
    private readonly JsTokens toks;
    private readonly int[] callAt;
    private readonly IReadOnlyList<int> closes;
    private readonly Dictionary<string, JsLiteral> env = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> bound = new(StringComparer.Ordinal);

    /// <param name="toks">The script's tokens.</param>
    /// <param name="callAt">The call index of each <c>tools</c> token that starts a call, else -1.</param>
    /// <param name="closes">The closing parenthesis of each call.</param>
    public CodePlainReader(JsTokens toks, int[] callAt, IReadOnlyList<int> closes)
    {
        this.toks = toks;
        this.callAt = callAt;
        this.closes = closes;
        Inputs = new JsLiteral?[closes.Count];
        Refreshed = new bool[closes.Count];
    }

    /// <summary>The printing statements in order, once <see cref="Read"/> returned true.</summary>
    public List<CodePrint> Prints { get; } = [];

    /// <summary>The inputs of the awaited calls, read with the literals bound before them.</summary>
    public JsLiteral?[] Inputs { get; }

    /// <summary>Which calls <see cref="Inputs"/> holds.</summary>
    public bool[] Refreshed { get; }

    /// <summary>Whether the script is plain.</summary>
    public bool Read()
    {
        var n = toks.Count;
        var p = 0;
        while (p < n)
        {
            if (toks.IsPunct(p, ';'))
            {
                p++;
                continue;
            }

            if (toks[p] is { Kind: JsTokenKind.Identifier, Text: "const" or "let" or "var" } && p + 2 < n
                && toks[p + 1].Kind == JsTokenKind.Identifier && toks.IsPunct(p + 2, '='))
            {
                var name = toks[p + 1].Text!;
                var index = Awaited(p + 3, out var q);
                if (index >= 0)
                {
                    bound[name] = index;
                    env.Remove(name);
                }
                else
                {
                    if (JsLiteralReader.Read(toks, p + 3, n, env, out q) is not { } literal)
                    {
                        return Fail();
                    }

                    env[name] = literal;
                    bound.Remove(name);
                }

                p = q;
                continue;
            }

            if (toks.IsId(p, "text") && toks.IsPunct(p + 1, '('))
            {
                var q = p + 2;
                var mode = CodePrintMode.Result;
                var index = Awaited(q, out var after);
                if (index < 0 && toks.IsPunct(q, '('))
                {
                    index = Awaited(q + 1, out after);
                    if (index >= 0 && toks.IsPunct(after, ')') && toks.IsPunct(after + 1, '.') && toks.IsId(after + 2, "output"))
                    {
                        mode = CodePrintMode.Output;
                        after += 3;
                    }
                    else
                    {
                        index = -1;
                    }
                }

                if (index < 0 && q < n && toks[q].Kind == JsTokenKind.Identifier && bound.TryGetValue(toks[q].Text!, out var boundIndex))
                {
                    index = boundIndex;
                    after = q + 1;
                    if (toks.IsPunct(after, '.') && toks.IsId(after + 1, "output"))
                    {
                        mode = CodePrintMode.Output;
                        after += 2;
                    }
                }

                if (index < 0 || !toks.IsPunct(after, ')'))
                {
                    return Fail();
                }

                Prints.Add(new CodePrint(index, mode));
                p = after + 1;
                continue;
            }

            if (Awaited(p, out var next) < 0)
            {
                return Fail();
            }

            p = next;
        }

        return true;
    }

    /// <summary>
    /// The call of <c>await tools.NAME(…)</c> at token <paramref name="i"/>, its input refreshed with the bindings so
    /// far, and the index after it; -1 (and <paramref name="next"/> = <paramref name="i"/>) when there is none.
    /// </summary>
    private int Awaited(int i, out int next)
    {
        next = i;
        if (!toks.IsId(i, "await") || i + 1 >= toks.Count || callAt[i + 1] < 0)
        {
            return -1;
        }

        var index = callAt[i + 1];
        var close = closes[index];
        Inputs[index] = CodeModeParser.CallInput(toks, i + 1, close, env);
        Refreshed[index] = true;
        next = close + 1;
        return index;
    }

    private bool Fail()
    {
        Prints.Clear();
        return false;
    }
}
