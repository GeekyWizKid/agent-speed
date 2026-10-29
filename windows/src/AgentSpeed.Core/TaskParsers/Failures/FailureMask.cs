using System.Text;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Masking (TASK-PARSERS 5.2): a message (<see cref="Mask"/>), a test name (<see cref="MaskName"/>) and a file
/// (<see cref="NormFile"/>) lose their volatile parts so the same failure gives the same digest across runs.
/// </summary>
internal static partial class FailureMask
{
    public const int MaxMessage = 200;

    [GeneratedRegex("""(?<![A-Za-z0-9._~/\\-])(?:(?:/private)?/var/folders/|(?:/private)?/tmp/|[A-Za-z]:[\\/]Users[\\/][^\\/ \t]+[\\/]AppData[\\/]Local[\\/]Temp[\\/])[^ \t'\"()\[\]\{\}<>,;|`]*""", RegexOptions.CultureInvariant)]
    private static partial Regex TmpPath();

    [GeneratedRegex("""(?<![A-Za-z0-9._~/\\-])(?:/Users/[^/ \t'\"]+/|/home/[^/ \t'\"]+/|[A-Za-z]:[\\/]Users[\\/][^\\/ \t'\"]+[\\/])""", RegexOptions.CultureInvariant)]
    private static partial Regex HomePath();

    [GeneratedRegex("""[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}""", RegexOptions.CultureInvariant)]
    private static partial Regex Uuid();

    [GeneratedRegex("""[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:[.,][0-9]+)?)?(?:Z|[+-][0-9]{2}:?[0-9]{2})?""", RegexOptions.CultureInvariant)]
    private static partial Regex IsoTime();

    [GeneratedRegex("""(?<![0-9])[0-9]{1,2}:[0-9]{2}:[0-9]{2}(?:[.,][0-9]+)?(?![0-9])""", RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDay();

    [GeneratedRegex("""(?<![0-9])[0-9]{4}-[0-9]{2}-[0-9]{2}(?![0-9])""", RegexOptions.CultureInvariant)]
    private static partial Regex Date();

    [GeneratedRegex("""0x[0-9A-Fa-f]+""", RegexOptions.CultureInvariant)]
    private static partial Regex HexAddr();

    [GeneratedRegex("""(\.[A-Za-z][A-Za-z0-9+]{0,9}):[0-9]+(?::[0-9]+)?""", RegexOptions.CultureInvariant)]
    private static partial Regex FilePos();

    [GeneratedRegex("""(\.[A-Za-z][A-Za-z0-9+]{0,9})\([0-9]+(?:,[0-9]+)?\)""", RegexOptions.CultureInvariant)]
    private static partial Regex FilePosParen();

    [GeneratedRegex("""(\.[A-Za-z][A-Za-z0-9+]{0,9}):\[[0-9]+,[0-9]+\]""", RegexOptions.CultureInvariant)]
    private static partial Regex FilePosBracket();

    [GeneratedRegex("""(?<![A-Za-z0-9_])(line|Line|LINE|lines|col|column|Column|col\.)( ?)[0-9]+(?![0-9])""", RegexOptions.CultureInvariant)]
    private static partial Regex LineWord();

    [GeneratedRegex("""(?<![A-Za-z0-9_.])[0-9]+(?:\.[0-9]+)? ?(?:ns|us|\u00b5s|ms|s|sec|secs|seconds|second|m|min|mins|minutes|minute|h|hr|hrs|hours|hour)(?![A-Za-z0-9_])""", RegexOptions.CultureInvariant)]
    private static partial Regex Duration();

    [GeneratedRegex("""(?<![A-Za-z0-9_])[0-9A-Fa-f]{8,}(?![A-Za-z0-9_])""", RegexOptions.CultureInvariant)]
    private static partial Regex LongHex();

    [GeneratedRegex("""(?<![A-Za-z0-9_])(pid|PID|port)([ =:#]+)[0-9]+""", RegexOptions.CultureInvariant)]
    private static partial Regex PidPort();

    [GeneratedRegex("""[ \t]+""", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    /// <summary><c>mask</c>: paths relative, volatile parts replaced, spaces collapsed, at most 200 characters.</summary>
    /// <remarks>Each pattern runs only when the text holds what every match of it needs, which leaves the result unchanged.</remarks>
    public static string Mask(string text, string? cwd)
    {
        var t = Paths(text, cwd);
        t = Uuid().Replace(t, "<uuid>");
        t = IsoTime().Replace(t, "<time>");
        t = t.Contains(':', StringComparison.Ordinal) ? TimeOfDay().Replace(t, "<time>") : t;
        t = t.Contains('-', StringComparison.Ordinal) ? Date().Replace(t, "<date>") : t;
        t = HexAddr().Replace(t, "0x?");
        t = FilePosBracket().Replace(t, "$1:[N]");
        t = FilePos().Replace(t, "$1:N");
        t = FilePosParen().Replace(t, "$1(N)");
        t = LineWord().Replace(t, "$1$2N");
        t = MayHoldDuration(t) ? Duration().Replace(t, "<dur>") : t;
        t = HasHexRun(t) ? LongHex().Replace(t, "<hex>") : t;
        t = PidPort().Replace(t, "$1 N");
        return Collapse(t);
    }

    /// <summary><c>mask_name</c>: a test name keeps its text; only paths are made relative and spaces collapsed.</summary>
    public static string MaskName(string text, string? cwd) => Collapse(Paths(text, cwd));

    /// <summary><c>norm_file</c>: unquoted, <c>/</c>-separated, temporary and home prefixes masked, cwd-relative.</summary>
    public static string NormFile(string path, string? cwd)
    {
        var p = path.Trim(' ', '\t').Trim('\'', '"', '`');
        if (p.StartsWith("file://", StringComparison.Ordinal))
        {
            p = p["file://".Length..];
        }

        p = p.Replace('\\', '/');
        p = MayHoldTmp(p) ? TmpPath().Replace(p, "<tmp>") : p;
        p = TaskText.CwdRelative(p, cwd, out _);
        p = MayHoldHome(p) ? HomePath().Replace(p, "~/") : p;
        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p;
    }

    /// <summary>
    /// Removes every <c>cwd/</c> that does not follow a path character (<c>NOT_IN_PATH</c>): each <c>/</c> of the cwd
    /// matches <c>/</c> or <c>\</c>, and a drive cwd compares ASCII case-insensitively.
    /// </summary>
    public static string StripCwd(string text, string? cwd)
    {
        var c = TaskText.NormCwd(cwd);
        if (c is null || text.Length <= c.Length)
        {
            return text;
        }

        var fold = TaskText.IsDriveCwd(c);
        var first = c[0];
        var other = first == '/' ? '\\' : fold && char.IsAsciiLetter(first) ? (char)(first ^ 0x20) : first;
        StringBuilder? builder = null;
        var copied = 0;
        var pos = 0;
        while (pos + c.Length < text.Length)
        {
            var next = text.AsSpan(pos).IndexOfAny(first, other);
            if (next < 0)
            {
                break;
            }

            pos += next;
            if (pos + c.Length >= text.Length)
            {
                break;
            }

            if ((pos == 0 || !IsPathChar(text[pos - 1])) && CwdAt(text, pos, c, fold))
            {
                builder ??= new StringBuilder(text.Length);
                builder.Append(text, copied, pos - copied);
                pos += c.Length + 1;
                copied = pos;
            }
            else
            {
                pos++;
            }
        }

        if (builder is null)
        {
            return text;
        }

        builder.Append(text, copied, text.Length - copied);
        return builder.ToString();
    }

    private static string Paths(string text, string? cwd)
    {
        var t = MayHoldTmp(text) ? TmpPath().Replace(text, "<tmp>") : text;
        t = StripCwd(t, cwd);
        return MayHoldHome(t) ? HomePath().Replace(t, "~/") : t;
    }

    private static string Collapse(string t)
    {
        if (t.Contains('\t', StringComparison.Ordinal) || t.Contains("  ", StringComparison.Ordinal))
        {
            t = Spaces().Replace(t, " ");
        }

        return TaskText.CutScalars(t.Trim(' '), MaxMessage);
    }

    private static bool MayHoldTmp(string t) =>
        t.Contains("/tmp/", StringComparison.Ordinal) || t.Contains("/var/folders/", StringComparison.Ordinal) || t.Contains("Users", StringComparison.Ordinal);

    private static bool MayHoldHome(string t) => t.Contains("Users", StringComparison.Ordinal) || t.Contains("/home/", StringComparison.Ordinal);

    /// <summary>A digit followed by a unit's first letter, directly or after one space: what every <c>DURATION</c> match holds.</summary>
    private static bool MayHoldDuration(string t)
    {
        for (var i = t.AsSpan().IndexOfAnyInRange('0', '9'); i >= 0 && i + 1 < t.Length; i++)
        {
            if (char.IsAsciiDigit(t[i]) && (IsUnitStart(t[i + 1]) || (t[i + 1] == ' ' && i + 2 < t.Length && IsUnitStart(t[i + 2]))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnitStart(char c) => c is 'n' or 'u' or '\u00b5' or 'm' or 's' or 'h';

    /// <summary>Eight hex digits in a row: what every <c>LONG_HEX</c> match holds.</summary>
    private static bool HasHexRun(string t)
    {
        var run = 0;
        foreach (var c in t)
        {
            run = char.IsAsciiHexDigit(c) ? run + 1 : 0;
            if (run >= 8)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CwdAt(string text, int pos, string cwd, bool fold)
    {
        for (var k = 0; k < cwd.Length; k++)
        {
            var t = text[pos + k];
            var e = cwd[k];
            if (e == '/')
            {
                if (t is not ('/' or '\\'))
                {
                    return false;
                }
            }
            else if (t != e && !(fold && char.IsAsciiLetter(e) && (t | 0x20) == (e | 0x20)))
            {
                return false;
            }
        }

        return text[pos + cwd.Length] is '/' or '\\';
    }

    private static bool IsPathChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '/' or '\\' or '-';
}
