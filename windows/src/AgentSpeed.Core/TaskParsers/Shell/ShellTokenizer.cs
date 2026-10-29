using System.Text;

namespace AgentSpeed.Core.TaskParsers;

internal enum ShellTokenKind
{
    Word,
    Operator,
    Redirection,
    HereDoc,
}

/// <summary>A token of a command line: a word, an operator, a redirection with its target, or a here-document body.</summary>
internal readonly record struct ShellToken(ShellTokenKind Kind, string Text, string Target = "");

/// <summary>
/// The shell tokenizer of TASK-PARSERS 3.2: a deterministic subset of POSIX shell. Quotes and escapes are removed;
/// <c>$(…)</c>, <c>${…}</c> and backquotes are kept verbatim; every unterminated form runs to the end of the text.
/// </summary>
internal static class ShellTokenizer
{
    private static readonly string[] RedirectionOperators = ["<<<", "<<-", "<<", ">>", ">&", "<&", ">|", "<>", "&>>", "&>", ">", "<"];

    public static List<ShellToken> Tokenize(string text)
    {
        var items = new List<ShellToken>();
        List<(string Delimiter, bool StripTabs, int Index)>? pending = null;
        var builder = new StringBuilder();
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            var c = text[i];
            if (c is ' ' or '\t')
            {
                i++;
                continue;
            }

            if (c == '\\' && i + 1 < n && text[i + 1] == '\n')
            {
                i += 2;
                continue;
            }

            if (c == '\n')
            {
                items.Add(new ShellToken(ShellTokenKind.Operator, ";"));
                i++;
                if (pending is not null)
                {
                    foreach (var (delimiter, strip, index) in pending)
                    {
                        string body;
                        (body, i) = ReadHereDoc(text, i, delimiter, strip);
                        items[index] = new ShellToken(ShellTokenKind.HereDoc, body);
                    }

                    pending = null;
                }

                continue;
            }

            if (c == '#')
            {
                var end = text.IndexOf('\n', i);
                i = end < 0 ? n : end;
                continue;
            }

            if (i + 1 < n)
            {
                var d = text[i + 1];
                var two = (c, d) switch
                {
                    ('&', '&') => "&&",
                    ('|', '|') => "||",
                    ('|', '&') => "|",
                    (';', ';') => ";",
                    _ => null,
                };
                if (two is not null)
                {
                    items.Add(new ShellToken(ShellTokenKind.Operator, two));
                    i += 2;
                    continue;
                }
            }

            if (MatchRedirection(text, i, out var op, out var after))
            {
                i = after;
                while (i < n && text[i] is ' ' or '\t')
                {
                    i++;
                }

                string word;
                (word, i) = ReadWord(text, i, builder);
                if (op is "<<" or "<<-")
                {
                    items.Add(new ShellToken(ShellTokenKind.HereDoc, ""));
                    (pending ??= []).Add((word, op == "<<-", items.Count - 1));
                }
                else
                {
                    items.Add(new ShellToken(ShellTokenKind.Redirection, op, word));
                }

                continue;
            }

            if (c is ';' or '&' or '|' or '(' or ')')
            {
                items.Add(new ShellToken(ShellTokenKind.Operator, c.ToString()));
                i++;
                continue;
            }

            string value;
            (value, i) = ReadWord(text, i, builder);
            items.Add(new ShellToken(ShellTokenKind.Word, value));
        }

        // Here-documents never opened by an LF keep their empty bodies.
        return items;
    }

    /// <summary><c>REDIR</c> matched at <paramref name="i"/>: optional digits, then the first operator that matches.</summary>
    private static bool MatchRedirection(string text, int i, out string op, out int end)
    {
        var k = i;
        while (k < text.Length && char.IsAsciiDigit(text[k]))
        {
            k++;
        }

        foreach (var candidate in RedirectionOperators)
        {
            if (string.CompareOrdinal(text, k, candidate, 0, candidate.Length) == 0 && k + candidate.Length <= text.Length)
            {
                op = candidate;
                end = k + candidate.Length;
                return true;
            }
        }

        op = "";
        end = i;
        return false;
    }

    private static (string Body, int Next) ReadHereDoc(string text, int i, string delimiter, bool stripTabs)
    {
        var n = text.Length;
        var start = i;
        while (i < n)
        {
            var end = text.IndexOf('\n', i);
            var lineEnd = end < 0 ? n : end;
            var line = text.AsSpan(i, lineEnd - i);
            var lineStart = i;
            i = end < 0 ? n : end + 1;
            if ((stripTabs ? line.TrimStart('\t') : line).SequenceEqual(delimiter))
            {
                return (BodyOf(text, start, lineStart), i);
            }
        }

        return (BodyOf(text, start, n), n);
    }

    /// <summary>The lines from <paramref name="start"/> to <paramref name="end"/>, each ending with LF.</summary>
    private static string BodyOf(string text, int start, int end)
    {
        if (end <= start)
        {
            return "";
        }

        var body = text[start..end];
        return body.EndsWith('\n') ? body : body + "\n";
    }

    /// <summary>One shell word from <paramref name="i"/>: its value and the next index.</summary>
    private static (string Value, int Next) ReadWord(string text, int i, StringBuilder output)
    {
        output.Clear();
        var n = text.Length;
        while (i < n)
        {
            var c = text[i];
            if (c is ' ' or '\t' or '\n' or ';' or '&' or '|' or '(' or ')' or '<' or '>')
            {
                break;
            }

            if (c == '\\')
            {
                if (i + 1 < n)
                {
                    if (text[i + 1] != '\n')
                    {
                        output.Append(text[i + 1]);
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }

                continue;
            }

            if (c == '\'')
            {
                var end = text.IndexOf('\'', i + 1);
                end = end < 0 ? n : end;
                output.Append(text, i + 1, end - (i + 1));
                i = end + 1;
                continue;
            }

            if (c == '$' && i + 1 < n && text[i + 1] == '\'')
            {
                var j = i + 2;
                while (j < n && text[j] != '\'')
                {
                    if (text[j] == '\\' && j + 1 < n)
                    {
                        output.Append(text[j + 1]);
                        j += 2;
                        continue;
                    }

                    output.Append(text[j]);
                    j++;
                }

                i = j + 1;
                continue;
            }

            if (c == '"')
            {
                var j = i + 1;
                while (j < n && text[j] != '"')
                {
                    if (text[j] == '\\' && j + 1 < n && text[j + 1] is '"' or '\\' or '$' or '`' or '\n')
                    {
                        if (text[j + 1] != '\n')
                        {
                            output.Append(text[j + 1]);
                        }

                        j += 2;
                        continue;
                    }

                    output.Append(text[j]);
                    j++;
                }

                i = j + 1;
                continue;
            }

            if (c == '$' && i + 1 < n && text[i + 1] is '(' or '{')
            {
                var open = text[i + 1];
                var close = open == '(' ? ')' : '}';
                var depth = 0;
                var j = i + 1;
                while (j < n)
                {
                    if (text[j] == open)
                    {
                        depth++;
                    }
                    else if (text[j] == close)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }
                    }

                    j++;
                }

                output.Append(text, i, Math.Min(j + 1, n) - i);
                i = j + 1;
                continue;
            }

            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                end = end < 0 ? n : end;
                output.Append(text, i, Math.Min(end + 1, n) - i);
                i = end + 1;
                continue;
            }

            output.Append(c);
            i++;
        }

        return (output.ToString(), Math.Min(i, n));
    }
}
