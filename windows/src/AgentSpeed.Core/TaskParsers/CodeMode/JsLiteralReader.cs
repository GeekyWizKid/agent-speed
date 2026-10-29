namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Literals of a code-mode script (TASK-PARSERS 4.4): a string with a known value, a valued number (with an optional
/// leading <c>-</c>), <c>true</c>, <c>false</c>, <c>null</c>, an array of literals and an object whose keys are
/// identifiers, strings or integers; a member whose value is not a literal is left out.
/// </summary>
internal static class JsLiteralReader
{
    /// <summary>
    /// The literal at token <paramref name="p"/> before <paramref name="end"/> and the index after it, or null (and
    /// <paramref name="next"/> = <paramref name="p"/>) when there is none. <paramref name="env"/> holds the names a
    /// plain script bound to literals.
    /// </summary>
    public static JsLiteral? Read(JsTokens toks, int p, int end, IReadOnlyDictionary<string, JsLiteral>? env, out int next) =>
        Read(toks, p, end, env, 0, out next);

    private static JsLiteral? Read(JsTokens toks, int p, int end, IReadOnlyDictionary<string, JsLiteral>? env, int depth, out int next)
    {
        next = p;
        if (p >= end)
        {
            return null;
        }

        var t = toks[p];
        if (t.Kind == JsTokenKind.Identifier && env is not null && env.TryGetValue(t.Text!, out var bound))
        {
            next = p + 1;
            return bound;
        }

        switch (t.Kind)
        {
            case JsTokenKind.String or JsTokenKind.Number when t.Text is null:
                return null;
            case JsTokenKind.String:
                next = p + 1;
                return JsLiteral.String(t.Text!);
            case JsTokenKind.Number:
                next = p + 1;
                return JsLiteral.Number(t.Text!, negative: false);
        }

        if (toks.IsPunct(p, '-') && p + 1 < end && toks[p + 1] is { Kind: JsTokenKind.Number, Text: { } raw })
        {
            next = p + 2;
            return JsLiteral.Number(raw, negative: true);
        }

        if (t.Kind == JsTokenKind.Identifier)
        {
            var constant = t.Text switch
            {
                "true" => JsLiteral.True,
                "false" => JsLiteral.False,
                "null" => JsLiteral.Null,
                _ => null,
            };
            if (constant is not null)
            {
                next = p + 1;
            }

            return constant;
        }

        if (toks.IsPunct(p, '{') || toks.IsPunct(p, '['))
        {
            var close = toks.Close(p);
            if (close < 0 || close >= end || depth >= JsLiteral.MaxDepth)
            {
                return null;
            }

            JsLiteral? container = t.Char == '{' ? ReadObject(toks, p, close, env, depth) : ReadArray(toks, p, close, env, depth);
            if (container is null || container.Depth > JsLiteral.MaxDepth)
            {
                return null;
            }

            next = close + 1;
            return container;
        }

        return null;
    }

    private static JsObjectLiteral ReadObject(JsTokens toks, int p, int close, IReadOnlyDictionary<string, JsLiteral>? env, int depth)
    {
        var obj = new JsObjectLiteral();
        var q = p + 1;
        while (q < close)
        {
            var stop = q;
            var nested = 0;
            while (stop < close && !(nested == 0 && toks.IsPunct(stop, ',')))
            {
                if (toks.IsOpen(stop))
                {
                    // The depth first comes back to this level at the bracket's close, so nothing between can stop
                    // the member: skipping there keeps nested objects linear instead of rescanning them per level.
                    var inner = toks.Close(stop);
                    if (nested == 0 && inner > stop && inner < close)
                    {
                        stop = inner + 1;
                        continue;
                    }

                    nested++;
                }
                else if (toks.IsClosing(stop))
                {
                    nested--;
                }

                stop++;
            }

            var keyToken = toks[q];
            var key = keyToken.Kind switch
            {
                JsTokenKind.Identifier or JsTokenKind.String => keyToken.Text,
                JsTokenKind.Number when keyToken.Text is { } raw && raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0 => IntegerKey(raw),
                _ => null,
            };
            if (key is not null && q + 1 < stop && toks.IsPunct(q + 1, ':')
                && Read(toks, q + 2, stop, env, depth + 1, out var after) is { } value && after == stop)
            {
                obj.Set(key, value);
            }

            q = stop + 1;
        }

        return obj;
    }

    private static JsArrayLiteral? ReadArray(JsTokens toks, int p, int close, IReadOnlyDictionary<string, JsLiteral>? env, int depth)
    {
        var items = new List<JsLiteral>();
        var q = p + 1;
        while (q < close)
        {
            var value = Read(toks, q, close, env, depth + 1, out var after);
            if (value is null || (after < close && !toks.IsPunct(after, ',')))
            {
                return null;
            }

            items.Add(value);
            q = after + 1;
        }

        return new JsArrayLiteral(items);
    }

    /// <summary><c>str(int(raw))</c>: the digits without leading zeros.</summary>
    private static string IntegerKey(string raw)
    {
        var digits = raw.TrimStart('0');
        return digits.Length == 0 ? "0" : digits;
    }
}
