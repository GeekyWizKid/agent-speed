using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>The edit patterns of TASK-PARSERS 6 (Appendix A.4), verbatim, as full-line patterns.</summary>
internal static partial class EditPatterns
{
    [GeneratedRegex(@"\A(?:\*\*\* (Add File|Delete File|Update File|Move to): (.+))\z", RegexOptions.CultureInvariant)]
    private static partial Regex PatchHeaderShared();

    public static Regex PatchHeader() => ThreadRegex.Of(PatchHeaderShared());

    [GeneratedRegex(@"\A(?:\[(.+)#([0-9A-Za-z]{4})\])\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashlineHeaderShared();


    public static Regex HashlineHeader() => ThreadRegex.Of(HashlineHeaderShared());

    [GeneratedRegex(@"\A(?:PUT (?:<[0-9]+|>[0-9]+\*?|>\$):)\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashlineInsertShared();


    public static Regex HashlineInsert() => ThreadRegex.Of(HashlineInsertShared());

    [GeneratedRegex(@"\A(?:MV (.+))\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashlineMoveShared();


    public static Regex HashlineMove() => ThreadRegex.Of(HashlineMoveShared());
}
