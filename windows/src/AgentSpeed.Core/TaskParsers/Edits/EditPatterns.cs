using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>The edit patterns of TASK-PARSERS 6 (Appendix A.4), verbatim, as full-line patterns.</summary>
internal static partial class EditPatterns
{
    [GeneratedRegex(@"\A(?:\*\*\* (Add File|Delete File|Update File|Move to): (.+))\z", RegexOptions.CultureInvariant)]
    public static partial Regex PatchHeader();

    [GeneratedRegex(@"\A(?:\[(.+)#([0-9A-Za-z]{4})\])\z", RegexOptions.CultureInvariant)]
    public static partial Regex HashlineHeader();

    [GeneratedRegex(@"\A(?:PUT (?:<[0-9]+|>[0-9]+\*?|>\$):)\z", RegexOptions.CultureInvariant)]
    public static partial Regex HashlineInsert();

    [GeneratedRegex(@"\A(?:MV (.+))\z", RegexOptions.CultureInvariant)]
    public static partial Regex HashlineMove();
}
