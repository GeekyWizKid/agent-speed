using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>Counts read from summary lines (TASK-PARSERS 5.3, <c>parse_counts</c>).</summary>
internal static partial class FailureCounts
{
    [GeneratedRegex("""\A(?:[ \t]*([0-9]+) ([A-Za-z][A-Za-z ]*?)[ \t]*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CountPartShared();

    private static Regex CountPart() => ThreadRegex.Of(CountPartShared());

    /// <summary>
    /// The text split at <paramref name="separator"/>; each part <c>N label</c> adds N to its label (equal labels add up).
    /// </summary>
    public static Dictionary<string, long> Parse(string text, char separator = ',')
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var part in text.Split(separator))
        {
            var match = CountPart().Match(part);
            if (match.Success)
            {
                var label = match.Groups[2].Value;
                counts[label] = Add(counts.GetValueOrDefault(label), TaskText.Num(match.Groups[1].ValueSpan));
            }
        }

        return counts;
    }

    /// <summary>The count of a label, 0 when missing.</summary>
    public static long CountOf(this Dictionary<string, long>? counts, string label) => counts?.GetValueOrDefault(label) ?? 0;

    /// <summary>The sum of two counts, held at <see cref="long.MaxValue"/> where the reference's integers would exceed it.</summary>
    public static long Add(long a, long b)
    {
        var sum = a + b;
        return a >= 0 && b >= 0 && sum < 0 ? long.MaxValue : sum;
    }
}
