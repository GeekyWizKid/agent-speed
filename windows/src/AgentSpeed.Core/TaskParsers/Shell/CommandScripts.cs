using static AgentSpeed.Core.TaskParsers.ShellWords;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>Package scripts and task runners (TASK-PARSERS 3.6).</summary>
internal static partial class CommandPrograms
{
    private static readonly Dictionary<string, string> ScriptAliases = new(StringComparer.Ordinal)
    {
        ["t"] = "test",
        ["tst"] = "test",
        ["tests"] = "test",
        ["type-check"] = "typecheck",
        ["types"] = "typecheck",
        ["compile"] = "build",
    };

    private static readonly Dictionary<string, CommandClass> CanonScripts = new(StringComparer.Ordinal)
    {
        ["test"] = Test,
        ["build"] = Build,
        ["lint"] = Check,
        ["typecheck"] = Check,
        ["check"] = Check,
    };

    private static readonly HashSet<string> TestWords = Set(
        "test", "tests", "spec", "specs", "unit", "e2e", "integration", "coverage", "cov", "jest", "vitest", "mocha", "pytest", "ctest",
        "playwright", "cypress");

    private static readonly HashSet<string> BuildWords = Set("build", "compile", "bundle", "dist", "all");

    private static readonly HashSet<string> CheckWords = Set(
        "lint", "typecheck", "types", "tsc", "check", "eslint", "ruff", "mypy", "pyright", "clippy", "vet", "analyze", "analyse", "verify",
        "validate");

    private static readonly HashSet<string> ForwardFilters = Set(
        "-t", "--testNamePattern", "-k", "--grep", "-g", "--filter", "-run", "--run", "--testPathPattern", "--spec", "--tests", "-E", "-R",
        "--findRelatedTests", "--only-testing", "-only-testing", "--skip", "--exact", "-p", "--package", "-m");

    private static readonly string[] ForwardFilterPrefixes = [.. ForwardFilters.Where(f => f.StartsWith("--", StringComparison.Ordinal)).Select(f => f + "=")];

    private static readonly HashSet<string> ForwardValue = Set(
        "--reporter", "-r", "--config", "-c", "--maxWorkers", "-w", "--timeout", "--testTimeout", "--env", "--outputFile",
        "--coverageReporters", "--pool", "--environment", "--seed", "--tb", "-n", "--workers", "-j", "--jobs");

    /// <summary><c>SCRIPT_LABEL</c>: each runner's test suite and the prefix of its other suites.</summary>
    internal static readonly Dictionary<string, (string Test, string Prefix)> ScriptLabel = new(StringComparer.Ordinal)
    {
        ["npm"] = ("npm test", "npm run "),
        ["pnpm"] = ("pnpm test", "pnpm run "),
        ["yarn"] = ("yarn test", "yarn run "),
        ["bun"] = ("bun run test", "bun run "),
        ["deno"] = ("deno task test", "deno task "),
        ["composer"] = ("composer test", "composer "),
        ["make"] = ("make test", "make "),
        ["ninja"] = ("ninja test", "ninja "),
        ["just"] = ("just test", "just "),
        ["task"] = ("task test", "task "),
        ["rake"] = ("rake test", "rake "),
        ["mise"] = ("mise run test", "mise run "),
        ["turbo"] = ("turbo test", "turbo "),
        ["nx"] = ("nx test", "nx "),
    };

    internal static readonly HashSet<string> NpmBuiltins = Set(
        "install", "i", "ci", "add", "uninstall", "remove", "rm", "update", "up", "outdated", "ls", "list", "link", "publish", "pack", "init",
        "create", "audit", "config", "cache", "version", "view", "info", "why", "explain", "dedupe", "prune", "rebuild", "start", "restart",
        "stop", "store", "fetch", "patch", "import", "deploy", "licenses", "doctor", "setup", "env", "approve-builds", "self-update",
        "workspaces", "workspace", "dlx", "exec", "x", "set", "plugin", "constraints", "unplug", "stage", "npm", "node", "upgrade", "pm",
        "repl", "help", "login", "logout", "whoami", "search", "owner", "access", "token", "team", "org", "profile", "star", "stars",
        "unstar", "fund", "sbom", "query", "run-script", "run", "rum", "urn", "test", "t", "tst", "build", "global");

    private static readonly HashSet<string> PmNarrow = Set("-w", "--workspace", "--filter", "-F");
    private static readonly HashSet<string> PmValue = Set("-w", "--workspace", "--filter", "-F", "--prefix", "-C", "--dir", "--cwd");
    private static readonly HashSet<string> NxValue = Set("-t", "--target", "--targets", "-p", "--projects");

    private static readonly HashSet<string> BunTestValue = Set(
        "-t", "--test-name-pattern", "--timeout", "--preload", "--coverage-reporter", "--reporter", "--rerun-each");

    private static readonly HashSet<string> DenoTestValue = Set(
        "--filter", "--allow-read", "--allow-net", "--allow-env", "--config", "-c", "--reporter", "--coverage", "--jobs", "--shuffle", "--seed");

    private static readonly Dictionary<string, string[]> RunnerSubcommands = new(StringComparer.Ordinal)
    {
        ["mise"] = ["run", "r"],
        ["deno"] = ["task"],
        ["turbo"] = ["run"],
        ["composer"] = ["run-script", "run"],
    };

    private static readonly HashSet<string> ComposerBuiltins = Set(
        "install", "update", "require", "remove", "dump-autoload", "dumpautoload", "show", "outdated", "validate", "create-project", "init",
        "config", "global", "exec", "search", "why", "why-not", "licenses", "audit", "status", "archive", "diagnose", "self-update",
        "depends", "prohibits", "fund", "reinstall", "bump", "check-platform-reqs", "clear-cache", "browse", "suggests");

    private static readonly HashSet<string> MakeFilterVars = Set(
        "TEST", "TESTS", "T", "TEST_ARGS", "TESTARGS", "TEST_FILTER", "TESTFILTER", "FILTER", "RUN", "ONLY", "K", "PKG", "PACKAGE", "FILE",
        "FILES", "SPEC", "ARGS", "PYTEST_ARGS", "PYTESTARGS", "TESTNAME", "CASE", "TESTOPTS");

    private static readonly Handler DenoTask = Runner("deno", Set());

    private static void AddScripts(Dictionary<string, Handler> t)
    {
        t["npm"] = PackageManager("npm");
        t["pnpm"] = PackageManager("pnpm");
        t["yarn"] = PackageManager("yarn");
        t["bun"] = PackageManager("bun");
        t["make"] = Runner("make", Set("-C", "--directory", "-f", "--file", "--makefile", "-I", "--include-dir", "-o", "--old-file", "-W", "--what-if", "-j", "-l"), (Build, "make"));
        t["ninja"] = Runner("ninja", Set("-C", "-f", "-j", "-k", "-l", "-d", "-t", "-w"), (Build, "ninja"));
        t["just"] = Runner("just", Set(
            "-f", "--justfile", "-d", "--working-directory", "--set", "--dotenv-path", "--dotenv-filename", "--shell", "--shell-arg", "--chooser",
            "--color", "--command", "--list-heading", "--list-prefix"));
        t["task"] = Runner("task", Set("-d", "--dir", "-t", "--taskfile", "-o", "--output", "-c", "--color"));
        t["rake"] = Runner("rake", Set("-f", "--rakefile", "-I", "--libdir", "-r", "--require", "-C", "--directory"));
        t["mise"] = Runner("mise", Set("-C", "--cd", "-E", "--env", "-j", "--jobs"));
        t["turbo"] = Runner("turbo", Set("--filter", "-F", "--concurrency", "--output-logs", "--cache-dir"));
        t["nx"] = Runner("nx", Set("-c", "--configuration", "--parallel", "--output-style"));
        t["composer"] = Runner("composer", Set("-d", "--working-dir"));
    }

    /// <summary>A script or target name's class and suite (3.6).</summary>
    private static (CommandClass Class, string? Suite) ScriptClass(string runner, string name)
    {
        var n = TaskText.LowerAscii(name);
        var canon = ScriptAliases.GetValueOrDefault(n, n);
        if (runner is "make" or "ninja" && canon == "check")
        {
            return (Test, runner == "make" ? "make check" : "ninja check");
        }

        if (runner is "make" or "ninja" && canon == "all")
        {
            return (Build, runner);
        }

        if (CanonScripts.TryGetValue(canon, out var cls))
        {
            var (testLabel, prefix) = ScriptLabel[runner];
            return (cls, canon == "test" ? testLabel : prefix + canon);
        }

        var cut = n.AsSpan().IndexOfAny(":._/-");
        var first = cut < 0 ? n : n[..cut];
        if (TestWords.Contains(first))
        {
            return (Test, null);
        }

        if (BuildWords.Contains(first))
        {
            return (Build, null);
        }

        return CheckWords.Contains(first) ? (Check, null) : (CommandClass.Other, null);
    }

    /// <summary>The forwarded-words rule (3.6).</summary>
    private static bool ForwardedTargeted(IReadOnlyList<string> args, int start, string? cwd)
    {
        var words = new List<string>(args.Count);
        for (var i = start; i < args.Count; i++)
        {
            if (args[i] != "--")
            {
                words.Add(args[i]);
            }
        }

        var k = 0;
        while (k < words.Count)
        {
            var w = words[k];
            if (ForwardFilters.Contains(w) || Array.Exists(ForwardFilterPrefixes, f => w.StartsWith(f, StringComparison.Ordinal)))
            {
                return true;
            }

            if (w.StartsWith('-'))
            {
                k += ForwardValue.Contains(w) ? 2 : 1;
                continue;
            }

            if (NameTargets(w, cwd))
            {
                return true;
            }

            k++;
        }

        return false;
    }

    private static bool IsWorkspaceSelector(string w) =>
        w.StartsWith("--workspace=", StringComparison.Ordinal) || w.StartsWith("--filter=", StringComparison.Ordinal);

    private static Handler PackageManager(string runner)
    {
        CommandClassification Handle(string[] args, string? cwd)
        {
            var k = 0;
            var narrow = false;
            while (k < args.Length && args[k].StartsWith('-'))
            {
                var w = args[k];
                if (PmValue.Contains(w))
                {
                    narrow = narrow || PmNarrow.Contains(w);
                    k += 2;
                    continue;
                }

                if (IsWorkspaceSelector(w))
                {
                    narrow = true;
                }

                k++;
            }

            var rest = From(args, k);
            if (rest.Length == 0)
            {
                return CommandClassification.Other;
            }

            var sub = rest[0];
            if (runner == "yarn" && sub == "workspace" && rest.Length >= 3)
            {
                var inner = Handle(rest[2..], cwd);
                return inner with { Targeted = true };
            }

            if (runner == "bun" && sub == "test")
            {
                var o = ParseOpts(rest, BunTestValue, start: 1);
                return R(Test, "bun test", o.Has("-t") || o.Has("--test-name-pattern") || NameTargeted(o.PositionalsAndAfter(), cwd));
            }

            if (runner == "bun" && sub == "build")
            {
                return R(Build, "bun build", false);
            }

            var dash = Array.IndexOf(rest, "--");
            var beforeCount = (dash >= 0 ? dash : rest.Length) - 1;
            var selects = false;
            for (var i = 1; i <= beforeCount; i++)
            {
                selects |= PmNarrow.Contains(rest[i]) || IsWorkspaceSelector(rest[i]);
            }

            if (selects)
            {
                narrow = true;
                var kept = new List<string>(rest.Length);
                for (var i = 0; i < rest.Length; i++)
                {
                    var w = rest[i];
                    var drop = PmNarrow.Contains(w) || (i > 0 && PmNarrow.Contains(rest[i - 1]) && i <= beforeCount) || IsWorkspaceSelector(w);
                    if (!drop)
                    {
                        kept.Add(w);
                    }
                }

                rest = [.. kept];
            }

            string name;
            int forwardFrom;
            if (sub is "test" or "t" or "tst")
            {
                name = "test";
                forwardFrom = 1;
            }
            else if (sub is "run" or "run-script" or "rum" or "urn")
            {
                if (rest.Length < 2)
                {
                    return CommandClassification.Other;
                }

                var j = DropOptions(rest, 1, PmNarrow, stopDashDash: false);
                if (j >= rest.Length)
                {
                    return CommandClassification.Other;
                }

                name = rest[j];
                forwardFrom = j + 1;
            }
            else if (runner is "pnpm" or "yarn" or "bun" && !NpmBuiltins.Contains(sub) && !HasExtension(sub))
            {
                var p = Prog(sub);
                if (Table.ContainsKey(p) && !ScriptLabel.ContainsKey(p))
                {
                    return Classify(rest, cwd);
                }

                name = sub;
                forwardFrom = 1;
            }
            else
            {
                return CommandClassification.Other;
            }

            var (cls, suite) = ScriptClass(runner, name);
            if (cls == CommandClass.Other)
            {
                return CommandClassification.Other;
            }

            return R(cls, suite, narrow || ForwardedTargeted(rest, forwardFrom, cwd));
        }

        return Handle;
    }

    /// <summary>make, ninja, just, task, rake, mise run, turbo, nx, composer, deno task: targets map like scripts.</summary>
    private static Handler Runner(string runner, HashSet<string> valueOptions, (CommandClass Class, string Suite)? noTarget = null) =>
        (args, cwd) =>
        {
            var rest = args;
            if (rest.Length > 0 && RunnerSubcommands.TryGetValue(runner, out var subs) && Array.IndexOf(subs, rest[0]) >= 0)
            {
                rest = rest[1..];
            }

            if (runner == "nx" && rest.Length > 0 && rest[0] is "run-many" or "affected")
            {
                var o = ParseOpts(rest, NxValue, start: 1);
                var target = o.FirstTruthy("-t", "--target", "--targets");
                if (target is not { IsText: true })
                {
                    return CommandClassification.Other;
                }

                var text = target.Value.Text!;
                var comma = text.IndexOf(',', StringComparison.Ordinal);
                var (cls, suite) = ScriptClass(runner, comma < 0 ? text : text[..comma]);
                return cls != CommandClass.Other ? R(cls, suite, o.FirstTruthy("-p", "--projects") is { IsText: true }) : CommandClassification.Other;
            }

            if (runner == "nx" && rest.Length >= 2 && rest[0] == "run" && rest[1].Contains(':', StringComparison.Ordinal))
            {
                var parts = rest[1].Split(':');
                var (cls, suite) = ScriptClass(runner, parts[1]);
                return cls != CommandClass.Other ? R(cls, suite, true) : CommandClassification.Other;
            }

            var positionals = new List<string>();
            var assignments = new List<string>();
            var k = 0;
            while (k < rest.Length)
            {
                var w = rest[k];
                if (w.StartsWith('-'))
                {
                    k += valueOptions.Contains(w) ? (runner == "just" && w == "--set" ? 3 : 2) : 1;
                    continue;
                }

                if (IsAssignment(w))
                {
                    assignments.Add(w[..w.IndexOf('=', StringComparison.Ordinal)]);
                }
                else
                {
                    positionals.Add(w);
                }

                k++;
            }

            if (runner == "composer" && positionals.Count > 0 && ComposerBuiltins.Contains(positionals[0]))
            {
                return CommandClassification.Other;
            }

            if (positionals.Count == 0)
            {
                return noTarget is { } fallback ? R(fallback.Class, fallback.Suite, false) : CommandClassification.Other;
            }

            CommandClass best;
            string? bestSuite;
            bool narrow;
            if (runner is "make" or "ninja" or "rake")
            {
                (best, bestSuite) = ScriptClass(runner, positionals[0]);
                for (var i = 1; i < positionals.Count; i++)
                {
                    var (cls, suite) = ScriptClass(runner, positionals[i]);
                    if (cls > best)
                    {
                        (best, bestSuite) = (cls, suite);
                    }
                }

                narrow = assignments.Exists(a => MakeFilterVars.Contains(TaskText.UpperAscii(a)));
            }
            else
            {
                (best, bestSuite) = ScriptClass(runner, positionals[0]);
                narrow = positionals.Count > 1 && (runner is "just" or "nx" || ForwardedTargeted(positionals, 1, cwd));
            }

            return best != CommandClass.Other ? R(best, bestSuite, narrow) : CommandClassification.Other;
        };

    private static CommandClassification Deno(string[] args, string? cwd)
    {
        if (args.Length == 0)
        {
            return CommandClassification.Other;
        }

        switch (args[0])
        {
            case "test":
                var o = ParseOpts(args, DenoTestValue, start: 1);
                return R(Test, "deno test", o.Has("--filter") || PathTargeted(o.PositionalsAndAfter(), cwd));
            case "check":
                return R(Check, "deno check", false);
            case "lint":
                return R(Check, "deno lint", false);
            case "fmt" when Array.IndexOf(args, "--check", 1) >= 0:
                return R(Check, "deno fmt", false);
            case "task":
                return DenoTask(args, cwd);
            default:
                return CommandClassification.Other;
        }
    }
}
