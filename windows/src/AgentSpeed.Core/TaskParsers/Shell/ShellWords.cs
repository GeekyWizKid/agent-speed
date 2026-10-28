namespace AgentSpeed.Core.TaskParsers;

/// <summary>An option's value as <c>parse_opts</c> records it: a flag, or the text of its value.</summary>
internal readonly record struct OptionValue(string? Text)
{
    public static OptionValue Flag => default;

    public bool IsText => Text is not null;

    /// <summary>Python truthiness: a flag, or a non-empty value.</summary>
    public bool IsTruthy => Text is null || Text.Length > 0;
}

/// <summary>Positionals, options and the words after <c>--</c> of a program's arguments (TASK-PARSERS 3.4).</summary>
internal sealed class ParsedOptions
{
    public List<string> Positionals { get; } = [];

    public Dictionary<string, OptionValue> Options { get; } = new(StringComparer.Ordinal);

    public string[] AfterDashDash { get; set; } = [];

    public bool Has(string name) => Options.ContainsKey(name);

    public bool HasAny(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (Options.ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>opts.get(a) or opts.get(b) or …</c>: the first truthy value, else the last name's lookup. Null when that lookup
    /// finds nothing.
    /// </summary>
    public OptionValue? FirstTruthy(params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var found = Options.TryGetValue(names[i], out var value);
            if (found && value.IsTruthy)
            {
                return value;
            }

            if (i == names.Length - 1)
            {
                return found ? value : null;
            }
        }

        return null;
    }

    /// <summary>The positionals followed by the words after <c>--</c>.</summary>
    public string[] PositionalsAndAfter()
    {
        var all = new string[Positionals.Count + AfterDashDash.Length];
        Positionals.CopyTo(all);
        AfterDashDash.CopyTo(all, Positionals.Count);
        return all;
    }
}

/// <summary>Program names, prefixes, shell wrappers, runners and option reading (TASK-PARSERS 3.4, 3.5).</summary>
internal static class ShellWords
{
    public static HashSet<string> Set(params string[] items) => new(items, StringComparer.Ordinal);

    public static readonly HashSet<string> Shells = Set("bash", "sh", "zsh", "dash", "ksh", "fish");
    public static readonly HashSet<string> PowerShells = Set("pwsh", "powershell");
    public static readonly HashSet<string> LeadingKeywords = Set("!", "if", "then", "else", "elif", "do", "while", "until", "{");
    public static readonly HashSet<string> ClosingKeywords = Set("fi", "done", "esac", "}");
    public static readonly HashSet<string> Constructs = Set("for", "case", "select", "function");

    public static readonly HashSet<string> Neutral = Set(
        "cd", "pushd", "popd", "export", "set", "unset", "source", ".", "true", ":", "echo", "printf", "sleep", "clear", "alias",
        "shopt", "ulimit", "umask", "wait", "trap", "exit", "return", "local", "declare", "typeset", "read", "false");

    private static readonly HashSet<string> NiceValue = Set("-n", "--adjustment");
    private static readonly HashSet<string> StdbufValue = Set("-i", "-o", "-e");
    private static readonly HashSet<string> CaffeinateValue = Set("-t", "-w");

    private static readonly HashSet<string> XvfbValue = Set(
        "-s", "--server-args", "-n", "--server-num", "-e", "--error-file", "-f", "--auth-file", "-p", "--xauth-protocol");

    private static readonly HashSet<string> TimeoutValue = Set("-s", "--signal", "-k", "--kill-after");
    private static readonly HashSet<string> EnvValue = Set("-u", "--unset", "-C", "--chdir", "-S", "--split-string");
    private static readonly HashSet<string> SudoValue = Set("-u", "-g", "-h", "-p", "-C", "-D", "-r", "-t", "-U", "--user", "--group");
    private static readonly HashSet<string> PrefixFlags = Set("exec", "command", "builtin", "noglob", "unbuffer", "chronic");

    public static readonly HashSet<string> PyValue = Set("-W", "-X", "--check-hash-based-pycs");

    private static readonly HashSet<string> UvCondaValue = Set(
        "--with", "--with-requirements", "--with-editable", "--python", "-p", "--extra", "--group", "--only-group", "--package",
        "--directory", "--project", "--env-file", "--index", "--index-url", "--default-index", "--extra-index-url", "-C",
        "--config-setting", "--no-group", "--script", "--gui-script",
        "-n", "--name", "-p", "--prefix", "--cwd");

    private static readonly HashSet<string> NpxValue = Set("-p", "--package", "-c", "--call", "--cache", "--userconfig");
    private static readonly HashSet<string> XcrunValue = Set("--sdk", "-sdk", "--toolchain", "-toolchain");
    private static readonly HashSet<string> CoverageValue = Set("--source", "--rcfile", "--data-file", "--concurrency", "--include", "--omit", "--context");
    private static readonly HashSet<string> UvRunners = Set("uv", "poetry", "pipenv", "pdm", "rye", "pixi", "conda", "mamba", "micromamba", "hatch");
    private static readonly HashSet<string> PhpTools = Set("phpunit", "pest", "phpstan", "psalm");
    private static readonly HashSet<string> CcNames = Set("gcc", "g++", "cc", "c++", "clang", "clang++");

    private static readonly Dictionary<string, string> ProgramAliases = new(StringComparer.Ordinal)
    {
        ["gradlew"] = "gradle",
        ["mvnw"] = "mvn",
        ["py.test"] = "pytest",
        ["nodejs"] = "node",
        ["gmake"] = "make",
        ["bazelisk"] = "bazel",
    };

    private static readonly string[] WindowsExtensions = [".exe", ".cmd", ".bat", ".ps1"];

    /// <summary>The normalised program name of a word (3.4).</summary>
    public static string Prog(string word)
    {
        var slash = word.AsSpan().LastIndexOfAny('/', '\\');
        var name = TaskText.LowerAscii(slash < 0 ? word : word[(slash + 1)..]);
        foreach (var ext in WindowsExtensions)
        {
            if (name.EndsWith(ext, StringComparison.Ordinal))
            {
                name = name[..^ext.Length];
                break;
            }
        }

        if (name == "py" || IsVersioned(name, "python") || IsVersioned(name, "pypy"))
        {
            return "python";
        }

        if (IsCompiler(name))
        {
            return "cc";
        }

        return ProgramAliases.TryGetValue(name, out var alias) ? alias : name;
    }

    /// <summary><c>stem[0-9.]*</c>.</summary>
    private static bool IsVersioned(string name, string stem)
    {
        if (!name.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = stem.Length; i < name.Length; i++)
        {
            if (!char.IsAsciiDigit(name[i]) && name[i] != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>(gcc|g\+\+|cc|c\+\+|clang|clang\+\+)(-[0-9.]+)?</c>.</summary>
    private static bool IsCompiler(string name)
    {
        var dash = name.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return CcNames.Contains(name);
        }

        if (dash == name.Length - 1 || !CcNames.Contains(name[..dash]))
        {
            return false;
        }

        for (var i = dash + 1; i < name.Length; i++)
        {
            if (!char.IsAsciiDigit(name[i]) && name[i] != '.')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsOption(string word) => word.StartsWith('-') && word != "-";

    /// <summary><c>drop_options</c>: the index after the option words from <paramref name="i"/> (may pass the end).</summary>
    public static int DropOptions(string[] words, int i, HashSet<string>? withValue = null, bool stopDashDash = true)
    {
        while (i < words.Length)
        {
            var w = words[i];
            if (w == "--" && stopDashDash)
            {
                return i + 1;
            }

            if (!w.StartsWith('-') || w == "-")
            {
                return i;
            }

            i += withValue is not null && withValue.Contains(w) ? 2 : 1;
        }

        return i;
    }

    /// <summary><c>words[from:]</c>, empty when <paramref name="from"/> passes the end.</summary>
    public static string[] From(string[] words, int from) => from >= words.Length ? [] : from <= 0 ? words : words[from..];

    /// <summary><c>NAME_ASSIGN</c> as a prefix match: <c>[A-Za-z_][A-Za-z0-9_]*=</c>.</summary>
    public static bool IsAssignment(string word)
    {
        if (word.Length < 2 || !(char.IsAsciiLetter(word[0]) || word[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < word.Length; i++)
        {
            var c = word[i];
            if (c == '=')
            {
                return true;
            }

            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Removes environment assignments and prefix commands (3.4); <c>nohup</c> and <c>setsid</c> mark the segment background.</summary>
    public static void StripPrefixes(ShellSegment seg)
    {
        var words = seg.Words;
        var i = 0;
        while (i < words.Length)
        {
            var w = words[i];
            if (IsAssignment(w))
            {
                i++;
                continue;
            }

            switch (Prog(w))
            {
                case "env":
                    i = DropOptions(words, i + 1, EnvValue);
                    continue;
                case "sudo" or "doas":
                    i = DropOptions(words, i + 1, SudoValue);
                    continue;
                case "time":
                    i = DropOptions(words, i + 1);
                    continue;
                case "nice":
                    i = DropOptions(words, i + 1, NiceValue);
                    continue;
                case "nohup" or "setsid":
                    seg.Background = true;
                    i = DropOptions(words, i + 1);
                    continue;
                case var p when PrefixFlags.Contains(p):
                    i = DropOptions(words, i + 1);
                    continue;
                case "stdbuf":
                    i = DropOptions(words, i + 1, StdbufValue);
                    continue;
                case "caffeinate":
                    i = DropOptions(words, i + 1, CaffeinateValue);
                    continue;
                case "xvfb-run":
                    i = DropOptions(words, i + 1, XvfbValue);
                    continue;
                case "timeout" or "gtimeout":
                    i = DropOptions(words, i + 1, TimeoutValue) + 1;
                    continue;
            }

            break;
        }

        seg.Words = From(words, i);
    }

    /// <summary>Drops leading keywords (<c>if</c>, <c>then</c>, <c>{</c>, …) and trailing <c>}</c> words.</summary>
    public static void DropKeywords(ShellSegment seg)
    {
        var words = seg.Words;
        var start = 0;
        while (start < words.Length && LeadingKeywords.Contains(words[start]))
        {
            start++;
        }

        var end = words.Length;
        while (end > start && words[end - 1] == "}")
        {
            end--;
        }

        if (start > 0 || end < words.Length)
        {
            seg.Words = words[start..end];
        }
    }

    /// <summary>The script of a shell wrapper (<c>bash -lc '…'</c>, <c>pwsh -Command …</c>, <c>cmd /c …</c>), or null.</summary>
    public static string? ShellScript(ShellSegment seg)
    {
        var words = seg.Words;
        if (words.Length == 0)
        {
            return null;
        }

        var p = Prog(words[0]);
        if (Shells.Contains(p))
        {
            for (var k = 1; k < words.Length; k++)
            {
                var w = words[k];
                if (!w.StartsWith('-'))
                {
                    return null;
                }

                if (IsShellCFlag(w) && k + 1 < words.Length)
                {
                    return words[k + 1];
                }
            }

            return null;
        }

        if (PowerShells.Contains(p))
        {
            for (var k = 1; k < words.Length; k++)
            {
                var w = TaskText.LowerAscii(words[k]);
                if (w is "-command" or "-c" && k + 1 < words.Length)
                {
                    return string.Join(' ', words, k + 1, words.Length - k - 1);
                }
            }

            return null;
        }

        if (p == "cmd")
        {
            for (var k = 1; k < words.Length; k++)
            {
                if (TaskText.LowerAscii(words[k]) == "/c" && k + 1 < words.Length)
                {
                    return string.Join(' ', words, k + 1, words.Length - k - 1);
                }
            }
        }

        return null;
    }

    /// <summary><c>SHELL_C_FLAG</c>: <c>-[A-Za-z]*c[A-Za-z]*</c>.</summary>
    private static bool IsShellCFlag(string w)
    {
        if (w.Length < 2 || w[0] != '-')
        {
            return false;
        }

        var hasC = false;
        for (var i = 1; i < w.Length; i++)
        {
            if (!char.IsAsciiLetter(w[i]))
            {
                return false;
            }

            hasC |= w[i] == 'c';
        }

        return hasC;
    }

    /// <summary>The index of the first word that is <c>-m</c>, <c>-</c> or not an option, skipping option values.</summary>
    public static int SkipUntilModule(string[] words, HashSet<string> valueOptions)
    {
        var k = 0;
        while (k < words.Length && words[k].StartsWith('-') && words[k] is not ("-m" or "-"))
        {
            k += valueOptions.Contains(words[k]) ? 2 : 1;
        }

        return k;
    }

    /// <summary>Removes runner wrappers until a program remains, at most 8 times (3.4).</summary>
    public static string[] Unwrap(string[] words)
    {
        for (var round = 0; round < 8; round++)
        {
            if (words.Length == 0)
            {
                return words;
            }

            var p = Prog(words[0]);
            var rest = words[1..];
            var first = rest.Length > 0 ? rest[0] : null;
            if (p is "npx" or "pnpx" or "bunx")
            {
                words = From(rest, DropOptions(rest, 0, NpxValue));
                continue;
            }

            if (p is "npm" or "pnpm" or "yarn" or "bun" && first is "exec" or "dlx" or "x")
            {
                var tail = rest[1..];
                words = From(tail, DropOptions(tail, 0, NpxValue));
                continue;
            }

            if (p == "mise" && first is "exec" or "x")
            {
                var dash = Array.IndexOf(rest, "--");
                words = From(rest, dash >= 0 ? dash + 1 : 1);
                continue;
            }

            if (UvRunners.Contains(p) && first == "run")
            {
                var tail = rest[1..];
                words = From(tail, DropOptions(tail, 0, UvCondaValue));
                continue;
            }

            if (p == "python" || (p == "coverage" && first == "run"))
            {
                var opts = p == "python" ? rest : rest[1..];
                var k = SkipUntilModule(opts, p == "python" ? PyValue : CoverageValue);
                if (k + 1 < opts.Length && opts[k] == "-m")
                {
                    words = opts[(k + 1)..];
                    continue;
                }

                return words;
            }

            if (p == "bundle" && first == "exec")
            {
                words = rest[1..];
                continue;
            }

            if (p == "xcrun")
            {
                words = From(rest, DropOptions(rest, 0, XcrunValue));
                continue;
            }

            if (p == "dotnet" && rest.Length >= 2 && rest[0] == "tool" && rest[1] == "run")
            {
                words = rest[2..];
                continue;
            }

            if (p == "php" && first is not null && !first.StartsWith('-') && PhpTools.Contains(Prog(first)))
            {
                words = rest;
                continue;
            }

            return words;
        }

        return words;
    }

    /// <summary><c>parse_opts</c> (3.4).</summary>
    public static ParsedOptions ParseOpts(string[] args, HashSet<string>? valueOptions = null, bool stopDashDash = true, int start = 0)
    {
        var result = new ParsedOptions();
        for (var k = start; k < args.Length; k++)
        {
            var w = args[k];
            if (w == "--" && stopDashDash)
            {
                result.AfterDashDash = args[(k + 1)..];
                break;
            }

            if (!IsOption(w))
            {
                result.Positionals.Add(w);
                continue;
            }

            var eq = w.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0 && w.StartsWith("--", StringComparison.Ordinal))
            {
                result.Options[w[..eq]] = new OptionValue(w[(eq + 1)..]);
            }
            else if (valueOptions is not null && valueOptions.Contains(w) && k + 1 < args.Length)
            {
                result.Options[w] = new OptionValue(args[k + 1]);
                k++;
            }
            else if (valueOptions is not null && w.Length > 2 && valueOptions.Contains(w[..2]))
            {
                result.Options[w[..2]] = new OptionValue(w[2..]);
            }
            else
            {
                result.Options[w] = OptionValue.Flag;
            }
        }

        return result;
    }

    /// <summary>The word ends with <c>.</c> and 1 to 10 ASCII letters or digits.</summary>
    public static bool HasExtension(string word)
    {
        var dot = word.LastIndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        var length = word.Length - dot - 1;
        if (length is < 1 or > 10)
        {
            return false;
        }

        for (var i = dot + 1; i < word.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(word[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><i>Broad</i> under the path rule of 3.5.</summary>
    public static bool IsBroadPath(string arg, string? cwd)
    {
        var a = TaskText.CwdRelative(arg.Replace('\\', '/'), cwd, out _);
        while (a.StartsWith("./", StringComparison.Ordinal))
        {
            a = a[2..];
        }

        a = a.TrimEnd('/');
        if (a is "" or ".")
        {
            return true;
        }

        if (a.Contains('*', StringComparison.Ordinal) || a.Contains('?', StringComparison.Ordinal) || a.Contains("::", StringComparison.Ordinal)
            || a.StartsWith('/') || (a.Length >= 2 && char.IsAsciiLetter(a[0]) && a[1] == ':'))
        {
            return false;
        }

        return !a.Contains('/', StringComparison.Ordinal) && !HasExtension(a);
    }

    /// <summary>The path rule: some word is not broad.</summary>
    public static bool PathTargeted(IEnumerable<string> words, string? cwd)
    {
        foreach (var w in words)
        {
            if (!IsBroadPath(w, cwd))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The name rule: some word, without trailing <c>/</c>, is not empty, not <c>.</c> and not cwd itself.</summary>
    public static bool NameTargeted(IEnumerable<string> words, string? cwd)
    {
        foreach (var w in words)
        {
            if (NameTargets(w, cwd))
            {
                return true;
            }
        }

        return false;
    }

    public static bool NameTargets(string word, string? cwd)
    {
        var a = TaskText.CwdRelative(word.Replace('\\', '/').TrimEnd('/'), cwd, out _);
        return a is not ("" or ".");
    }
}
