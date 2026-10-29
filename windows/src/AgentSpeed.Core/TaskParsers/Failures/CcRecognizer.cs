using System.Buffers;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// C and C++ compilers, linkers and make (TASK-PARSERS 5.3): compiler errors in C-family files, undefined symbols and
/// references (code <c>ld</c>), and the <c>N errors generated.</c> totals.
/// </summary>
internal sealed partial class CcRecognizer(FailureFound found) : FailureRecognizer(found)
{
    private bool symbols;

    [GeneratedRegex("""\A(?:[ \t]+\"(.+)\", referenced from:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LdSymbol();

    [GeneratedRegex("""\A(?:(.+?\.(?:c|cc|cpp|cxx|c\+\+|C|h|hh|hpp|hxx|h\+\+|m|mm|cu|cuh|ino|S|s|ipp|tpp|inl)):([0-9]+)(?::([0-9]+))?: (?:fatal )?error: (.+?)(?: \[([^\]]+)\])?)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CcError();

    [GeneratedRegex("""\A(?:(.+?\.(?:c|cc|cpp|cxx|c\+\+|C|h|hh|hpp|hxx|h\+\+|m|mm|cu|cuh|ino|S|s|ipp|tpp|inl)):([0-9]+)(?::([0-9]+))?: (?:warning|note): .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CcWarning();

    [GeneratedRegex("""\A(?:In file included from .*|[ \t]+from .*|.+?: In (?:function|member function|constructor|destructor|instantiation of|static member function) .*:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CcContext();

    [GeneratedRegex("""\A(?:([0-9]+) errors? generated\.)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex CcGenerated();

    [GeneratedRegex("""\A(?:Undefined symbols for architecture [^ ]+:)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LdUndefinedArch();

    [GeneratedRegex("""undefined reference to [`'\u2018](.+?)['\u2019]""", RegexOptions.CultureInvariant)]
    private static partial Regex LdReference();

    [GeneratedRegex("""\A(?:(?:[^ ]*/)?ld(?:\.[a-z]+)?: .*|collect2: error: ld returned [0-9]+ exit status|clang(?:\+\+)?: error: linker command failed.*|[ \t]+(?:_?[A-Za-z0-9_]+(?:\(.*\))? in .+\.o|in function .*))\z""", RegexOptions.CultureInvariant)]
    private static partial Regex LdNoise();

    [GeneratedRegex("""\A(?:g?make(?:\[[0-9]+\])?: \*\*\* .*|g?make(?:\[[0-9]+\])?: (?:Entering|Leaving) directory .*)\z""", RegexOptions.CultureInvariant)]
    private static partial Regex MakeNoise();

    // Every pattern below needs one of these (the patterns are anchored and case-sensitive).
    private static readonly SearchValues<string> Gate = Literals("error: ", "warning: ", "note: ", "from ", ": In ", " generated.", "Undefined symbols for architecture ", "undefined reference to ", "ld: ", "ld.", "collect2: ", " in ", "in function ", "make");

    public override bool CannotClaim(string line) => !symbols && Lacks(line, Gate);

    public override int Step(string[] lines, int i)
    {
        var line = lines[i];
        Match r;
        if (symbols)
        {
            r = LdSymbol().Match(line);
            if (r.Success)
            {
                Found.Error("cc", "", "ld", "undefined symbol " + G(r, 1));
                return 1;
            }

            if (StartsIndented(line))
            {
                return 1;
            }

            symbols = false;
        }

        r = Has(line, "error: ") ? CcError().Match(line) : Match.Empty;
        if (r.Success)
        {
            Found.Error("cc", G(r, 1), CcCode(G(r, 5)), G(r, 4));
            return 1;
        }

        if (((Has(line, "warning: ") || Has(line, "note: ")) && CcWarning().IsMatch(line)) || CcContext().IsMatch(line))
        {
            return 1;
        }

        r = CcGenerated().Match(line);
        if (r.Success)
        {
            Found.Summary("cc", FailureFamily.Error, N(r, 1), null, FailureMode.Sum);
            return 1;
        }

        if (LdUndefinedArch().IsMatch(line))
        {
            symbols = true;
            return 1;
        }

        r = LdReference().Match(line);
        if (r.Success)
        {
            Found.Error("cc", "", "ld", "undefined reference to " + G(r, 1));
            return 1;
        }

        return LdNoise().IsMatch(line) || MakeNoise().IsMatch(line) ? 1 : 0;
    }

    /// <summary>The bracketed flag with <c>-Werror,</c> removed and <c>-Werror=</c> read as <c>-W</c>.</summary>
    private static string CcCode(string flag)
    {
        if (flag.StartsWith("-Werror,", StringComparison.Ordinal))
        {
            flag = flag["-Werror,".Length..];
        }

        return flag.StartsWith("-Werror=", StringComparison.Ordinal) ? "-W" + flag["-Werror=".Length..] : flag;
    }
}
