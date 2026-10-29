using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// Text conventions shared by every task parser (TASK-PARSERS 1): digests, ASCII-only trimming and case folding,
/// LF normalisation, numbers read from text and the cwd-relative rule (3.3).
/// </summary>
internal static class TaskText
{
    /// <summary>Numbers read from text are capped at 18 digits (TASK-PARSERS 1).</summary>
    public const long MaxNumber = 999_999_999_999_999_999;

    /// <summary>The first 16 hex digits of SHA-256 over the empty string.</summary>
    public const string EmptyDigest = "e3b0c44298fc1c14";

    private const int StackLimit = 1024;

    /// <summary>
    /// <c>sha16(text)</c>: the first 16 lowercase hex digits of SHA-256 over the UTF-8 bytes of the text. An unpaired
    /// surrogate encodes as U+FFFD, as the encoder's replacement fallback writes it.
    /// </summary>
    public static string Sha16(string text)
    {
        if (text.Length == 0)
        {
            return EmptyDigest;
        }

        var max = Encoding.UTF8.GetMaxByteCount(text.Length);
        byte[]? rented = null;
        var buffer = max <= StackLimit ? stackalloc byte[max] : (rented = ArrayPool<byte>.Shared.Rent(max));
        try
        {
            var count = Encoding.UTF8.GetBytes(text, buffer);
            return Sha16(buffer[..count]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static string Sha16(ReadOnlySpan<byte> utf8)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8, hash);
        return Convert.ToHexStringLower(hash[..8]);
    }

    /// <summary>The text with every unpaired surrogate replaced by U+FFFD (TASK-PARSERS 1: strings are scalar values).</summary>
    public static string Scalars(string text)
    {
        var bad = FirstLoneSurrogate(text);
        if (bad < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, bad);
        for (var i = bad; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[i + 1]);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(c) ? '�' : c);
            }
        }

        return builder.ToString();
    }

    private static int FirstLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!char.IsSurrogate(c))
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>LF-normalised: CR LF, then any remaining CR, replaced by LF.</summary>
    public static string Lf(string text) =>
        text.Contains('\r', StringComparison.Ordinal) ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : text;

    private static readonly char[] Ws = [' ', '\t'];

    /// <summary>Trim: ASCII spaces and tabs only.</summary>
    public static string Trim(string text) => text.Trim(Ws);

    public static string TrimStart(string text) => text.TrimStart(Ws);

    public static string TrimEnd(string text) => text.TrimEnd(Ws);

    public static bool IsWs(char c) => c is ' ' or '\t';

    public static bool IsAsciiAlnum(char c) => char.IsAsciiLetterOrDigit(c);

    /// <summary>ASCII-only lower case (TASK-PARSERS 1: case folding is ASCII only).</summary>
    public static string LowerAscii(string text)
    {
        var i = 0;
        while (i < text.Length && !char.IsAsciiLetterUpper(text[i]))
        {
            i++;
        }

        if (i == text.Length)
        {
            return text;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var k = 0; k < source.Length; k++)
            {
                var c = source[k];
                span[k] = char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c;
            }
        });
    }

    /// <summary>ASCII-only upper case.</summary>
    public static string UpperAscii(string text)
    {
        var i = 0;
        while (i < text.Length && !char.IsAsciiLetterLower(text[i]))
        {
            i++;
        }

        if (i == text.Length)
        {
            return text;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var k = 0; k < source.Length; k++)
            {
                var c = source[k];
                span[k] = char.IsAsciiLetterLower(c) ? (char)(c & ~0x20) : c;
            }
        });
    }

    /// <summary>
    /// A decimal number read from text (a run of digits with an optional leading sign), capped at
    /// <see cref="MaxNumber"/>: more than 18 significant digits read as 999 999 999 999 999 999.
    /// </summary>
    public static long Num(ReadOnlySpan<char> text)
    {
        var negative = text.StartsWith("-");
        var start = 0;
        while (start < text.Length && text[start] is '+' or '-')
        {
            start++;
        }

        while (start < text.Length && text[start] == '0')
        {
            start++;
        }

        var digits = text[start..];
        if (digits.Length > 18)
        {
            return negative ? -MaxNumber : MaxNumber;
        }

        long value = 0;
        foreach (var c in digits)
        {
            value = (value * 10) + (c - '0');
        }

        return negative ? -value : value;
    }

    /// <summary>The working directory with <c>/</c> separators and no trailing <c>/</c>; null when absent or the root.</summary>
    public static string? NormCwd(string? cwd)
    {
        if (cwd is null)
        {
            return null;
        }

        var c = cwd.Replace('\\', '/').TrimEnd('/');
        return c.Length == 0 ? null : c;
    }

    /// <summary>The cwd starts with a drive (<c>DRIVE</c> matched at the start of <c>cwd + "/"</c>).</summary>
    public static bool IsDriveCwd(string normCwd) =>
        normCwd.Length >= 2 && char.IsAsciiLetter(normCwd[0]) && normCwd[1] == ':' && (normCwd.Length == 2 || normCwd[2] == '/');

    /// <summary>
    /// <i>cwd-relative</i> (3.3): the part of a <c>/</c>-separated path below <paramref name="cwd"/> when the path is
    /// cwd or inside it (<paramref name="inside"/> true), else the path unchanged. A drive cwd compares ASCII
    /// case-insensitively.
    /// </summary>
    public static string CwdRelative(string path, string? cwd, out bool inside)
    {
        var c = NormCwd(cwd);
        inside = false;
        if (c is null)
        {
            return path;
        }

        var comparison = IsDriveCwd(c) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Length == c.Length && AsciiEquals(path, c, comparison))
        {
            inside = true;
            return "";
        }

        if (path.Length > c.Length && path[c.Length] == '/' && AsciiEquals(path.AsSpan(0, c.Length), c, comparison))
        {
            inside = true;
            return path[(c.Length + 1)..];
        }

        return path;
    }

    private static bool AsciiEquals(ReadOnlySpan<char> a, ReadOnlySpan<char> b, StringComparison comparison) =>
        comparison == StringComparison.Ordinal ? a.SequenceEqual(b) : AsciiIgnoreCaseEquals(a, b);

    /// <summary>Equality with ASCII letters folded; every other character compares exactly.</summary>
    public static bool AsciiIgnoreCaseEquals(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x == y)
            {
                continue;
            }

            if (!char.IsAsciiLetter(x) || (x | 0x20) != (y | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The number of Unicode scalar values in the text (a surrogate pair counts once).</summary>
    public static int ScalarLength(string text)
    {
        var count = text.Length;
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
            {
                count--;
                i++;
            }
        }

        return count;
    }

    /// <summary>The first <paramref name="max"/> scalar values of the text, never splitting a surrogate pair.</summary>
    public static string CutScalars(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var units = 0;
        for (var n = 0; n < max && units < text.Length; n++)
        {
            units += char.IsHighSurrogate(text[units]) && units + 1 < text.Length && char.IsLowSurrogate(text[units + 1]) ? 2 : 1;
        }

        return units >= text.Length ? text : text[..units];
    }

    /// <summary>Ordinal order over scalar values (UTF-16 ordinal order differs for characters above U+FFFF).</summary>
    public static int CompareScalars(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x == y)
            {
                continue;
            }

            // A surrogate (U+D800-U+DFFF) stands for a scalar above U+FFFF, so it sorts after every BMP character above it.
            var kx = char.IsSurrogate(x) ? x + 0x10000 : x;
            var ky = char.IsSurrogate(y) ? y + 0x10000 : y;
            return kx.CompareTo(ky);
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <summary>A comparer for <see cref="CompareScalars"/>.</summary>
    public static IComparer<string> ScalarOrder { get; } = Comparer<string>.Create(CompareScalars);
}
