using System.Text;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The tokens of a code-mode script (TASK-PARSERS 4.4 <i>Tokens</i>). Indices are UTF-16 units: every character the
/// lexer acts on is in the BMP, and a surrogate pair passes through a string, a template, a regular expression or a
/// comment unit by unit exactly as its single scalar does in the reference.
/// </summary>
internal static class JsLexer
{
    /// <summary>
    /// Templates nested deeper than this cannot be tokenised (the reference exhausts its recursion near 500 levels
    /// instead of answering).
    /// </summary>
    public const int MaxNesting = TaskJson.MaxDepth;

    /// <summary>The tokens of <paramref name="text"/>, or null when it cannot be tokenised.</summary>
    public static JsTokens? Tokenize(string text)
    {
        var items = new List<JsToken>();
        return Lex(text, 0, items, untilBrace: false, nesting: 0, new JsRegexScanner(text), out _) ? new JsTokens(text, items) : null;
    }

    /// <summary>
    /// Lexes from <paramref name="i"/> to the end, or in a template's <c>${…}</c> up to its closing brace
    /// (<paramref name="end"/> is the index after it). Tokens go to <paramref name="items"/> when given.
    /// </summary>
    private static bool Lex(string text, int i, List<JsToken>? items, bool untilBrace, int nesting, JsRegexScanner regexes, out int end)
    {
        var n = text.Length;
        var braces = 0;
        var regexAllowed = true;
        while (i < n)
        {
            var c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n' or '\f' or '\v')
            {
                i++;
            }
            else if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                var j = text.IndexOf('\n', i);
                i = j < 0 ? n : j;
            }
            else if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                var j = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = j < 0 ? n : j + 2;
            }
            else if (c is '\'' or '"')
            {
                if (!ReadString(text, i, out var value, out var j))
                {
                    end = n;
                    return false;
                }

                items?.Add(new JsToken(JsTokenKind.String, i, j, value, '\0'));
                regexAllowed = false;
                i = j;
            }
            else if (c == '`')
            {
                if (!ReadTemplate(text, i, nesting, regexes, out var value, out var j))
                {
                    end = n;
                    return false;
                }

                items?.Add(new JsToken(JsTokenKind.String, i, j, value, '\0'));
                regexAllowed = false;
                i = j;
            }
            else if (c == '/' && regexAllowed && regexes.End(i) is var regexEnd && regexEnd >= 0)
            {
                items?.Add(new JsToken(JsTokenKind.Regex, i, regexEnd, null, '\0'));
                regexAllowed = false;
                i = regexEnd;
            }
            else if (IsIdentStart(c))
            {
                var j = i + 1;
                while (j < n && IsIdent(text[j]))
                {
                    j++;
                }

                regexAllowed = IsRegexKeyword(text.AsSpan(i, j - i));
                items?.Add(new JsToken(JsTokenKind.Identifier, i, j, items is null ? null : text[i..j], '\0'));
                i = j;
            }
            else if (char.IsAsciiDigit(c))
            {
                var j = NumberTokenEnd(text, i);
                var raw = text.AsSpan(i, j - i);
                var valued = CodeModePatterns.Number().IsMatch(raw);
                items?.Add(new JsToken(JsTokenKind.Number, i, j, valued && items is not null ? raw.ToString() : null, '\0'));
                regexAllowed = false;
                i = j;
            }
            else
            {
                if (untilBrace)
                {
                    if (c == '{')
                    {
                        braces++;
                    }
                    else if (c == '}')
                    {
                        if (braces == 0)
                        {
                            end = i + 1;
                            return true;
                        }

                        braces--;
                    }
                }

                items?.Add(new JsToken(JsTokenKind.Punctuator, i, i + 1, null, c));
                regexAllowed = c is not (')' or ']' or '}');
                i++;
            }
        }

        end = i;
        return !untilBrace;
    }

    /// <summary>A <c>'…'</c> or <c>"…"</c> string; a raw line break leaves it unterminated.</summary>
    private static bool ReadString(string text, int i, out string? value, out int end)
    {
        var quote = text[i];
        var n = text.Length;
        StringBuilder? builder = null;
        var run = i + 1;
        var j = i + 1;
        while (j < n)
        {
            var c = text[j];
            if (c == quote)
            {
                value = Finish(text, builder, run, j);
                end = j + 1;
                return true;
            }

            if (c == '\\' && j + 1 < n)
            {
                builder ??= new StringBuilder();
                builder.Append(text, run, j - run);
                j = Escape(text, j + 1, builder);
                run = j;
                continue;
            }

            if (c is '\n' or '\r')
            {
                break;
            }

            j++;
        }

        value = null;
        end = n;
        return false;
    }

    /// <summary>A template literal: its value, or null when it holds <c>${…}</c>. Raw CR LF and CR read as LF.</summary>
    private static bool ReadTemplate(string text, int i, int nesting, JsRegexScanner regexes, out string? value, out int end)
    {
        var n = text.Length;
        var builder = new StringBuilder();
        var opaque = false;
        var j = i + 1;
        while (j < n)
        {
            var c = text[j];
            if (c == '`')
            {
                value = opaque ? null : TaskText.Scalars(builder.ToString());
                end = j + 1;
                return true;
            }

            if (c == '\\' && j + 1 < n)
            {
                j = Escape(text, j + 1, builder);
                continue;
            }

            if (c == '$' && j + 1 < n && text[j + 1] == '{')
            {
                opaque = true;
                if (nesting >= MaxNesting || !Lex(text, j + 2, null, untilBrace: true, nesting + 1, regexes, out j))
                {
                    break;
                }

                continue;
            }

            if (c == '\r')
            {
                builder.Append('\n');
                j += j + 1 < n && text[j + 1] == '\n' ? 2 : 1;
                continue;
            }

            builder.Append(c);
            j++;
        }

        value = null;
        end = n;
        return false;
    }

    private static string Finish(string text, StringBuilder? builder, int run, int end)
    {
        if (builder is null)
        {
            return text[run..end];
        }

        builder.Append(text, run, end - run);

        // Surrogates from \u escapes pair up, and a lone one reads as U+FFFD.
        return TaskText.Scalars(builder.ToString());
    }

    /// <summary>One escape after the backslash at <c>text[i - 1]</c>; returns the next index.</summary>
    private static int Escape(string text, int i, StringBuilder builder)
    {
        var c = text[i];
        switch (c)
        {
            case '\r':
                return i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1;
            case '\n' or '\u2028' or '\u2029':
                return i + 1;
            case 'n':
                builder.Append('\n');
                return i + 1;
            case 'r':
                builder.Append('\r');
                return i + 1;
            case 't':
                builder.Append('\t');
                return i + 1;
            case 'b':
                builder.Append('\b');
                return i + 1;
            case 'f':
                builder.Append('\f');
                return i + 1;
            case 'v':
                builder.Append('\v');
                return i + 1;
            case '0' when !(i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])):
                builder.Append('\0');
                return i + 1;
            case 'x' when CodeModePatterns.Hex2().IsMatch(text.AsSpan(), i + 1):
                builder.Append((char)Hex(text.AsSpan(i + 1, 2)));
                return i + 3;
            case 'u':
                foreach (var m in CodeModePatterns.CodePoint().EnumerateMatches(text.AsSpan(), i + 1))
                {
                    var point = Hex(text.AsSpan(m.Index + 1, m.Length - 2));
                    if (point <= 0x10FFFF)
                    {
                        AppendCodePoint(builder, point);
                        return m.Index + m.Length;
                    }

                    break;
                }

                if (CodeModePatterns.Hex4().IsMatch(text.AsSpan(), i + 1))
                {
                    builder.Append((char)Hex(text.AsSpan(i + 1, 4)));
                    return i + 5;
                }

                break;
        }

        builder.Append(c);
        return i + 1;
    }

    private static int NumberTokenEnd(string text, int i)
    {
        foreach (var m in CodeModePatterns.NumberToken().EnumerateMatches(text.AsSpan(), i))
        {
            return m.Index + m.Length;
        }

        return i + 1;
    }

    private static bool IsRegexKeyword(ReadOnlySpan<char> name)
    {
        foreach (var word in CodeModePatterns.RegexAfter)
        {
            if (name.SequenceEqual(word))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c is '_' or '$';

    public static bool IsIdent(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '$';

    private static int Hex(ReadOnlySpan<char> digits)
    {
        var value = 0;
        foreach (var c in digits)
        {
            value = (value << 4) | (c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
        }

        return value;
    }

    private static void AppendCodePoint(StringBuilder builder, int point)
    {
        if (point <= 0xFFFF)
        {
            builder.Append((char)point);
        }
        else
        {
            point -= 0x10000;
            builder.Append((char)(0xD800 + (point >> 10))).Append((char)(0xDC00 + (point & 0x3FF)));
        }
    }
}
