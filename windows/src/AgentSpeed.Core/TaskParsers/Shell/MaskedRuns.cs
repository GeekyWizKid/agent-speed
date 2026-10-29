using System.Text.RegularExpressions;
using static AgentSpeed.Core.TaskParsers.ShellWords;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Masked runs (TASK-PARSERS 3.10): the status a masked command prints itself (<c>; echo $?</c>, ok/fail branches, a
/// failure-only branch) and error filters (<c>| grep -i error</c>) that print nothing.
/// </summary>
internal static partial class MaskedRuns
{
    private const string NoOutput = "(Bash completed with no output)";
    private const string GrepFlags = "iEnwFPashHo";

    private static readonly HashSet<string> GrepPrograms = Set("grep", "egrep");

    private static readonly HashSet<string> GrepLongFlags = Set(
        "--ignore-case", "--extended-regexp", "--line-number", "--word-regexp", "--fixed-strings", "--perl-regexp", "--text",
        "--no-messages", "--no-filename", "--with-filename", "--only-matching");

    private static readonly HashSet<string> GrepValueOptions = Set(
        "-e", "-A", "-B", "-C", "-m", "--regexp", "--after-context", "--before-context", "--context", "--max-count");

    private static readonly HashSet<string> Limiters = Set("head", "tail");

    [GeneratedRegex(@"\$\?|\$\{\?\}|\$\{PIPESTATUS\[([0-9]+)\]\}|\$PIPESTATUS", RegexOptions.CultureInvariant)]
    private static partial Regex StatusRefShared();


    private static Regex StatusRef() => ThreadRegex.Of(StatusRefShared());

    private enum Form
    {
        Printed,
        Branches,
        Else,
    }

    /// <summary>An echo text: fixed (<see cref="Suffix"/> null) or holding one status reference between prefix and suffix.</summary>
    private readonly record struct StatusText(string Prefix, string? Suffix, long? Index)
    {
        public bool IsFixed => Suffix is null;
    }

    private readonly record struct StatusEcho(Form Form, StatusText Ok, StatusText Fail);

    /// <summary><c>masked_status</c>: null when the command is not masked or its output shows nothing usable.</summary>
    public static MaskedStatus? Status(ShellAnalysis analysis, string output)
    {
        if (!analysis.Info.Masked)
        {
            return null;
        }

        var last = LastLine(output);
        var echo = analysis.Chosen >= 0 ? Echo(analysis.Parsed, analysis.Chosen) : null;
        if (echo is { } e && last is not null)
        {
            if (e.Form == Form.Printed)
            {
                if (PrintedValue(last, e.Ok.Prefix, e.Ok.Suffix!) is { } value)
                {
                    return new MaskedStatus(value, value != 0);
                }
            }
            else
            {
                if (e.Form == Form.Branches && last == TaskText.Trim(e.Ok.Prefix))
                {
                    return new MaskedStatus(0, false);
                }

                if (e.Fail.IsFixed)
                {
                    if (last == TaskText.Trim(e.Fail.Prefix))
                    {
                        return new MaskedStatus(null, true);
                    }
                }
                else if (PrintedValue(last, e.Fail.Prefix, e.Fail.Suffix!) is { } value && value != 0)
                {
                    return new MaskedStatus(value, true);
                }
            }
        }

        return analysis.Info.ErrorFilter && Silent(output) ? new MaskedStatus(null, false) : null;
    }

    /// <summary>No output at all: nothing but JSON whitespace, or Claude Code's placeholder for it.</summary>
    public static bool Silent(string text)
    {
        var stripped = text.AsSpan().Trim(" \t\r\n");
        return stripped.IsEmpty || stripped.SequenceEqual(NoOutput);
    }

    /// <summary>The last line of the LF-normalised output that is not empty after trimming, trimmed.</summary>
    private static string? LastLine(string output)
    {
        var text = TaskText.Lf(output).AsSpan();
        while (text.Length > 0)
        {
            var newline = text.LastIndexOf('\n');
            var line = text[(newline + 1)..].Trim(" \t");
            if (!line.IsEmpty)
            {
                return line.ToString();
            }

            if (newline < 0)
            {
                break;
            }

            text = text[..newline];
        }

        return null;
    }

    private static int PipelineEnd(List<ShellSegment> segs, int k)
    {
        while (k + 1 < segs.Count && segs[k].OpAfter == "|")
        {
            k++;
        }

        return k;
    }

    private static int PipelineStart(List<ShellSegment> segs, int k)
    {
        while (k > 0 && segs[k].OpBefore == "|")
        {
            k--;
        }

        return k;
    }

    /// <summary>The chosen segment pipes into a grep for error lines, then at most <c>head</c> / <c>tail</c>, ending the command.</summary>
    public static bool ErrorFilter(List<ShellSegment> segs, int k)
    {
        var end = PipelineEnd(segs, k);
        if (end == k || end != segs.Count - 1 || segs[k].OpAfter != "|")
        {
            return false;
        }

        var words = segs[k + 1].Words;
        if (words.Length == 0 || !GrepPrograms.Contains(Prog(words[0])))
        {
            return false;
        }

        var patterns = GrepPatterns(words);
        if (patterns is null || !patterns.Exists(p => TaskText.LowerAscii(p).Contains("error", StringComparison.Ordinal)))
        {
            return false;
        }

        for (var j = k + 2; j <= end; j++)
        {
            if (segs[j].Words.Length == 0 || !Limiters.Contains(Prog(segs[j].Words[0])))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The patterns of a grep that reads only its input and keeps matching lines, else null.</summary>
    private static List<string>? GrepPatterns(string[] words)
    {
        var patterns = new List<string>();
        var positional = new List<string>();
        for (var i = 1; i < words.Length; i++)
        {
            var w = words[i];
            if (w == "--")
            {
                positional.AddRange(words[(i + 1)..]);
                break;
            }

            var eq = w.IndexOf('=', StringComparison.Ordinal);
            var name = eq < 0 ? w : w[..eq];
            if (GrepValueOptions.Contains(w) || (w.StartsWith("--", StringComparison.Ordinal) && eq >= 0 && GrepValueOptions.Contains(name)))
            {
                string arg;
                if (eq >= 0)
                {
                    arg = w[(eq + 1)..];
                }
                else if (i + 1 < words.Length)
                {
                    arg = words[++i];
                }
                else
                {
                    return null;
                }

                if (name is "-e" or "--regexp")
                {
                    patterns.Add(arg);
                }
            }
            else if (w.Length > 2 && w[0] == '-' && w[1] is 'e' or 'A' or 'B' or 'C' or 'm' && !w.StartsWith("--", StringComparison.Ordinal))
            {
                if (w[1] == 'e')
                {
                    patterns.Add(w[2..]);
                }
            }
            else if (GrepLongFlags.Contains(w) || name is "--color" or "--colour")
            {
            }
            else if (w.StartsWith('-') && w.Length > 1 && !w.StartsWith("--", StringComparison.Ordinal))
            {
                for (var c = 1; c < w.Length; c++)
                {
                    if (!GrepFlags.Contains(w[c], StringComparison.Ordinal))
                    {
                        return null;
                    }
                }
            }
            else if (w.StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }
            else
            {
                positional.Add(w);
            }
        }

        if (patterns.Count == 0)
        {
            if (positional.Count == 0)
            {
                return null;
            }

            patterns.Add(positional[0]);
            positional.RemoveAt(0);
        }

        return positional.Count > 0 ? null : patterns;
    }

    /// <summary>What an <c>echo</c> segment prints (its words joined by spaces), or null.</summary>
    private static string? EchoText(ShellSegment seg)
    {
        var w = seg.Words;
        if (w.Length < 2 || Prog(w[0]) != "echo" || seg.Redirections.Count > 0 || seg.HereDocs.Count > 0 || w[1].StartsWith('-'))
        {
            return null;
        }

        return string.Join(' ', w, 1, w.Length - 1);
    }

    /// <summary>A status text, a fixed text, or null.</summary>
    private static StatusText? TextOf(string text)
    {
        var refs = StatusRef().Matches(text);
        if (refs.Count > 1)
        {
            return null;
        }

        if (refs.Count == 0)
        {
            return text.Contains('$', StringComparison.Ordinal) || text.Contains('`', StringComparison.Ordinal) || TaskText.Trim(text).Length == 0
                ? null
                : new StatusText(text, null, null);
        }

        var m = refs[0];
        var prefix = text[..m.Index];
        var suffix = text[(m.Index + m.Length)..];
        if (prefix.Contains('$', StringComparison.Ordinal) || prefix.Contains('`', StringComparison.Ordinal)
            || suffix.Contains('$', StringComparison.Ordinal) || suffix.Contains('`', StringComparison.Ordinal))
        {
            return null;
        }

        long? index = m.Value == "$PIPESTATUS" ? 0 : m.Groups[1].Success ? TaskText.Num(m.Groups[1].ValueSpan) : null;
        return new StatusText(prefix, suffix, index);
    }

    /// <summary>The echo segments that report the chosen segment's status (3.10), or null.</summary>
    private static StatusEcho? Echo(ShellParse parsed, int k)
    {
        var segs = parsed.Segments;
        var end = PipelineEnd(segs, k);
        var own = end == k || parsed.Pipefail;
        var after = segs.Count - end - 1;
        var texts = new StatusText[after];
        for (var i = 0; i < after; i++)
        {
            if (TextOf(EchoText(segs[end + 1 + i]) ?? "") is not { } t)
            {
                return null;
            }

            texts[i] = t;
        }

        if (after == 1 && segs[end + 1].OpBefore == ";")
        {
            var t = texts[0];
            if (t.IsFixed || !(t.Index is null ? own : t.Index == k - PipelineStart(segs, k)))
            {
                return null;
            }

            return new StatusEcho(Form.Printed, t, default);
        }

        if (!own || Array.Exists(texts, t => t.Index is not null))
        {
            return null;
        }

        if (after == 2 && segs[end + 1].OpBefore == "&&" && segs[end + 2].OpBefore == "||")
        {
            var (ok, fail) = (texts[0], texts[1]);
            if (!ok.IsFixed || (fail.IsFixed && TaskText.Trim(fail.Prefix) == TaskText.Trim(ok.Prefix)))
            {
                return null;
            }

            return new StatusEcho(Form.Branches, ok, fail);
        }

        if (after == 1 && segs[end + 1].OpBefore == "||")
        {
            return new StatusEcho(Form.Else, default, texts[0]);
        }

        return null;
    }

    /// <summary>The integer between <paramref name="prefix"/> and <paramref name="suffix"/> (trimmed at their outer edge), or null.</summary>
    private static long? PrintedValue(string line, string prefix, string suffix)
    {
        prefix = TaskText.TrimStart(prefix);
        suffix = TaskText.TrimEnd(suffix);
        if (line.Length < prefix.Length + suffix.Length || !line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var middle = line.AsSpan(prefix.Length, line.Length - prefix.Length - suffix.Length);
        var digits = middle.StartsWith("-") ? middle[1..] : middle;
        if (digits.IsEmpty)
        {
            return null;
        }

        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return null;
            }
        }

        return TaskText.Num(middle);
    }
}
