using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The JavaScript token patterns of TASK-PARSERS 4.4 (Appendix A.4), verbatim; a match at a position is anchored with
/// <c>\G</c>, a full match with <c>\A(?:P)\z</c>.
/// </summary>
internal static partial class CodeModePatterns
{
    /// <summary>The words after which a <c>/</c> starts a regular expression (A.2 <c>JS_REGEX_AFTER</c>).</summary>
    public static readonly string[] RegexAfter =
        ["return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield", "await"];

    [GeneratedRegex(@"\G(?:[0-9](?:[0-9A-Za-z_.]|(?<=[eE])[+-])*)", RegexOptions.CultureInvariant)]
    public static partial Regex NumberToken();

    [GeneratedRegex(@"\A(?:[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\z", RegexOptions.CultureInvariant)]
    public static partial Regex Number();

    [GeneratedRegex(@"\G(?:[0-9A-Fa-f]{2})", RegexOptions.CultureInvariant)]
    public static partial Regex Hex2();

    [GeneratedRegex(@"\G(?:[0-9A-Fa-f]{4})", RegexOptions.CultureInvariant)]
    public static partial Regex Hex4();

    [GeneratedRegex(@"\G(?:\{([0-9A-Fa-f]{1,6})\})", RegexOptions.CultureInvariant)]
    public static partial Regex CodePoint();
}
