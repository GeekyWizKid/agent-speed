namespace AgentSpeed.Core.TaskParsers;

internal enum JsTokenKind : byte
{
    Number,
    String,
    Regex,
    Identifier,
    Punctuator,
}

/// <summary>
/// One token of a code-mode script (TASK-PARSERS 4.4) over <c>[Start, End)</c> of the script. <see cref="Text"/> is an
/// identifier's name, a string's value (null for a template holding <c>${…}</c>) or a number's raw text when it is
/// valued (<c>JS_NUMBER</c>); <see cref="Char"/> is a punctuator.
/// </summary>
internal readonly record struct JsToken(JsTokenKind Kind, int Start, int End, string? Text, char Char);

/// <summary>The tokens of a script, with the closing bracket of every opening one.</summary>
internal sealed class JsTokens
{
    private readonly List<JsToken> items;
    private readonly int[] closes;

    public JsTokens(string text, List<JsToken> items)
    {
        Text = text;
        this.items = items;
        closes = Closes(items);
    }

    public string Text { get; }

    public int Count => items.Count;

    public JsToken this[int k] => items[k];

    /// <summary>
    /// The index of the token that closes the bracket at <paramref name="k"/> by depth over <c>([{</c> and <c>)]}</c>
    /// (any closing bracket closes any opening one), or -1.
    /// </summary>
    public int Close(int k) => closes[k];

    public bool IsPunct(int k, char c) => k < items.Count && items[k].Kind == JsTokenKind.Punctuator && items[k].Char == c;

    public bool IsId(int k, string name) => k < items.Count && items[k].Kind == JsTokenKind.Identifier && items[k].Text == name;

    public bool IsOpen(int k) => items[k].Kind == JsTokenKind.Punctuator && items[k].Char is '(' or '[' or '{';

    public bool IsClosing(int k) => items[k].Kind == JsTokenKind.Punctuator && items[k].Char is ')' or ']' or '}';

    // One pass with a stack gives what a depth count from each opening bracket finds: the first later token at which
    // the depth returns to zero.
    private static int[] Closes(List<JsToken> items)
    {
        var closes = new int[items.Count];
        Array.Fill(closes, -1);
        var open = new Stack<int>();
        for (var k = 0; k < items.Count; k++)
        {
            var t = items[k];
            if (t.Kind != JsTokenKind.Punctuator)
            {
                continue;
            }

            if (t.Char is '(' or '[' or '{')
            {
                open.Push(k);
            }
            else if (t.Char is (')' or ']' or '}') && open.Count > 0)
            {
                closes[open.Pop()] = k;
            }
        }

        return closes;
    }
}
