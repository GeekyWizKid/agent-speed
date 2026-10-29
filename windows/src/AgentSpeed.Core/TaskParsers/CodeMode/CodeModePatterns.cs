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
    private static partial Regex NumberTokenShared();


    public static Regex NumberToken() => ThreadRegex.Of(NumberTokenShared());

    [GeneratedRegex(@"\A(?:[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberShared();


    public static Regex Number() => ThreadRegex.Of(NumberShared());

    [GeneratedRegex(@"\G(?:[0-9A-Fa-f]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex Hex2Shared();


    public static Regex Hex2() => ThreadRegex.Of(Hex2Shared());

    [GeneratedRegex(@"\G(?:[0-9A-Fa-f]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex Hex4Shared();


    public static Regex Hex4() => ThreadRegex.Of(Hex4Shared());

    [GeneratedRegex(@"\G(?:\{([0-9A-Fa-f]{1,6})\})", RegexOptions.CultureInvariant)]
    private static partial Regex CodePointShared();


    public static Regex CodePoint() => ThreadRegex.Of(CodePointShared());
}
