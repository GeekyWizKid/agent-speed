import Foundation

/// The class, suite and narrowing of one segment (TASK-PARSERS 3.5).
struct Classification: Equatable {
    var commandClass: TaskCommandClass
    var suite: String?
    var targeted: Bool

    static let other = Classification(.other, nil, false)

    init(_ commandClass: TaskCommandClass, _ suite: String?, _ targeted: Bool) {
        self.commandClass = commandClass
        self.suite = suite
        self.targeted = targeted
    }
}

typealias ProgramRule = @Sendable (_ args: [Bytes], _ cwd: String?) -> Classification

/// The programs with a rule of their own (TASK-PARSERS 3.5, A.3).
enum ProgramRules {
    static func classify(_ words: [Bytes], cwd: String?) -> Classification {
        guard let first = words.first else { return .other }
        let p = ShellCommand.program(first)
        let args = Array(words.dropFirst())
        if p == Bytes(utf8: "python") { return pythonScript(args, cwd) }
        guard let rule = rules[p] else { return .other }
        let result = rule(args, cwd)
        assert(result.suite == nil || TaskSuites.all.contains(result.suite!), "suite outside the vocabulary")
        return result
    }

    static func hasRule(_ program: Bytes) -> Bool { rules[program] != nil }

    // MARK: Generic rules (A.3)

    static func named(_ commandClass: TaskCommandClass, _ suite: String) -> ProgramRule {
        { _, _ in Classification(commandClass, suite, false) }
    }

    static func paths(_ commandClass: TaskCommandClass, _ suite: String, values: [String] = [],
                      filters: [String] = []) -> ProgramRule {
        let values = OptionList(values)
        let filters = TaskText.words(filters)
        return { args, cwd in
            let parsed = CommandOptions.parse(args, values: values)
            return Classification(commandClass, suite,
                                  parsed.hasAny(filters) || CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
        }
    }

    static func checkFlag(_ suite: String, flags: [String], values: [String]) -> ProgramRule {
        let values = OptionList(values)
        let flags = TaskText.words(flags)
        return { args, cwd in
            guard args.contains(where: { flags.contains($0) }) else { return .other }
            let parsed = CommandOptions.parse(args, values: values)
            return Classification(.check, suite, CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
        }
    }

    struct Subcommand {
        let commandClass: TaskCommandClass
        let suite: String
        let filters: [String]
        let pathRule: Bool

        init(_ commandClass: TaskCommandClass, _ suite: String, _ filters: [String] = [], paths: Bool = false) {
            self.commandClass = commandClass
            self.suite = suite
            self.filters = filters
            pathRule = paths
        }
    }

    static func subcommands(_ table: [String: Subcommand], values: [String] = []) -> ProgramRule {
        let values = OptionList(values)
        let table = table.reduce(into: [Bytes: (Subcommand, Set<Bytes>)]()) {
            $0[Bytes(utf8: $1.key)] = ($1.value, TaskText.words($1.value.filters))
        }
        return { args, cwd in
            guard let first = args.first, let (entry, filters) = table[first] else { return .other }
            let parsed = CommandOptions.parse(Array(args.dropFirst()), values: values)
            let targeted = parsed.hasAny(filters)
                || (entry.pathRule && CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
            return Classification(entry.commandClass, entry.suite, targeted)
        }
    }

    static func posargs(_ suite: String) -> ProgramRule {
        { args, _ in
            guard let index = args.firstIndex(of: CommandOptions.dashDash) else { return Classification(.test, suite, false) }
            return Classification(.test, suite, index < args.count - 1)
        }
    }

    // MARK: Own rules (3.5)

    static let pytestValue = OptionList([
        "-k", "-m", "-p", "-c", "-o", "-n", "-W", "-r", "--maxfail", "--tb", "--rootdir", "--basetemp", "--junitxml",
        "--junit-xml", "--durations", "--deselect", "--ignore", "--ignore-glob", "--log-level", "--log-cli-level",
        "--timeout", "--confcutdir", "--import-mode", "--dist", "--cov", "--cov-report", "--cov-config",
        "--cov-fail-under", "--reruns", "--reruns-delay", "--count", "-p", "--override-ini", "--capture", "--html",
        "--randomly-seed",
    ])
    static let pytestFilters = TaskText.words([
        "-k", "-m", "--lf", "--last-failed", "--sw", "--stepwise", "--deselect", "--stepwise-skip",
    ])

    static func pytest(_ args: [Bytes], _ cwd: String?) -> Classification {
        let parsed = CommandOptions.parse(args, values: pytestValue)
        return Classification(.test, "pytest",
                              parsed.hasAny(pytestFilters) || CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
    }

    static let unittestValue = OptionList([
        "-s", "-t", "-p", "-k", "--start-directory", "--top-level-directory", "--pattern",
    ])

    static func unittest(_ args: [Bytes], _ cwd: String?) -> Classification {
        let parsed = CommandOptions.parse(args, values: unittestValue)
        let positionals = parsed.positionals.filter { $0 != Bytes(utf8: "discover") }
        return Classification(.test, "unittest", !positionals.isEmpty || parsed.has("-k"))
    }

    static let cargoGlobalValue = OptionList(["--color", "--config", "-Z", "-C", "--manifest-path"])
    static let cargoValue = OptionList([
        "-p", "--package", "--test", "--bin", "--example", "--bench", "-F", "--features", "--target", "--target-dir",
        "--manifest-path", "-j", "--jobs", "--profile", "--exclude", "--color", "--message-format", "--config", "-Z",
        "--timings",
    ])
    static let cargoNarrow = TaskText.words([
        "-p", "--package", "--test", "--lib", "--bin", "--bins", "--example", "--examples", "--doc", "--bench",
        "--benches",
    ])
    static let libtestValue = OptionList([
        "--skip", "--test-threads", "--format", "--color", "-Z", "--logfile", "--report-time",
    ])
    static let nextestValue = OptionList([
        "-E", "--filterset", "--filter-expr", "-p", "--package", "--test", "--bin", "--profile", "-P", "-j",
        "--test-threads", "--retries", "--partition", "--archive-file", "--cargo-profile", "-F", "--features",
        "--target-dir", "--manifest-path",
    ])
    static let nextestNarrow = TaskText.words([
        "-E", "--filterset", "--filter-expr", "-p", "--package", "--test", "--lib", "--bin",
    ])

    static func cargo(_ args: [Bytes], _ cwd: String?) -> Classification {
        var k = 0
        while k < args.count, args[k].first == Byte.dash || args[k].first == 0x2B {
            k += cargoGlobalValue.takesValue(args[k]) ? 2 : 1
        }
        guard k < args.count else { return .other }
        let sub = args[k]
        let rest = Array(args[(k + 1)...])
        if sub == Bytes(utf8: "test") || sub == Bytes(utf8: "t") {
            let parsed = CommandOptions.parse(rest, values: cargoValue)
            let after = CommandOptions.parse(parsed.afterDashDash, values: libtestValue, stopAtDashDash: false)
            let targeted = parsed.hasAny(cargoNarrow) || !parsed.positionals.isEmpty || !after.positionals.isEmpty
                || after.has("--exact") || after.has("--skip")
            return Classification(.test, "cargo test", targeted)
        }
        if sub == Bytes(utf8: "nextest"), let mode = rest.first, mode == Bytes(utf8: "run") || mode == Bytes(utf8: "r") {
            let parsed = CommandOptions.parse(Array(rest.dropFirst()), values: nextestValue)
            return Classification(.test, "cargo nextest", !parsed.positionals.isEmpty || parsed.hasAny(nextestNarrow))
        }
        let parsed = CommandOptions.parse(rest, values: cargoValue)
        let narrow = parsed.hasAny(cargoNarrow)
        switch sub {
        case Bytes(utf8: "build"), Bytes(utf8: "b"), Bytes(utf8: "rustc"): return Classification(.build, "cargo build", narrow)
        case Bytes(utf8: "check"), Bytes(utf8: "c"): return Classification(.check, "cargo check", narrow)
        case Bytes(utf8: "clippy"): return Classification(.check, "cargo clippy", narrow)
        case Bytes(utf8: "fmt") where rest.contains(Bytes(utf8: "--check")): return Classification(.check, "cargo fmt", false)
        default: return .other
        }
    }

    static let swiftTestValue = OptionList([
        "--filter", "--skip", "-s", "--specifier", "-c", "--configuration", "--package-path", "--scratch-path", "-j",
        "--jobs", "--num-workers", "--xunit-output", "--test-product", "--sanitize", "--triple", "--sdk",
    ])
    static let swiftTestFilters = TaskText.words(["--filter", "--skip", "-s", "--specifier"])
    static let swiftBuildNarrow = TaskText.words(["--target", "--product"])
    static let swiftBuildValue = OptionList([
        "--target", "--product", "-c", "--configuration", "--package-path", "--scratch-path", "-j", "--jobs",
        "--triple", "--sdk", "--arch",
    ])

    static func swift(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard let sub = args.first else { return .other }
        let rest = Array(args.dropFirst())
        if sub == Bytes(utf8: "test") {
            return Classification(.test, "swift test",
                                  CommandOptions.parse(rest, values: swiftTestValue).hasAny(swiftTestFilters))
        }
        if sub == Bytes(utf8: "build") {
            return Classification(.build, "swift build",
                                  CommandOptions.parse(rest, values: swiftBuildValue).hasAny(swiftBuildNarrow))
        }
        return .other
    }

    static let xcodeActions = TaskText.words([
        "build", "test", "build-for-testing", "test-without-building", "analyze", "archive", "clean", "install",
        "installsrc", "docbuild",
    ])
    static let xcodeFlags = TaskText.words([
        "-quiet", "-verbose", "-json", "-allowProvisioningUpdates", "-allowProvisioningDeviceRegistration",
        "-skipUnavailableActions", "-hideShellScriptEnvironment", "-retry-tests-on-failure",
        "-skipPackagePluginValidation", "-skipMacroValidation", "-disableAutomaticPackageResolution",
        "-onlyUsePackageVersionsFromResolvedFile", "-showTestPlans", "-dry-run", "-UseModernBuildSystem",
        "-enableCodeCoverage", "-parallel-testing-enabled",
    ])
    static let xcodeInfo = TaskText.words([
        "-list", "-showBuildSettings", "-version", "-showsdks", "-showdestinations", "-usage", "-help",
        "-resolvePackageDependencies", "-showTestPlans", "-showComponent",
    ])

    static func xcodebuild(_ args: [Bytes], _ cwd: String?) -> Classification {
        var actions: [Bytes] = []
        var narrow = false
        var k = 0
        while k < args.count {
            let word = args[k]
            if xcodeInfo.contains(word) { return .other }
            if word.hasPrefix(Bytes(utf8: "-only-testing")) || word.hasPrefix(Bytes(utf8: "-skip-testing")) {
                narrow = true
                k += word.contains(Byte.colon) ? 1 : 2
                continue
            }
            if word.first == Byte.dash {
                k += xcodeFlags.contains(word) || word.contains(Byte.colon) || word.contains(Byte.equals) ? 1 : 2
                continue
            }
            if xcodeActions.contains(word) { actions.append(word) }
            k += 1
        }
        if actions.contains(Bytes(utf8: "test")) || actions.contains(Bytes(utf8: "test-without-building")) {
            return Classification(.test, "xcodebuild test", narrow)
        }
        if actions.contains(Bytes(utf8: "analyze")) { return Classification(.check, "xcodebuild analyze", false) }
        if !actions.isEmpty, actions.allSatisfy({ $0 == Bytes(utf8: "clean") }) { return .other }
        return Classification(.build, "xcodebuild build", false)
    }

    static let goTestValue = OptionList([
        "-run", "-skip", "-bench", "-count", "-timeout", "-tags", "-p", "-parallel", "-cpu", "-coverprofile",
        "-coverpkg", "-covermode", "-o", "-exec", "-benchtime", "-blockprofile", "-cpuprofile", "-memprofile",
        "-mutexprofile", "-outputdir", "-trace", "-shuffle", "-fuzz", "-fuzztime", "-vet", "-ldflags", "-gcflags",
        "-mod", "-modfile", "-C", "-asmflags", "-buildmode", "-compiler", "-gccgoflags", "-installsuffix", "-pkgdir",
        "-toolexec", "-overlay", "-pgo",
    ])
    static let goBroad = TaskText.words(["./...", "...", "all", "."])

    static func go(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard let sub = args.first else { return .other }
        var rest = Array(args.dropFirst())
        if let index = rest.firstIndex(of: Bytes(utf8: "-args")) { rest = Array(rest[..<index]) }
        let normalized = rest.map { $0.count > 2 && $0.hasPrefix(Bytes(utf8: "--")) ? Bytes($0.dropFirst()) : $0 }
        let parsed = CommandOptions.parse(normalized, values: goTestValue, stopAtDashDash: false)
        let names = Set(parsed.options.keys.map { $0.firstPart(byte: Byte.equals) })
        let narrow = parsed.positionals.contains { !goBroad.contains($0) }
        switch sub {
        case Bytes(utf8: "test"):
            let filtered = ["-run", "-skip", "-fuzz"].contains { names.contains(Bytes(utf8: $0)) }
            return Classification(.test, "go test", narrow || filtered)
        case Bytes(utf8: "build"), Bytes(utf8: "install"):
            return Classification(.build, "go build", narrow)
        case Bytes(utf8: "vet"):
            return Classification(.check, "go vet", narrow)
        default:
            return .other
        }
    }

    static let dotnetValue = OptionList([
        "--filter", "-c", "--configuration", "-f", "--framework", "-r", "--runtime", "-o", "--output", "-v",
        "--verbosity", "-s", "--settings", "-l", "--logger", "--results-directory", "-a", "--test-adapter-path",
        "--collect", "-p", "--property",
    ])

    static func dotnet(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard let sub = args.first else { return .other }
        let parsed = CommandOptions.parse(Array(args.dropFirst()), values: dotnetValue)
        switch sub {
        case Bytes(utf8: "test"):
            return Classification(.test, "dotnet test", parsed.has("--filter"))
        case Bytes(utf8: "build"), Bytes(utf8: "publish"), Bytes(utf8: "pack"), Bytes(utf8: "msbuild"):
            return Classification(.build, "dotnet " + sub.string, false)
        case Bytes(utf8: "format") where parsed.has("--verify-no-changes"):
            return Classification(.check, "dotnet format", false)
        default:
            return .other
        }
    }

    static let gradleValue = OptionList([
        "-p", "--project-dir", "-b", "--build-file", "-c", "--settings-file", "-x", "--exclude-task", "--tests",
        "--max-workers", "-I", "--init-script", "-g", "--gradle-user-home", "--console", "--warning-mode",
        "--priority", "-D", "-P", "--include-build",
    ])
    private static let gradleTestName = Pattern(#"test[A-Z0-9].*"#)
    private static let gradleSuites: [Bytes: String] = [
        "test": "gradle test", "check": "gradle check", "build": "gradle build", "assemble": "gradle assemble",
        "lint": "gradle lint",
    ].reduce(into: [:]) { $0[Bytes(utf8: $1.key)] = $1.value }

    static func gradleTaskClass(_ base: Bytes) -> TaskCommandClass {
        if base == Bytes(utf8: "test") || gradleTestName.fullMatches(base) || base.hasSuffix(Bytes(utf8: "Test"))
            || base.hasSuffix(Bytes(utf8: "Tests")) {
            return .test
        }
        if base == Bytes(utf8: "check") || base == Bytes(utf8: "build") { return .test }
        if ["assemble", "jar", "classes", "installDist", "bootJar", "shadowJar"].contains(where: { base == Bytes(utf8: $0) })
            || base.hasPrefix(Bytes(utf8: "assemble")) || base.hasPrefix(Bytes(utf8: "compile")) {
            return .build
        }
        if base.hasPrefix(Bytes(utf8: "lint")) || ["detekt", "ktlintCheck", "spotlessCheck"].contains(where: { base == Bytes(utf8: $0) })
            || base.hasPrefix(Bytes(utf8: "checkstyle")) || base.hasPrefix(Bytes(utf8: "pmd")) || base.hasPrefix(Bytes(utf8: "spotbugs")) {
            return .check
        }
        return .other
    }

    static func gradle(_ args: [Bytes], _ cwd: String?) -> Classification {
        let parsed = CommandOptions.parse(args, values: gradleValue)
        var excluded: [Bytes] = []
        for (k, word) in args.enumerated() {
            if word == Bytes(utf8: "-x") || word == Bytes(utf8: "--exclude-task"), k + 1 < args.count {
                excluded.append(args[k + 1].lastPart(byte: Byte.colon))
            } else if word.hasPrefix(Bytes(utf8: "--exclude-task=")) {
                excluded.append(Bytes(word.dropFirst("--exclude-task=".utf8.count)).lastPart(byte: Byte.colon))
            }
        }
        var best = Classification.other
        for task in parsed.positionals {
            let base = task.lastPart(byte: Byte.colon)
            var commandClass = gradleTaskClass(base)
            if commandClass == .test, base == Bytes(utf8: "check") || base == Bytes(utf8: "build"), excluded.contains(Bytes(utf8: "test")) {
                commandClass = .build
            }
            if commandClass.rank > best.commandClass.rank {
                let module = task.trimmed([Byte.colon]).contains(Byte.colon)
                best = Classification(commandClass, gradleSuites[base], module || parsed.has("--tests"))
            }
        }
        return best
    }

    static let mvnTest = TaskText.words(["test", "integration-test", "verify", "install", "deploy", "package"])
    static let mvnBuild = TaskText.words(["compile", "test-compile", "process-classes"])
    static let mvnSkip = TaskText.words(["-DskipTests", "-DskipTests=true", "-Dmaven.test.skip=true", "-Dmaven.test.skip"])
    static let mvnValue = OptionList([
        "-pl", "--projects", "-f", "--file", "-s", "--settings", "-P", "--activate-profiles", "-T", "--threads", "-rf",
        "--resume-from", "-gs", "--global-settings", "-t", "--toolchains", "-l", "--log-file",
    ])

    static func mvn(_ args: [Bytes], _ cwd: String?) -> Classification {
        let parsed = CommandOptions.parse(args, values: mvnValue)
        let skip = args.contains { mvnSkip.contains($0) }
        let narrow = parsed.has("-pl") || parsed.has("--projects")
            || args.contains { $0.hasPrefix(Bytes(utf8: "-Dtest=")) || $0.hasPrefix(Bytes(utf8: "-Dit.test=")) }
        var best = Classification.other
        for goal in parsed.positionals {
            let commandClass: TaskCommandClass
            if mvnTest.contains(goal) || goal == Bytes(utf8: "surefire:test") {
                commandClass = skip ? .build : .test
            } else if mvnBuild.contains(goal) {
                commandClass = .build
            } else if goal.hasSuffix(Bytes(utf8: ":check")) {
                commandClass = .check
            } else {
                continue
            }
            if commandClass.rank > best.commandClass.rank {
                let suite = mvnTest.contains(goal) || mvnBuild.contains(goal) ? "mvn " + goal.string : nil
                best = Classification(commandClass, suite, narrow)
            }
        }
        return best
    }

    static func cc(_ args: [Bytes], _ cwd: String?) -> Classification {
        if args.contains(Bytes(utf8: "-E")) || args.contains(Bytes(utf8: "--version")) || args.first == Bytes(utf8: "-v") { return .other }
        if args.contains(Bytes(utf8: "-fsyntax-only")) { return Classification(.check, "cc", false) }
        return Classification(.build, "cc", false)
    }

    static let cmakeValue = OptionList(["--build", "--target", "-t", "-j", "--parallel", "--config"])

    static func cmake(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard args.contains(Bytes(utf8: "--build")) else { return .other }
        let parsed = CommandOptions.parse(args, values: cmakeValue)
        let target = CommandOptions.firstTruthy(parsed["--target"], parsed["-t"])
        if let text = target?.text, [Bytes(utf8: "test"), Bytes(utf8: "check"), Bytes(utf8: "RUN_TESTS")].contains(text) {
            return Classification(.test, "cmake --build", true)
        }
        return Classification(.build, "cmake --build", target?.text != nil)
    }

    static let ctestValue = OptionList([
        "--test-dir", "-C", "--build-config", "-j", "--parallel", "--timeout", "--output-log", "-R", "--tests-regex",
        "-E", "--exclude-regex", "-L", "--label-regex", "-LE", "--label-exclude", "-I", "--tests-information",
        "--repeat", "--stop-time", "-O", "--resource-spec-file", "--repeat-until-fail",
    ])
    static let ctestFilters = TaskText.words([
        "-R", "--tests-regex", "-E", "--exclude-regex", "-L", "--label-regex", "-LE", "--label-exclude", "-I",
        "--tests-information", "--rerun-failed",
    ])

    static func ctest(_ args: [Bytes], _ cwd: String?) -> Classification {
        Classification(.test, "ctest", CommandOptions.parse(args, values: ctestValue).hasAny(ctestFilters))
    }

    static let jestValue = OptionList([
        "-c", "--config", "-w", "--maxWorkers", "--testTimeout", "--reporters", "--coverageReporters", "--rootDir",
        "--roots", "--env", "--testEnvironment", "--outputFile", "--seed", "-t", "--testNamePattern",
        "--testPathPattern", "--testPathPatterns", "--testPathIgnorePatterns", "--changedSince", "--selectProjects",
        "--shard", "--coverageDirectory", "--collectCoverageFrom", "--json",
    ])
    static let jestFilters = TaskText.words([
        "-t", "--testNamePattern", "--testPathPattern", "--testPathPatterns", "--findRelatedTests", "-o",
        "--onlyChanged", "--changedSince", "--lastCommit", "--selectProjects", "--shard", "--onlyFailures", "-f",
    ])

    static func jest(_ args: [Bytes], _ cwd: String?) -> Classification {
        let parsed = CommandOptions.parse(args, values: jestValue)
        return Classification(.test, "jest",
                              parsed.hasAny(jestFilters) || CommandOptions.nameTargeted(parsed.operands, cwd: cwd))
    }

    static let vitestValue = OptionList([
        "-c", "--config", "--root", "-r", "--dir", "--reporter", "--outputFile", "--environment", "--pool", "--project",
        "--shard", "-t", "--testNamePattern", "--maxWorkers", "--minWorkers", "--testTimeout", "--mode", "--exclude",
    ])
    static let vitestFilters = TaskText.words(["-t", "--testNamePattern", "--changed", "--shard", "--project"])
    static let vitestSubcommands = TaskText.words(["run", "watch", "dev", "related", "bench", "list", "init", "typecheck"])

    static func vitest(_ args: [Bytes], _ cwd: String?) -> Classification {
        let sub = args.first.flatMap { vitestSubcommands.contains($0) ? $0 : nil }
        let rest = sub == nil ? args : Array(args.dropFirst())
        if let sub, sub == Bytes(utf8: "bench") || sub == Bytes(utf8: "list") || sub == Bytes(utf8: "init") { return .other }
        let parsed = CommandOptions.parse(rest, values: vitestValue)
        return Classification(.test, "vitest", sub == Bytes(utf8: "related") || parsed.hasAny(vitestFilters)
                                  || CommandOptions.nameTargeted(parsed.operands, cwd: cwd))
    }

    static let mochaValue = OptionList([
        "-r", "--require", "-R", "--reporter", "-t", "--timeout", "-s", "--slow", "-u", "--ui", "--config",
        "--package", "--extension", "--ignore", "--file", "-g", "--grep", "-f", "--fgrep", "--spec", "--retries", "-j",
        "--jobs", "--reporter-option", "-O",
    ])
    static let mochaFilters = TaskText.words(["-g", "--grep", "-f", "--fgrep"])

    static func mocha(_ args: [Bytes], _ cwd: String?) -> Classification {
        var parsed = CommandOptions.parse(args, values: mochaValue)
        if let spec = parsed["--spec"]?.text { parsed.positionals.append(spec) }
        return Classification(.test, "mocha",
                              parsed.hasAny(mochaFilters) || CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
    }

    static let ruffSubcommands = TaskText.words([
        "check", "format", "rule", "config", "linter", "clean", "server", "analyze", "version",
    ])
    static let ruffValue = OptionList([
        "--select", "--ignore", "--extend-select", "--extend-ignore", "--config", "--output-format",
        "--target-version", "--exclude", "--extend-exclude", "--line-length", "--per-file-ignores", "--cache-dir",
        "--stdin-filename", "--fixable", "--unfixable",
    ])

    static func ruff(_ args: [Bytes], _ cwd: String?) -> Classification {
        let sub = args.first.flatMap { ruffSubcommands.contains($0) ? $0 : nil }
        let rest = sub == nil ? args : Array(args.dropFirst())
        if sub == nil || sub == Bytes(utf8: "check") {
            let parsed = CommandOptions.parse(rest, values: ruffValue)
            return Classification(.check, "ruff", CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
        }
        if sub == Bytes(utf8: "format"), rest.contains(Bytes(utf8: "--check")) || rest.contains(Bytes(utf8: "--diff")) {
            let parsed = CommandOptions.parse(rest, values: ruffValue)
            return Classification(.check, "ruff format", CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
        }
        return .other
    }

    static let tscNotCheck = TaskText.words(["--watch", "-w", "--version", "-v", "--init", "--help", "-h", "--showConfig"])
    static let tscValue = OptionList([
        "-p", "--project", "--outDir", "--target", "--module", "--lib", "--jsx", "--moduleResolution", "--rootDir",
        "--declarationDir", "--tsBuildInfoFile",
    ])

    static func tsc(_ suite: String) -> ProgramRule {
        { args, cwd in
            if args.contains(where: { tscNotCheck.contains($0) }) { return .other }
            let parsed = CommandOptions.parse(args, values: tscValue)
            if args.contains(Bytes(utf8: "-b")) || args.contains(Bytes(utf8: "--build")) { return Classification(.check, suite, false) }
            return Classification(.check, suite, CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
        }
    }

    static let bazelValue = OptionList(["--config", "--test_filter", "--test_output", "--jobs", "-j"])
    static let bazelBroad = TaskText.words(["//...", "...", "//:all"])

    static func bazel(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard let sub = args.first, sub == Bytes(utf8: "test") || sub == Bytes(utf8: "build") else { return .other }
        let parsed = CommandOptions.parse(Array(args.dropFirst()), values: bazelValue)
        let narrow = parsed.positionals.contains { !bazelBroad.contains($0) }
            || args.contains { $0.hasPrefix(Bytes(utf8: "--test_filter")) }
        return sub == Bytes(utf8: "test") ? Classification(.test, "bazel test", narrow) : Classification(.build, "bazel build", narrow)
    }

    static let mesonValue = OptionList([
        "-C", "--suite", "--no-suite", "--num-processes", "--repeat", "--timeout-multiplier", "--setup", "--wrapper",
    ])

    static func meson(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard let sub = args.first else { return .other }
        if sub == Bytes(utf8: "test") {
            let parsed = CommandOptions.parse(Array(args.dropFirst()), values: mesonValue)
            return Classification(.test, "meson test", !parsed.positionals.isEmpty || parsed.has("--suite"))
        }
        if sub == Bytes(utf8: "compile") { return Classification(.build, "meson compile", false) }
        return .other
    }

    static let pyValue = OptionList(["-W", "-X", "--check-hash-based-pycs"])
    static let djangoValue = OptionList([
        "-k", "--settings", "--parallel", "--tag", "--exclude-tag", "--pattern", "-p", "-v", "--verbosity",
    ])

    /// `python manage.py test`, `python setup.py test`; any other script is not a check.
    static func pythonScript(_ input: [Bytes], _ cwd: String?) -> Classification {
        let start = ShellCommand.skipUntilModule(input, values: pyValue)
        let args = start >= input.count ? [] : Array(input[start...])
        guard args.count >= 2, args[1] == Bytes(utf8: "test") else { return .other }
        let script = args[0].replacing(byte: Byte.backslash, with: Byte.slash).lastPart(byte: Byte.slash)
        if script == Bytes(utf8: "manage.py") {
            let parsed = CommandOptions.parse(Array(args.dropFirst(2)), values: djangoValue)
            return Classification(.test, "django test", !parsed.positionals.isEmpty || parsed.has("-k") || parsed.has("--tag"))
        }
        if script == Bytes(utf8: "setup.py") { return Classification(.test, "setup.py test", false) }
        return .other
    }

    static let nodeTestValue = OptionList([
        "--test-name-pattern", "--test-reporter", "--test-reporter-destination", "--test-concurrency", "--require",
        "-r", "--import", "--loader",
    ])

    static func node(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard args.contains(Bytes(utf8: "--test")) else { return .other }
        let parsed = CommandOptions.parse(args.filter { $0 != Bytes(utf8: "--test") }, values: nodeTestValue)
        return Classification(.test, "node --test", parsed.has("--test-name-pattern") || parsed.has("--test-only")
                                  || CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
    }

    static func swiftlint(_ args: [Bytes], _ cwd: String?) -> Classification {
        if let first = args.first, first == Bytes(utf8: "autocorrect") || first == Bytes(utf8: "analyze") { return .other }
        if args.contains(Bytes(utf8: "--fix")) { return .other }
        return Classification(.check, "swiftlint", false)
    }

    static let denoTestValue = OptionList([
        "--filter", "--allow-read", "--allow-net", "--allow-env", "--config", "-c", "--reporter", "--coverage",
        "--jobs", "--shuffle", "--seed",
    ])

    static func deno(_ args: [Bytes], _ cwd: String?) -> Classification {
        guard let sub = args.first else { return .other }
        let rest = Array(args.dropFirst())
        switch sub {
        case Bytes(utf8: "test"):
            let parsed = CommandOptions.parse(rest, values: denoTestValue)
            return Classification(.test, "deno test",
                                  parsed.has("--filter") || CommandOptions.pathTargeted(parsed.operands, cwd: cwd))
        case Bytes(utf8: "check"): return Classification(.check, "deno check", false)
        case Bytes(utf8: "lint"): return Classification(.check, "deno lint", false)
        case Bytes(utf8: "fmt") where rest.contains(Bytes(utf8: "--check")): return Classification(.check, "deno fmt", false)
        case Bytes(utf8: "task"): return ScriptRunners.runner(.deno, values: .none, fallback: nil)(args, cwd)
        default: return .other
        }
    }

    // MARK: The table

    static let rules: [Bytes: ProgramRule] = {
        let eslintValue = [
            "-c", "--config", "--ext", "-f", "--format", "-o", "--output-file", "--rule", "--parser",
            "--resolve-plugins-relative-to", "--max-warnings", "--ignore-path", "--ignore-pattern", "--cache-location",
            "--env", "--global", "--plugin", "--rulesdir", "--parser-options",
            "--report-unused-disable-directives-severity", "--cache-strategy",
        ]
        let mypyValue = [
            "--config-file", "-p", "--package", "-m", "--module", "--python-version", "--cache-dir", "--exclude",
            "--follow-imports", "--platform", "--junit-xml", "--python-executable",
        ]
        let pyrightValue = [
            "-p", "--project", "--pythonpath", "--pythonversion", "--level", "--threads", "--pythonplatform", "-t",
            "--typeshedpath", "--venvpath",
        ]
        let rspecValue = [
            "-e", "--example", "-E", "--example-matches", "-t", "--tag", "-f", "--format", "-o", "--out", "-r",
            "--require", "-I", "--seed", "--order", "-P", "--pattern", "--exclude-pattern", "--default-path",
        ]
        let phpunitValue = [
            "--filter", "--group", "--exclude-group", "--testsuite", "-c", "--configuration", "--bootstrap",
            "--log-junit", "--order-by", "--cache-directory", "--coverage-html", "--coverage-clover", "--coverage-text",
            "--test-suffix", "--exclude-testsuite",
        ]
        typealias S = Subcommand
        let table: [String: ProgramRule] = [
            "pytest": pytest,
            "unittest": unittest,
            "cargo": cargo,
            "swift": swift,
            "swiftc": named(.build, "swiftc"),
            "xcodebuild": xcodebuild,
            "go": go,
            "dotnet": dotnet,
            "msbuild": named(.build, "msbuild"),
            "vstest.console": named(.test, "vstest"),
            "gradle": gradle,
            "mvn": mvn,
            "javac": named(.build, "javac"),
            "kotlinc": named(.build, "kotlinc"),
            "cc": cc,
            "cmake": cmake,
            "ctest": ctest,
            "jest": jest,
            "vitest": vitest,
            "mocha": mocha,
            "ava": paths(.test, "ava", values: ["--match", "-m", "--timeout", "-T"], filters: ["--match", "-m"]),
            "tap": paths(.test, "tap", values: ["--grep", "-g", "--reporter", "-R"], filters: ["--grep", "-g"]),
            "node": node,
            "playwright": subcommands(
                ["test": S(.test, "playwright test", ["-g", "--grep", "--last-failed", "--only-changed"], paths: true)],
                values: ["-g", "--grep", "--project", "-c", "--config", "--reporter", "--workers", "-j", "--shard",
                         "--retries", "--timeout", "--grep-invert"]),
            "cypress": subcommands(
                ["run": S(.test, "cypress run", ["--spec", "-s"])],
                values: ["--spec", "-s", "--browser", "-b", "--config", "-c", "--env", "-e", "--project", "-P"]),
            "tsc": tsc("tsc"),
            "vue-tsc": tsc("vue-tsc"),
            "eslint": paths(.check, "eslint", values: eslintValue),
            "oxlint": paths(.check, "oxlint", values: ["-c", "--config", "-A", "-D", "-W", "--format", "-f"]),
            "biome": subcommands(
                ["check": S(.check, "biome", paths: true), "lint": S(.check, "biome", paths: true),
                 "ci": S(.check, "biome", paths: true)],
                values: ["--config-path", "--reporter", "--max-diagnostics"]),
            "prettier": checkFlag("prettier", flags: ["--check", "-c", "--list-different", "-l"],
                                  values: ["--config", "--ignore-path", "--plugin", "--parser"]),
            "stylelint": paths(.check, "stylelint", values: ["--config", "-c", "--formatter", "-f", "--ignore-path"]),
            "svelte-check": named(.check, "svelte-check"),
            "astro": subcommands(["check": S(.check, "astro check")]),
            "ruff": ruff,
            "mypy": paths(.check, "mypy", values: mypyValue),
            "dmypy": named(.check, "mypy"),
            "pyright": paths(.check, "pyright", values: pyrightValue),
            "basedpyright": paths(.check, "basedpyright", values: pyrightValue),
            "flake8": paths(.check, "flake8", values: ["--config", "--select", "--ignore", "--extend-ignore",
                                                        "--max-line-length", "--exclude", "--format"]),
            "pylint": paths(.check, "pylint", values: ["--rcfile", "--disable", "--enable", "-j", "--jobs",
                                                        "--output-format", "--load-plugins"]),
            "black": checkFlag("black", flags: ["--check", "--diff"],
                               values: ["--config", "-l", "--line-length", "-t", "--target-version", "--include",
                                        "--exclude"]),
            "isort": checkFlag("isort", flags: ["--check", "--check-only", "-c", "--diff"],
                               values: ["--settings-path", "--sp", "--profile"]),
            "tox": posargs("tox"),
            "nox": posargs("nox"),
            "rspec": paths(.test, "rspec", values: rspecValue,
                           filters: ["-e", "--example", "-E", "--example-matches", "-t", "--tag", "--only-failures",
                                     "--next-failure", "-P", "--pattern"]),
            "rails": subcommands(["test": S(.test, "rails test", ["-n", "--name"], paths: true)],
                                 values: ["-n", "--name", "-e", "--environment", "--seed"]),
            "rubocop": paths(.check, "rubocop", values: ["-c", "--config", "-f", "--format", "--only", "--except",
                                                          "--require", "-r", "-o", "--out"]),
            "phpunit": paths(.test, "phpunit", values: phpunitValue, filters: ["--filter", "--group", "--testsuite"]),
            "pest": paths(.test, "pest", values: phpunitValue, filters: ["--filter", "--group", "--testsuite"]),
            "phpstan": subcommands(
                ["analyse": S(.check, "phpstan", paths: true), "analyze": S(.check, "phpstan", paths: true)],
                values: ["-c", "--configuration", "-l", "--level", "--memory-limit", "--error-format"]),
            "psalm": paths(.check, "psalm", values: ["-c", "--config", "--threads", "--output-format"]),
            "mix": subcommands(
                ["test": S(.test, "mix test", ["--only", "--failed", "--stale", "--include"], paths: true),
                 "compile": S(.build, "mix compile"), "credo": S(.check, "mix credo"),
                 "dialyzer": S(.check, "mix dialyzer")],
                values: ["--only", "--include", "--exclude", "--seed", "--max-failures", "--partitions"]),
            "zig": subcommands(["test": S(.test, "zig test"), "build": S(.build, "zig build")]),
            "stack": subcommands(["test": S(.test, "stack test", ["--ta", "--test-arguments"]),
                                  "build": S(.build, "stack build")],
                                 values: ["--ta", "--test-arguments"]),
            "cabal": subcommands(["test": S(.test, "cabal test", ["--test-options", "--test-option"]),
                                  "build": S(.build, "cabal build")],
                                 values: ["--test-options", "--test-option"]),
            "dart": subcommands(
                ["test": S(.test, "dart test", ["-n", "--name", "-N", "--plain-name", "-t", "--tags"], paths: true),
                 "analyze": S(.check, "dart analyze")],
                values: ["-n", "--name", "-N", "--plain-name", "-t", "--tags", "-p", "--platform", "-j",
                         "--concurrency", "-r", "--reporter"]),
            "flutter": subcommands(
                ["test": S(.test, "flutter test", ["-n", "--name", "--plain-name", "-t", "--tags"], paths: true),
                 "analyze": S(.check, "flutter analyze"), "build": S(.build, "flutter build")],
                values: ["-n", "--name", "--plain-name", "-t", "--tags", "-r", "--reporter", "--flavor"]),
            "deno": deno,
            "golangci-lint": subcommands(
                ["run": S(.check, "golangci-lint", paths: true)],
                values: ["-c", "--config", "--timeout", "-E", "--enable", "-D", "--disable", "--out-format"]),
            "staticcheck": paths(.check, "staticcheck", values: ["-checks", "-f", "-tags"]),
            "swiftlint": swiftlint,
            "swift-format": subcommands(["lint": S(.check, "swift-format")]),
            "clang-tidy": named(.check, "clang-tidy"),
            "cppcheck": named(.check, "cppcheck"),
            "shellcheck": named(.check, "shellcheck"),
            "hadolint": named(.check, "hadolint"),
            "terraform": subcommands(["validate": S(.check, "terraform validate")]),
            "pre-commit": subcommands(["run": S(.check, "pre-commit", ["--files"])],
                                      values: ["--files", "--hook-stage", "--from-ref", "--to-ref"]),
            "bazel": bazel,
            "meson": meson,
            "vite": subcommands(["build": S(.build, "vite build")]),
            "next": subcommands(["build": S(.build, "next build"), "lint": S(.check, "next lint")]),
            "nuxt": subcommands(["build": S(.build, "nuxt build")]),
            "ng": subcommands(["build": S(.build, "ng build"), "test": S(.test, "ng test", ["--include"]),
                               "lint": S(.check, "ng lint")],
                              values: ["--include", "--configuration", "-c", "--project"]),
            "webpack": named(.build, "webpack"),
            "rollup": named(.build, "rollup"),
            "esbuild": named(.build, "esbuild"),
            "tsup": named(.build, "tsup"),
            "rustc": named(.build, "rustc"),
            "npm": ScriptRunners.packageManager(.npm),
            "pnpm": ScriptRunners.packageManager(.pnpm),
            "yarn": ScriptRunners.packageManager(.yarn),
            "bun": ScriptRunners.packageManager(.bun),
            "make": ScriptRunners.runner(.make, values: OptionList([
                "-C", "--directory", "-f", "--file", "--makefile", "-I", "--include-dir", "-o", "--old-file", "-W",
                "--what-if", "-j", "-l",
            ]), fallback: Classification(.build, "make", false)),
            "ninja": ScriptRunners.runner(.ninja, values: OptionList(["-C", "-f", "-j", "-k", "-l", "-d", "-t", "-w"]),
                                          fallback: Classification(.build, "ninja", false)),
            "just": ScriptRunners.runner(.just, values: OptionList([
                "-f", "--justfile", "-d", "--working-directory", "--set", "--dotenv-path", "--dotenv-filename",
                "--shell", "--shell-arg", "--chooser", "--color", "--command", "--list-heading", "--list-prefix",
            ]), fallback: nil),
            "task": ScriptRunners.runner(.task, values: OptionList([
                "-d", "--dir", "-t", "--taskfile", "-o", "--output", "-c", "--color",
            ]), fallback: nil),
            "rake": ScriptRunners.runner(.rake, values: OptionList([
                "-f", "--rakefile", "-I", "--libdir", "-r", "--require", "-C", "--directory",
            ]), fallback: nil),
            "mise": ScriptRunners.runner(.mise, values: OptionList(["-C", "--cd", "-E", "--env", "-j", "--jobs"]),
                                         fallback: nil),
            "turbo": ScriptRunners.runner(.turbo, values: OptionList([
                "--filter", "-F", "--concurrency", "--output-logs", "--cache-dir",
            ]), fallback: nil),
            "nx": ScriptRunners.runner(.nx, values: OptionList(["-c", "--configuration", "--parallel", "--output-style"]),
                                       fallback: nil),
            "composer": ScriptRunners.runner(.composer, values: OptionList(["-d", "--working-dir"]), fallback: nil),
        ]
        return table.reduce(into: [:]) { $0[Bytes(utf8: $1.key)] = $1.value }
    }()
}
