using static AgentSpeed.Core.TaskParsers.ShellWords;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>The class, suite and narrowing of one segment's run (TASK-PARSERS 3.5).</summary>
internal readonly record struct CommandClassification(CommandClass Class, string? Suite, bool Targeted)
{
    public static CommandClassification Other => default;
}

/// <summary>Programs with a rule of their own (TASK-PARSERS 3.5, Appendix A.3).</summary>
internal static partial class CommandPrograms
{
    internal delegate CommandClassification Handler(string[] args, string? cwd);

    private static CommandClassification R(CommandClass cls, string? suite, bool targeted) => new(cls, suite, targeted);

    private const CommandClass Test = CommandClass.Test;
    private const CommandClass Build = CommandClass.Build;
    private const CommandClass Check = CommandClass.Check;

    private static readonly HashSet<string> UnittestValue = Set("-s", "-t", "-p", "-k", "--start-directory", "--top-level-directory", "--pattern");

    private static readonly HashSet<string> SwiftTestValue = Set(
        "--filter", "--skip", "-s", "--specifier", "-c", "--configuration", "--package-path", "--scratch-path", "-j", "--jobs",
        "--num-workers", "--xunit-output", "--test-product", "--sanitize", "--triple", "--sdk");

    private static readonly string[] SwiftTestFilters = ["--filter", "--skip", "-s", "--specifier"];
    private static readonly string[] SwiftBuildNarrow = ["--target", "--product"];

    private static readonly HashSet<string> SwiftBuildValue = Set(
        "--target", "--product", "-c", "--configuration", "--package-path", "--scratch-path", "-j", "--jobs", "--triple", "--sdk", "--arch");

    private static readonly HashSet<string> DotnetValue = Set(
        "--filter", "-c", "--configuration", "-f", "--framework", "-r", "--runtime", "-o", "--output", "-v", "--verbosity", "-s",
        "--settings", "-l", "--logger", "--results-directory", "-a", "--test-adapter-path", "--collect", "-p", "--property");

    private static readonly HashSet<string> CmakeValue = Set("--build", "--target", "-t", "-j", "--parallel", "--config");
    private static readonly HashSet<string> TscNotCheck = Set("--watch", "-w", "--version", "-v", "--init", "--help", "-h", "--showConfig");

    private static readonly HashSet<string> TscValue = Set(
        "-p", "--project", "--outDir", "--target", "--module", "--lib", "--jsx", "--moduleResolution", "--rootDir", "--declarationDir",
        "--tsBuildInfoFile");

    private static readonly HashSet<string> BazelValue = Set("--config", "--test_filter", "--test_output", "--jobs", "-j");

    private static readonly HashSet<string> MesonValue = Set(
        "-C", "--suite", "--no-suite", "--num-processes", "--repeat", "--timeout-multiplier", "--setup", "--wrapper");

    private static readonly HashSet<string> DjangoValue = Set("-k", "--settings", "--parallel", "--tag", "--exclude-tag", "--pattern", "-p", "-v", "--verbosity");

    private static readonly HashSet<string> NodeTestValue = Set(
        "--test-name-pattern", "--test-reporter", "--test-reporter-destination", "--test-concurrency", "--require", "-r", "--import", "--loader");

    private static readonly HashSet<string> PytestValue = Set(
        "-k", "-m", "-p", "-c", "-o", "-n", "-W", "-r", "--maxfail", "--tb", "--rootdir", "--basetemp", "--junitxml", "--junit-xml",
        "--durations", "--deselect", "--ignore", "--ignore-glob", "--log-level", "--log-cli-level", "--timeout", "--confcutdir",
        "--import-mode", "--dist", "--cov", "--cov-report", "--cov-config", "--cov-fail-under", "--reruns", "--reruns-delay", "--count",
        "-p", "--override-ini", "--capture", "--html", "--randomly-seed");

    private static readonly string[] PytestFilters = ["-k", "-m", "--lf", "--last-failed", "--sw", "--stepwise", "--deselect", "--stepwise-skip"];
    private static readonly HashSet<string> CargoGlobalValue = Set("--color", "--config", "-Z", "-C", "--manifest-path");

    private static readonly HashSet<string> CargoValue = Set(
        "-p", "--package", "--test", "--bin", "--example", "--bench", "-F", "--features", "--target", "--target-dir", "--manifest-path",
        "-j", "--jobs", "--profile", "--exclude", "--color", "--message-format", "--config", "-Z", "--timings");

    private static readonly string[] CargoNarrow = ["-p", "--package", "--test", "--lib", "--bin", "--bins", "--example", "--examples", "--doc", "--bench", "--benches"];
    private static readonly HashSet<string> LibtestValue = Set("--skip", "--test-threads", "--format", "--color", "-Z", "--logfile", "--report-time");

    private static readonly HashSet<string> NextestValue = Set(
        "-E", "--filterset", "--filter-expr", "-p", "--package", "--test", "--bin", "--profile", "-P", "-j", "--test-threads", "--retries",
        "--partition", "--archive-file", "--cargo-profile", "-F", "--features", "--target-dir", "--manifest-path");

    private static readonly string[] NextestNarrow = ["-E", "--filterset", "--filter-expr", "-p", "--package", "--test", "--lib", "--bin"];

    private static readonly HashSet<string> XcodeActions = Set(
        "build", "test", "build-for-testing", "test-without-building", "analyze", "archive", "clean", "install", "installsrc", "docbuild");

    private static readonly HashSet<string> XcodeFlags = Set(
        "-quiet", "-verbose", "-json", "-allowProvisioningUpdates", "-allowProvisioningDeviceRegistration", "-skipUnavailableActions",
        "-hideShellScriptEnvironment", "-retry-tests-on-failure", "-skipPackagePluginValidation", "-skipMacroValidation",
        "-disableAutomaticPackageResolution", "-onlyUsePackageVersionsFromResolvedFile", "-showTestPlans", "-dry-run",
        "-UseModernBuildSystem", "-enableCodeCoverage", "-parallel-testing-enabled");

    private static readonly HashSet<string> XcodeInfo = Set(
        "-list", "-showBuildSettings", "-version", "-showsdks", "-showdestinations", "-usage", "-help", "-resolvePackageDependencies",
        "-showTestPlans", "-showComponent");

    private static readonly HashSet<string> GoTestValue = Set(
        "-run", "-skip", "-bench", "-count", "-timeout", "-tags", "-p", "-parallel", "-cpu", "-coverprofile", "-coverpkg", "-covermode",
        "-o", "-exec", "-benchtime", "-blockprofile", "-cpuprofile", "-memprofile", "-mutexprofile", "-outputdir", "-trace", "-shuffle",
        "-fuzz", "-fuzztime", "-vet", "-ldflags", "-gcflags", "-mod", "-modfile", "-C", "-asmflags", "-buildmode", "-compiler",
        "-gccgoflags", "-installsuffix", "-pkgdir", "-toolexec", "-overlay", "-pgo");

    private static readonly HashSet<string> GoBroad = Set("./...", "...", "all", ".");

    private static readonly HashSet<string> GradleValue = Set(
        "-p", "--project-dir", "-b", "--build-file", "-c", "--settings-file", "-x", "--exclude-task", "--tests", "--max-workers", "-I",
        "--init-script", "-g", "--gradle-user-home", "--console", "--warning-mode", "--priority", "-D", "-P", "--include-build");

    private static readonly Dictionary<string, string> GradleSuites = new(StringComparer.Ordinal)
    {
        ["test"] = "gradle test",
        ["check"] = "gradle check",
        ["build"] = "gradle build",
        ["assemble"] = "gradle assemble",
        ["lint"] = "gradle lint",
    };

    private static readonly HashSet<string> MvnTest = Set("test", "integration-test", "verify", "install", "deploy", "package");
    private static readonly HashSet<string> MvnBuild = Set("compile", "test-compile", "process-classes");
    private static readonly HashSet<string> MvnSkip = Set("-DskipTests", "-DskipTests=true", "-Dmaven.test.skip=true", "-Dmaven.test.skip");

    private static readonly HashSet<string> MvnValue = Set(
        "-pl", "--projects", "-f", "--file", "-s", "--settings", "-P", "--activate-profiles", "-T", "--threads", "-rf", "--resume-from",
        "-gs", "--global-settings", "-t", "--toolchains", "-l", "--log-file");

    private static readonly HashSet<string> CtestValue = Set(
        "--test-dir", "-C", "--build-config", "-j", "--parallel", "--timeout", "--output-log", "-R", "--tests-regex", "-E",
        "--exclude-regex", "-L", "--label-regex", "-LE", "--label-exclude", "-I", "--tests-information", "--repeat", "--stop-time", "-O",
        "--resource-spec-file", "--repeat-until-fail");

    private static readonly string[] CtestFilters =
        ["-R", "--tests-regex", "-E", "--exclude-regex", "-L", "--label-regex", "-LE", "--label-exclude", "-I", "--tests-information", "--rerun-failed"];

    private static readonly HashSet<string> JestValue = Set(
        "-c", "--config", "-w", "--maxWorkers", "--testTimeout", "--reporters", "--coverageReporters", "--rootDir", "--roots", "--env",
        "--testEnvironment", "--outputFile", "--seed", "-t", "--testNamePattern", "--testPathPattern", "--testPathPatterns",
        "--testPathIgnorePatterns", "--changedSince", "--selectProjects", "--shard", "--coverageDirectory", "--collectCoverageFrom", "--json");

    private static readonly string[] JestFilters =
    [
        "-t", "--testNamePattern", "--testPathPattern", "--testPathPatterns", "--findRelatedTests", "-o", "--onlyChanged", "--changedSince",
        "--lastCommit", "--selectProjects", "--shard", "--onlyFailures", "-f",
    ];

    private static readonly HashSet<string> VitestValue = Set(
        "-c", "--config", "--root", "-r", "--dir", "--reporter", "--outputFile", "--environment", "--pool", "--project", "--shard", "-t",
        "--testNamePattern", "--maxWorkers", "--minWorkers", "--testTimeout", "--mode", "--exclude");

    private static readonly string[] VitestFilters = ["-t", "--testNamePattern", "--changed", "--shard", "--project"];
    private static readonly HashSet<string> VitestSubcommands = Set("run", "watch", "dev", "related", "bench", "list", "init", "typecheck");

    private static readonly HashSet<string> MochaValue = Set(
        "-r", "--require", "-R", "--reporter", "-t", "--timeout", "-s", "--slow", "-u", "--ui", "--config", "--package", "--extension",
        "--ignore", "--file", "-g", "--grep", "-f", "--fgrep", "--spec", "--retries", "-j", "--jobs", "--reporter-option", "-O");

    private static readonly string[] MochaFilters = ["-g", "--grep", "-f", "--fgrep"];

    private static readonly HashSet<string> MypyValue = Set(
        "--config-file", "-p", "--package", "-m", "--module", "--python-version", "--cache-dir", "--exclude", "--follow-imports",
        "--platform", "--junit-xml", "--python-executable");

    private static readonly HashSet<string> PyrightValue = Set(
        "-p", "--project", "--pythonpath", "--pythonversion", "--level", "--threads", "--pythonplatform", "-t", "--typeshedpath", "--venvpath");

    private static readonly HashSet<string> RuffValue = Set(
        "--select", "--ignore", "--extend-select", "--extend-ignore", "--config", "--output-format", "--target-version", "--exclude",
        "--extend-exclude", "--line-length", "--per-file-ignores", "--cache-dir", "--stdin-filename", "--fixable", "--unfixable");

    private static readonly HashSet<string> EslintValue = Set(
        "-c", "--config", "--ext", "-f", "--format", "-o", "--output-file", "--rule", "--parser", "--resolve-plugins-relative-to",
        "--max-warnings", "--ignore-path", "--ignore-pattern", "--cache-location", "--env", "--global", "--plugin", "--rulesdir",
        "--parser-options", "--report-unused-disable-directives-severity", "--cache-strategy");

    private static readonly HashSet<string> RspecValue = Set(
        "-e", "--example", "-E", "--example-matches", "-t", "--tag", "-f", "--format", "-o", "--out", "-r", "--require", "-I", "--seed",
        "--order", "-P", "--pattern", "--exclude-pattern", "--default-path");

    private static readonly HashSet<string> PhpunitValue = Set(
        "--filter", "--group", "--exclude-group", "--testsuite", "-c", "--configuration", "--bootstrap", "--log-junit", "--order-by",
        "--cache-directory", "--coverage-html", "--coverage-clover", "--coverage-text", "--test-suffix", "--exclude-testsuite");

    private static readonly HashSet<string> RuffSubcommands = Set("check", "format", "rule", "config", "linter", "clean", "server", "analyze", "version");

    /// <summary>Every program with a rule, by its normalised name; <c>python</c> scripts are handled apart (<see cref="PythonScript"/>).</summary>
    public static readonly Dictionary<string, Handler> Table = BuildTable();

    private static Dictionary<string, Handler> BuildTable()
    {
        var t = new Dictionary<string, Handler>(StringComparer.Ordinal)
        {
            ["pytest"] = Pytest,
            ["unittest"] = Unittest,
            ["cargo"] = Cargo,
            ["swift"] = Swift,
            ["swiftc"] = Named(Build, "swiftc"),
            ["xcodebuild"] = Xcodebuild,
            ["go"] = Go,
            ["dotnet"] = Dotnet,
            ["msbuild"] = Named(Build, "msbuild"),
            ["vstest.console"] = Named(Test, "vstest"),
            ["gradle"] = Gradle,
            ["mvn"] = Mvn,
            ["javac"] = Named(Build, "javac"),
            ["kotlinc"] = Named(Build, "kotlinc"),
            ["cc"] = Cc,
            ["cmake"] = Cmake,
            ["ctest"] = Ctest,
            ["jest"] = Jest,
            ["vitest"] = Vitest,
            ["mocha"] = Mocha,
            ["ava"] = PathTool(Test, "ava", Set("--match", "-m", "--timeout", "-T"), ["--match", "-m"]),
            ["tap"] = PathTool(Test, "tap", Set("--grep", "-g", "--reporter", "-R"), ["--grep", "-g"]),
            ["node"] = Node,
            ["playwright"] = Sub(
                new() { ["test"] = new(Test, "playwright test", ["-g", "--grep", "--last-failed", "--only-changed"], true) },
                Set("-g", "--grep", "--project", "-c", "--config", "--reporter", "--workers", "-j", "--shard", "--retries", "--timeout", "--grep-invert")),
            ["cypress"] = Sub(
                new() { ["run"] = new(Test, "cypress run", ["--spec", "-s"], false) },
                Set("--spec", "-s", "--browser", "-b", "--config", "-c", "--env", "-e", "--project", "-P")),
            ["tsc"] = Tsc("tsc"),
            ["vue-tsc"] = Tsc("vue-tsc"),
            ["eslint"] = PathTool(Check, "eslint", EslintValue),
            ["oxlint"] = PathTool(Check, "oxlint", Set("-c", "--config", "-A", "-D", "-W", "--format", "-f")),
            ["biome"] = Sub(
                new()
                {
                    ["check"] = new(Check, "biome", [], true),
                    ["lint"] = new(Check, "biome", [], true),
                    ["ci"] = new(Check, "biome", [], true),
                },
                Set("--config-path", "--reporter", "--max-diagnostics")),
            ["prettier"] = CheckFlag("prettier", ["--check", "-c", "--list-different", "-l"], Set("--config", "--ignore-path", "--plugin", "--parser")),
            ["stylelint"] = PathTool(Check, "stylelint", Set("--config", "-c", "--formatter", "-f", "--ignore-path")),
            ["svelte-check"] = Named(Check, "svelte-check"),
            ["astro"] = Sub(new() { ["check"] = new(Check, "astro check", [], false) }),
            ["ruff"] = Ruff,
            ["mypy"] = PathTool(Check, "mypy", MypyValue),
            ["dmypy"] = Named(Check, "mypy"),
            ["pyright"] = PathTool(Check, "pyright", PyrightValue),
            ["basedpyright"] = PathTool(Check, "basedpyright", PyrightValue),
            ["flake8"] = PathTool(Check, "flake8", Set("--config", "--select", "--ignore", "--extend-ignore", "--max-line-length", "--exclude", "--format")),
            ["pylint"] = PathTool(Check, "pylint", Set("--rcfile", "--disable", "--enable", "-j", "--jobs", "--output-format", "--load-plugins")),
            ["black"] = CheckFlag("black", ["--check", "--diff"], Set("--config", "-l", "--line-length", "-t", "--target-version", "--include", "--exclude")),
            ["isort"] = CheckFlag("isort", ["--check", "--check-only", "-c", "--diff"], Set("--settings-path", "--sp", "--profile")),
            ["tox"] = PosArgs("tox"),
            ["nox"] = PosArgs("nox"),
            ["rspec"] = PathTool(Test, "rspec", RspecValue, ["-e", "--example", "-E", "--example-matches", "-t", "--tag", "--only-failures", "--next-failure", "-P", "--pattern"]),
            ["rails"] = Sub(
                new() { ["test"] = new(Test, "rails test", ["-n", "--name"], true) },
                Set("-n", "--name", "-e", "--environment", "--seed")),
            ["rubocop"] = PathTool(Check, "rubocop", Set("-c", "--config", "-f", "--format", "--only", "--except", "--require", "-r", "-o", "--out")),
            ["phpunit"] = PathTool(Test, "phpunit", PhpunitValue, ["--filter", "--group", "--testsuite"]),
            ["pest"] = PathTool(Test, "pest", PhpunitValue, ["--filter", "--group", "--testsuite"]),
            ["phpstan"] = Sub(
                new()
                {
                    ["analyse"] = new(Check, "phpstan", [], true),
                    ["analyze"] = new(Check, "phpstan", [], true),
                },
                Set("-c", "--configuration", "-l", "--level", "--memory-limit", "--error-format")),
            ["psalm"] = PathTool(Check, "psalm", Set("-c", "--config", "--threads", "--output-format")),
            ["mix"] = Sub(
                new()
                {
                    ["test"] = new(Test, "mix test", ["--only", "--failed", "--stale", "--include"], true),
                    ["compile"] = new(Build, "mix compile", [], false),
                    ["credo"] = new(Check, "mix credo", [], false),
                    ["dialyzer"] = new(Check, "mix dialyzer", [], false),
                },
                Set("--only", "--include", "--exclude", "--seed", "--max-failures", "--partitions")),
            ["zig"] = Sub(new()
            {
                ["test"] = new(Test, "zig test", [], false),
                ["build"] = new(Build, "zig build", [], false),
            }),
            ["stack"] = Sub(
                new()
                {
                    ["test"] = new(Test, "stack test", ["--ta", "--test-arguments"], false),
                    ["build"] = new(Build, "stack build", [], false),
                },
                Set("--ta", "--test-arguments")),
            ["cabal"] = Sub(
                new()
                {
                    ["test"] = new(Test, "cabal test", ["--test-options", "--test-option"], false),
                    ["build"] = new(Build, "cabal build", [], false),
                },
                Set("--test-options", "--test-option")),
            ["dart"] = Sub(
                new()
                {
                    ["test"] = new(Test, "dart test", ["-n", "--name", "-N", "--plain-name", "-t", "--tags"], true),
                    ["analyze"] = new(Check, "dart analyze", [], false),
                },
                Set("-n", "--name", "-N", "--plain-name", "-t", "--tags", "-p", "--platform", "-j", "--concurrency", "-r", "--reporter")),
            ["flutter"] = Sub(
                new()
                {
                    ["test"] = new(Test, "flutter test", ["-n", "--name", "--plain-name", "-t", "--tags"], true),
                    ["analyze"] = new(Check, "flutter analyze", [], false),
                    ["build"] = new(Build, "flutter build", [], false),
                },
                Set("-n", "--name", "--plain-name", "-t", "--tags", "-r", "--reporter", "--flavor")),
            ["deno"] = Deno,
            ["golangci-lint"] = Sub(
                new() { ["run"] = new(Check, "golangci-lint", [], true) },
                Set("-c", "--config", "--timeout", "-E", "--enable", "-D", "--disable", "--out-format")),
            ["staticcheck"] = PathTool(Check, "staticcheck", Set("-checks", "-f", "-tags")),
            ["swiftlint"] = Swiftlint,
            ["swift-format"] = Sub(new() { ["lint"] = new(Check, "swift-format", [], false) }),
            ["clang-tidy"] = Named(Check, "clang-tidy"),
            ["cppcheck"] = Named(Check, "cppcheck"),
            ["shellcheck"] = Named(Check, "shellcheck"),
            ["hadolint"] = Named(Check, "hadolint"),
            ["terraform"] = Sub(new() { ["validate"] = new(Check, "terraform validate", [], false) }),
            ["pre-commit"] = Sub(
                new() { ["run"] = new(Check, "pre-commit", ["--files"], false) },
                Set("--files", "--hook-stage", "--from-ref", "--to-ref")),
            ["bazel"] = Bazel,
            ["meson"] = Meson,
            ["vite"] = Sub(new() { ["build"] = new(Build, "vite build", [], false) }),
            ["next"] = Sub(new()
            {
                ["build"] = new(Build, "next build", [], false),
                ["lint"] = new(Check, "next lint", [], false),
            }),
            ["nuxt"] = Sub(new() { ["build"] = new(Build, "nuxt build", [], false) }),
            ["ng"] = Sub(
                new()
                {
                    ["build"] = new(Build, "ng build", [], false),
                    ["test"] = new(Test, "ng test", ["--include"], false),
                    ["lint"] = new(Check, "ng lint", [], false),
                },
                Set("--include", "--configuration", "-c", "--project")),
            ["webpack"] = Named(Build, "webpack"),
            ["rollup"] = Named(Build, "rollup"),
            ["esbuild"] = Named(Build, "esbuild"),
            ["tsup"] = Named(Build, "tsup"),
            ["rustc"] = Named(Build, "rustc"),
        };
        AddScripts(t);
        return t;
    }

    /// <summary>The class of one segment's words after prefixes and runners are removed.</summary>
    public static CommandClassification Classify(string[] words, string? cwd)
    {
        if (words.Length == 0)
        {
            return CommandClassification.Other;
        }

        var p = Prog(words[0]);
        var args = words[1..];
        if (p == "python")
        {
            return PythonScript(args, cwd);
        }

        return Table.TryGetValue(p, out var handler) ? handler(args, cwd) : CommandClassification.Other;
    }

    private static CommandClassification Pytest(string[] args, string? cwd)
    {
        var o = ParseOpts(args, PytestValue);
        return R(Test, "pytest", o.HasAny(PytestFilters) || PathTargeted(o.PositionalsAndAfter(), cwd));
    }

    private static CommandClassification Unittest(string[] args, string? cwd)
    {
        var o = ParseOpts(args, UnittestValue);
        return R(Test, "unittest", o.Positionals.Exists(p => p != "discover") || o.Has("-k"));
    }

    private static CommandClassification Cargo(string[] args, string? cwd)
    {
        var k = 0;
        while (k < args.Length && (args[k].StartsWith('-') || args[k].StartsWith('+')))
        {
            k += CargoGlobalValue.Contains(args[k]) ? 2 : 1;
        }

        if (k >= args.Length)
        {
            return CommandClassification.Other;
        }

        var sub = args[k];
        var rest = args[(k + 1)..];
        if (sub is "test" or "t")
        {
            var o = ParseOpts(rest, CargoValue);
            var a = ParseOpts(o.AfterDashDash, LibtestValue, stopDashDash: false);
            var targeted = o.HasAny(CargoNarrow) || o.Positionals.Count > 0 || a.Positionals.Count > 0 || a.Has("--exact") || a.Has("--skip");
            return R(Test, "cargo test", targeted);
        }

        if (sub == "nextest" && rest.Length > 0 && rest[0] is "run" or "r")
        {
            var o = ParseOpts(rest, NextestValue, start: 1);
            return R(Test, "cargo nextest", o.Positionals.Count > 0 || o.HasAny(NextestNarrow));
        }

        var narrow = ParseOpts(rest, CargoValue).HasAny(CargoNarrow);
        return sub switch
        {
            "build" or "b" or "rustc" => R(Build, "cargo build", narrow),
            "check" or "c" => R(Check, "cargo check", narrow),
            "clippy" => R(Check, "cargo clippy", narrow),
            "fmt" when Array.IndexOf(rest, "--check") >= 0 => R(Check, "cargo fmt", false),
            _ => CommandClassification.Other,
        };
    }

    private static CommandClassification Swift(string[] args, string? cwd)
    {
        if (args.Length == 0)
        {
            return CommandClassification.Other;
        }

        return args[0] switch
        {
            "test" => R(Test, "swift test", ParseOpts(args, SwiftTestValue, start: 1).HasAny(SwiftTestFilters)),
            "build" => R(Build, "swift build", ParseOpts(args, SwiftBuildValue, start: 1).HasAny(SwiftBuildNarrow)),
            _ => CommandClassification.Other,
        };
    }

    private static CommandClassification Xcodebuild(string[] args, string? cwd)
    {
        var actions = new List<string>();
        var narrow = false;
        var k = 0;
        while (k < args.Length)
        {
            var w = args[k];
            if (XcodeInfo.Contains(w))
            {
                return CommandClassification.Other;
            }

            if (w.StartsWith("-only-testing", StringComparison.Ordinal) || w.StartsWith("-skip-testing", StringComparison.Ordinal))
            {
                narrow = true;
                k += w.Contains(':', StringComparison.Ordinal) ? 1 : 2;
                continue;
            }

            if (w.StartsWith('-'))
            {
                k += XcodeFlags.Contains(w) || w.Contains(':', StringComparison.Ordinal) || w.Contains('=', StringComparison.Ordinal) ? 1 : 2;
                continue;
            }

            if (XcodeActions.Contains(w))
            {
                actions.Add(w);
            }

            k++;
        }

        if (actions.Contains("test") || actions.Contains("test-without-building"))
        {
            return R(Test, "xcodebuild test", narrow);
        }

        if (actions.Contains("analyze"))
        {
            return R(Check, "xcodebuild analyze", false);
        }

        if (actions.Count > 0 && actions.TrueForAll(a => a == "clean"))
        {
            return CommandClassification.Other;
        }

        return R(Build, "xcodebuild build", false);
    }

    private static CommandClassification Go(string[] args, string? cwd)
    {
        if (args.Length == 0)
        {
            return CommandClassification.Other;
        }

        var sub = args[0];
        var rest = args[1..];
        var argsAt = Array.IndexOf(rest, "-args");
        if (argsAt >= 0)
        {
            rest = rest[..argsAt];
        }

        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i].StartsWith("--", StringComparison.Ordinal) && rest[i].Length > 2)
            {
                rest[i] = rest[i][1..];
            }
        }

        var o = ParseOpts(rest, GoTestValue, stopDashDash: false);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in o.Options.Keys)
        {
            var eq = name.IndexOf('=', StringComparison.Ordinal);
            names.Add(eq < 0 ? name : name[..eq]);
        }

        var narrow = o.Positionals.Exists(p => !GoBroad.Contains(p));
        return sub switch
        {
            "test" => R(Test, "go test", narrow || names.Contains("-run") || names.Contains("-skip") || names.Contains("-fuzz")),
            "build" or "install" => R(Build, "go build", narrow),
            "vet" => R(Check, "go vet", narrow),
            _ => CommandClassification.Other,
        };
    }

    private static CommandClassification Dotnet(string[] args, string? cwd)
    {
        if (args.Length == 0)
        {
            return CommandClassification.Other;
        }

        var sub = args[0];
        var o = ParseOpts(args, DotnetValue, start: 1);
        return sub switch
        {
            "test" => R(Test, "dotnet test", o.Has("--filter")),
            "build" => R(Build, "dotnet build", false),
            "publish" => R(Build, "dotnet publish", false),
            "pack" => R(Build, "dotnet pack", false),
            "msbuild" => R(Build, "dotnet msbuild", false),
            "format" when o.Has("--verify-no-changes") => R(Check, "dotnet format", false),
            _ => CommandClassification.Other,
        };
    }

    private static CommandClass GradleTaskClass(string name)
    {
        // test[A-Z0-9].* as a full match: `.` stops at LF.
        if (name == "test" || (name.Length > 4 && name.StartsWith("test", StringComparison.Ordinal) && (char.IsAsciiLetterUpper(name[4]) || char.IsAsciiDigit(name[4]))
                && !name.Contains('\n', StringComparison.Ordinal))
            || name.EndsWith("Test", StringComparison.Ordinal) || name.EndsWith("Tests", StringComparison.Ordinal))
        {
            return Test;
        }

        if (name is "check" or "build")
        {
            return Test;
        }

        if (name is "assemble" or "jar" or "classes" or "installDist" or "bootJar" or "shadowJar"
            || name.StartsWith("assemble", StringComparison.Ordinal) || name.StartsWith("compile", StringComparison.Ordinal))
        {
            return Build;
        }

        if (name.StartsWith("lint", StringComparison.Ordinal) || name is "detekt" or "ktlintCheck" or "spotlessCheck"
            || name.StartsWith("checkstyle", StringComparison.Ordinal) || name.StartsWith("pmd", StringComparison.Ordinal)
            || name.StartsWith("spotbugs", StringComparison.Ordinal))
        {
            return Check;
        }

        return CommandClass.Other;
    }

    private static string AfterLastColon(string task)
    {
        var colon = task.LastIndexOf(':');
        return colon < 0 ? task : task[(colon + 1)..];
    }

    private static CommandClassification Gradle(string[] args, string? cwd)
    {
        var o = ParseOpts(args, GradleValue);
        var excludesTest = false;
        for (var k = 0; k < args.Length; k++)
        {
            var w = args[k];
            if (w is "-x" or "--exclude-task" && k + 1 < args.Length)
            {
                excludesTest |= AfterLastColon(args[k + 1]) == "test";
            }
            else if (w.StartsWith("--exclude-task=", StringComparison.Ordinal))
            {
                excludesTest |= AfterLastColon(w[(w.IndexOf('=', StringComparison.Ordinal) + 1)..]) == "test";
            }
        }

        var best = CommandClassification.Other;
        foreach (var task in o.Positionals)
        {
            var name = AfterLastColon(task);
            var cls = GradleTaskClass(name);
            if (cls == Test && name is "check" or "build" && excludesTest)
            {
                cls = Build;
            }

            if (cls > best.Class)
            {
                var module = task.Trim(':').Contains(':', StringComparison.Ordinal);
                best = R(cls, GradleSuites.GetValueOrDefault(name), module || o.Has("--tests"));
            }
        }

        return best;
    }

    private static CommandClassification Mvn(string[] args, string? cwd)
    {
        var o = ParseOpts(args, MvnValue);
        var skip = false;
        var narrow = o.Has("-pl") || o.Has("--projects");
        foreach (var w in args)
        {
            skip |= MvnSkip.Contains(w);
            narrow |= w.StartsWith("-Dtest=", StringComparison.Ordinal) || w.StartsWith("-Dit.test=", StringComparison.Ordinal);
        }

        var best = CommandClassification.Other;
        foreach (var goal in o.Positionals)
        {
            CommandClass cls;
            if (MvnTest.Contains(goal) || goal == "surefire:test")
            {
                cls = skip ? Build : Test;
            }
            else if (MvnBuild.Contains(goal))
            {
                cls = Build;
            }
            else if (goal.EndsWith(":check", StringComparison.Ordinal))
            {
                cls = Check;
            }
            else
            {
                continue;
            }

            if (cls > best.Class)
            {
                best = R(cls, MvnTest.Contains(goal) || MvnBuild.Contains(goal) ? "mvn " + goal : null, narrow);
            }
        }

        return best;
    }

    private static CommandClassification Cc(string[] args, string? cwd)
    {
        if (Array.IndexOf(args, "-E") >= 0 || Array.IndexOf(args, "--version") >= 0 || (args.Length > 0 && args[0] == "-v"))
        {
            return CommandClassification.Other;
        }

        return Array.IndexOf(args, "-fsyntax-only") >= 0 ? R(Check, "cc", false) : R(Build, "cc", false);
    }

    private static CommandClassification Cmake(string[] args, string? cwd)
    {
        if (Array.IndexOf(args, "--build") < 0)
        {
            return CommandClassification.Other;
        }

        var target = ParseOpts(args, CmakeValue).FirstTruthy("--target", "-t");
        if (target is { Text: "test" or "check" or "RUN_TESTS" })
        {
            return R(Test, "cmake --build", true);
        }

        return R(Build, "cmake --build", target is { IsText: true });
    }

    private static CommandClassification Ctest(string[] args, string? cwd) =>
        R(Test, "ctest", ParseOpts(args, CtestValue).HasAny(CtestFilters));

    private static CommandClassification Jest(string[] args, string? cwd)
    {
        var o = ParseOpts(args, JestValue);
        return R(Test, "jest", o.HasAny(JestFilters) || NameTargeted(o.PositionalsAndAfter(), cwd));
    }

    private static CommandClassification Vitest(string[] args, string? cwd)
    {
        var sub = args.Length > 0 && VitestSubcommands.Contains(args[0]) ? args[0] : null;
        if (sub is "bench" or "list" or "init")
        {
            return CommandClassification.Other;
        }

        var o = ParseOpts(args, VitestValue, start: sub is null ? 0 : 1);
        return R(Test, "vitest", sub == "related" || o.HasAny(VitestFilters) || NameTargeted(o.PositionalsAndAfter(), cwd));
    }

    private static CommandClassification Mocha(string[] args, string? cwd)
    {
        var o = ParseOpts(args, MochaValue);
        if (o.Options.TryGetValue("--spec", out var spec) && spec.IsText)
        {
            o.Positionals.Add(spec.Text!);
        }

        return R(Test, "mocha", o.HasAny(MochaFilters) || PathTargeted(o.PositionalsAndAfter(), cwd));
    }

    private static Handler PathTool(CommandClass cls, string suite, HashSet<string>? valueOptions = null, string[]? filters = null) =>
        (args, cwd) =>
        {
            var o = ParseOpts(args, valueOptions);
            return R(cls, suite, (filters is not null && o.HasAny(filters)) || PathTargeted(o.PositionalsAndAfter(), cwd));
        };

    private static CommandClassification Ruff(string[] args, string? cwd)
    {
        var sub = args.Length > 0 && RuffSubcommands.Contains(args[0]) ? args[0] : null;
        var start = sub is null ? 0 : 1;
        if (sub is null or "check")
        {
            return R(Check, "ruff", PathTargeted(ParseOpts(args, RuffValue, start: start).PositionalsAndAfter(), cwd));
        }

        if (sub == "format" && HasAnyFrom(args, start, "--check", "--diff"))
        {
            return R(Check, "ruff format", PathTargeted(ParseOpts(args, RuffValue, start: start).PositionalsAndAfter(), cwd));
        }

        return CommandClassification.Other;
    }

    private static bool HasAnyFrom(string[] args, int start, params string[] words)
    {
        for (var i = start; i < args.Length; i++)
        {
            if (Array.IndexOf(words, args[i]) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Handler CheckFlag(string suite, string[] flags, HashSet<string> valueOptions) =>
        (args, cwd) => HasAnyFrom(args, 0, flags)
            ? R(Check, suite, PathTargeted(ParseOpts(args, valueOptions).PositionalsAndAfter(), cwd))
            : CommandClassification.Other;

    private static Handler Tsc(string name) =>
        (args, cwd) =>
        {
            foreach (var a in args)
            {
                if (TscNotCheck.Contains(a))
                {
                    return CommandClassification.Other;
                }
            }

            if (HasAnyFrom(args, 0, "-b", "--build"))
            {
                return R(Check, name, false);
            }

            return R(Check, name, PathTargeted(ParseOpts(args, TscValue).PositionalsAndAfter(), cwd));
        };

    /// <summary>A subcommand's class, suite, filter options and whether the path rule applies.</summary>
    internal sealed record SubRule(CommandClass Class, string Suite, string[] Filters, bool PathRule);

    private static Handler Sub(Dictionary<string, SubRule> table, HashSet<string>? valueOptions = null) =>
        (args, cwd) =>
        {
            if (args.Length == 0 || !table.TryGetValue(args[0], out var rule))
            {
                return CommandClassification.Other;
            }

            var o = ParseOpts(args, valueOptions, start: 1);
            return R(rule.Class, rule.Suite, o.HasAny(rule.Filters) || (rule.PathRule && PathTargeted(o.PositionalsAndAfter(), cwd)));
        };

    private static CommandClassification Bazel(string[] args, string? cwd)
    {
        if (args.Length == 0 || args[0] is not ("test" or "build"))
        {
            return CommandClassification.Other;
        }

        var o = ParseOpts(args, BazelValue, start: 1);
        var narrow = o.Positionals.Exists(p => p is not ("//..." or "..." or "//:all"))
            || Array.Exists(args, a => a.StartsWith("--test_filter", StringComparison.Ordinal));
        return args[0] == "test" ? R(Test, "bazel test", narrow) : R(Build, "bazel build", narrow);
    }

    private static CommandClassification Meson(string[] args, string? cwd)
    {
        if (args.Length == 0)
        {
            return CommandClassification.Other;
        }

        if (args[0] == "test")
        {
            var o = ParseOpts(args, MesonValue, start: 1);
            return R(Test, "meson test", o.Positionals.Count > 0 || o.Has("--suite"));
        }

        return args[0] == "compile" ? R(Build, "meson compile", false) : CommandClassification.Other;
    }

    /// <summary><c>python manage.py test</c> and <c>python setup.py test</c>; any other script is not a check.</summary>
    private static CommandClassification PythonScript(string[] args, string? cwd)
    {
        args = From(args, SkipUntilModule(args, PyValue));
        if (args.Length >= 2 && args[1] == "test")
        {
            var script = args[0].Replace('\\', '/');
            script = script[(script.LastIndexOf('/') + 1)..];
            if (script == "manage.py")
            {
                var o = ParseOpts(args, DjangoValue, start: 2);
                return R(Test, "django test", o.Positionals.Count > 0 || o.Has("-k") || o.Has("--tag"));
            }

            if (script == "setup.py")
            {
                return R(Test, "setup.py test", false);
            }
        }

        return CommandClassification.Other;
    }

    private static CommandClassification Node(string[] args, string? cwd)
    {
        if (Array.IndexOf(args, "--test") < 0)
        {
            return CommandClassification.Other;
        }

        var o = ParseOpts(Array.FindAll(args, a => a != "--test"), NodeTestValue);
        return R(Test, "node --test", o.Has("--test-name-pattern") || o.Has("--test-only") || PathTargeted(o.PositionalsAndAfter(), cwd));
    }

    private static Handler Named(CommandClass cls, string suite) => (_, _) => R(cls, suite, false);

    /// <summary>tox and nox: arguments after <c>--</c> go to the test runner and narrow the run.</summary>
    private static Handler PosArgs(string name) =>
        (args, _) =>
        {
            var dash = Array.IndexOf(args, "--");
            return R(Test, name, dash >= 0 && dash < args.Length - 1);
        };

    private static CommandClassification Swiftlint(string[] args, string? cwd)
    {
        if ((args.Length > 0 && args[0] is "autocorrect" or "analyze") || Array.IndexOf(args, "--fix") >= 0)
        {
            return CommandClassification.Other;
        }

        return R(Check, "swiftlint", false);
    }
}
