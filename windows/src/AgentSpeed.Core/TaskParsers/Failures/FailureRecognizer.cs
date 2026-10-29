using System.Buffers;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// A recogniser of TASK-PARSERS 5.3: at line <c>i</c> it claims the number of lines it consumes (0 = not claimed) and
/// emits failures and summaries into <see cref="Found"/>; <see cref="Finish"/> emits what its state still holds when
/// the segment ends.
/// </summary>
internal abstract class FailureRecognizer(FailureFound found)
{
    protected FailureFound Found { get; } = found;

    public abstract int Step(string[] lines, int i);

    /// <summary>
    /// The recogniser's patterns are not tried on lines it cannot claim (tests compare the results without this).
    /// </summary>
    internal static bool Gates { get; set; } = true;

    /// <summary>
    /// True when <see cref="Step"/> would return 0 at this line without changing any state: the line holds none of the
    /// literals that every match of the recogniser's patterns needs. A cheap check before the patterns run.
    /// </summary>
    public virtual bool CannotClaim(string line) => false;

    /// <summary>The line holds none of <paramref name="literals"/>.</summary>
    protected static bool Lacks(string line, SearchValues<string> literals) => !line.AsSpan().ContainsAny(literals);

    protected static SearchValues<string> Literals(params string[] literals) => SearchValues.Create(literals, StringComparison.Ordinal);

    public virtual void Finish()
    {
    }

    /// <summary>A fresh set of recognisers in the order they are tried.</summary>
    public static FailureRecognizer[] CreateAll(FailureFound found) =>
    [
        new PytestRecognizer(found),
        new UnittestRecognizer(found),
        new CargoRecognizer(found),
        new RustcRecognizer(found),
        new SwiftRecognizer(found),
        new GoRecognizer(found),
        new JsRecognizer(found),
        new CheckersRecognizer(found),
        new DotnetRecognizer(found),
        new JvmRecognizer(found),
        new MiscRecognizer(found),
        new CcRecognizer(found),
    ];

    /// <summary>The text of a group; a group that did not take part reads as empty.</summary>
    protected static string G(Match match, int group) => match.Groups[group].Value;

    /// <summary>The number of a group, or <paramref name="absent"/> when the group did not take part.</summary>
    protected static long N(Match match, int group, long absent = 0)
    {
        var g = match.Groups[group];
        return g.Success ? TaskText.Num(g.ValueSpan) : absent;
    }

    /// <summary>The line holds a literal that every match of a pattern needs (a cheap check before the pattern runs).</summary>
    protected static bool Has(string line, string literal) => line.Contains(literal, StringComparison.Ordinal);

    protected static bool StartsIndented(string line) => line.Length > 0 && line[0] is ' ' or '\t';
}
