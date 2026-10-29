import Foundation

/// Package scripts and task runners (TASK-PARSERS 3.6): the class of a script or target comes from its name.
enum ScriptRunners {
    enum Runner: String, CaseIterable {
        case npm, pnpm, yarn, bun, deno, composer, make, ninja, just, task, rake, mise, turbo, nx

        /// The suite of the canonical `test` script and the prefix of the other canonical names.
        var labels: (test: String, prefix: String) {
            switch self {
            case .npm: ("npm test", "npm run ")
            case .pnpm: ("pnpm test", "pnpm run ")
            case .yarn: ("yarn test", "yarn run ")
            case .bun: ("bun run test", "bun run ")
            case .deno: ("deno task test", "deno task ")
            case .composer: ("composer test", "composer ")
            case .make: ("make test", "make ")
            case .ninja: ("ninja test", "ninja ")
            case .just: ("just test", "just ")
            case .task: ("task test", "task ")
            case .rake: ("rake test", "rake ")
            case .mise: ("mise run test", "mise run ")
            case .turbo: ("turbo test", "turbo ")
            case .nx: ("nx test", "nx ")
            }
        }

        /// The first words a runner drops before its task (`RUNNER_SUBCOMMANDS`).
        var subcommands: Set<Bytes> {
            switch self {
            case .mise: TaskText.words(["run", "r"])
            case .deno: TaskText.words(["task"])
            case .turbo: TaskText.words(["run"])
            case .composer: TaskText.words(["run-script", "run"])
            default: []
            }
        }
    }

    /// Program names that are runners of `SCRIPT_LABEL`.
    static let runnerNames: Set<Bytes> = Set(Runner.allCases.map { Bytes(utf8: $0.rawValue) })

    private static let aliases: [Bytes: Bytes] = [
        "t": "test", "tst": "test", "tests": "test", "type-check": "typecheck", "types": "typecheck", "compile": "build",
    ].reduce(into: [:]) { $0[Bytes(utf8: $1.key)] = Bytes(utf8: $1.value) }
    private static let canonical: [Bytes: TaskCommandClass] = [
        "test": .test, "build": .build, "lint": .check, "typecheck": .check, "check": .check,
    ].reduce(into: [:]) { $0[Bytes(utf8: $1.key)] = $1.value }
    static let testWords = TaskText.words([
        "test", "tests", "spec", "specs", "unit", "e2e", "integration", "coverage", "cov", "jest", "vitest", "mocha",
        "pytest", "ctest", "playwright", "cypress",
    ])
    static let buildWords = TaskText.words(["build", "compile", "bundle", "dist", "all"])
    static let checkWords = TaskText.words([
        "lint", "typecheck", "types", "tsc", "check", "eslint", "ruff", "mypy", "pyright", "clippy", "vet", "analyze",
        "analyse", "verify", "validate",
    ])
    static let forwardFilters = TaskText.words([
        "-t", "--testNamePattern", "-k", "--grep", "-g", "--filter", "-run", "--run", "--testPathPattern", "--spec",
        "--tests", "-E", "-R", "--findRelatedTests", "--only-testing", "-only-testing", "--skip", "--exact", "-p",
        "--package", "-m",
    ])
    private static let forwardFilterAssignments: [Bytes] = forwardFilters
        .filter { $0.hasPrefix(Bytes(utf8: "--")) }.map { $0 + [Byte.equals] }
    static let forwardValue = OptionList([
        "--reporter", "-r", "--config", "-c", "--maxWorkers", "-w", "--timeout", "--testTimeout", "--env",
        "--outputFile", "--coverageReporters", "--pool", "--environment", "--seed", "--tb", "-n", "--workers", "-j",
        "--jobs",
    ])
    static let npmBuiltins = TaskText.words([
        "install", "i", "ci", "add", "uninstall", "remove", "rm", "update", "up", "outdated", "ls", "list", "link",
        "publish", "pack", "init", "create", "audit", "config", "cache", "version", "view", "info", "why", "explain",
        "dedupe", "prune", "rebuild", "start", "restart", "stop", "store", "fetch", "patch", "import", "deploy",
        "licenses", "doctor", "setup", "env", "approve-builds", "self-update", "workspaces", "workspace", "dlx", "exec",
        "x", "set", "plugin", "constraints", "unplug", "stage", "npm", "node", "upgrade", "pm", "repl", "help", "login",
        "logout", "whoami", "search", "owner", "access", "token", "team", "org", "profile", "star", "stars", "unstar",
        "fund", "sbom", "query", "run-script", "run", "rum", "urn", "test", "t", "tst", "build", "global",
    ])
    static let composerBuiltins = TaskText.words([
        "install", "update", "require", "remove", "dump-autoload", "dumpautoload", "show", "outdated", "validate",
        "create-project", "init", "config", "global", "exec", "search", "why", "why-not", "licenses", "audit", "status",
        "archive", "diagnose", "self-update", "depends", "prohibits", "fund", "reinstall", "bump",
        "check-platform-reqs", "clear-cache", "browse", "suggests",
    ])
    static let makeFilterVariable = Pattern(
        #"TESTS?|T|TEST_?ARGS|TEST_?FILTER|FILTER|RUN|ONLY|K|PKG|PACKAGE|FILES?|SPEC|ARGS|PYTEST_?ARGS|TESTNAME|CASE|TESTOPTS"#)
    private static let nameSeparators: Set<UInt8> = [Byte.colon, Byte.dot, 0x5F, Byte.slash, Byte.dash]

    /// The class and suite of a script or target name.
    static func scriptClass(_ runner: Runner, _ name: Bytes) -> (TaskCommandClass, String?) {
        let folded = name.lowercasedASCII()
        let canon = aliases[folded] ?? folded
        if runner == .make || runner == .ninja {
            if canon == Bytes(utf8: "check") { return (.test, runner == .make ? "make check" : "ninja check") }
            if canon == Bytes(utf8: "all") { return (.build, runner.rawValue) }
        }
        if let commandClass = canonical[canon] {
            let labels = runner.labels
            return (commandClass, canon == Bytes(utf8: "test") ? labels.test : labels.prefix + canon.string)
        }
        let first = Bytes(folded.prefix { !nameSeparators.contains($0) })
        if testWords.contains(first) { return (.test, nil) }
        if buildWords.contains(first) { return (.build, nil) }
        if checkWords.contains(first) { return (.check, nil) }
        return (.other, nil)
    }

    static func forwardedTargeted(_ args: [Bytes], cwd: String?) -> Bool {
        let words = args.filter { $0 != CommandOptions.dashDash }
        var k = 0
        while k < words.count {
            let word = words[k]
            if forwardFilters.contains(word) || forwardFilterAssignments.contains(where: { word.hasPrefix($0) }) {
                return true
            }
            if word.first == Byte.dash {
                k += forwardValue.takesValue(word) ? 2 : 1
                continue
            }
            if CommandOptions.nameTargeted([word], cwd: cwd) { return true }
            k += 1
        }
        return false
    }

    private static let pmNarrow = OptionList(["-w", "--workspace", "--filter", "-F"])
    private static let pmValue = OptionList(["-w", "--workspace", "--filter", "-F", "--prefix", "-C", "--dir", "--cwd"])
    private static let bunTestValue = OptionList([
        "-t", "--test-name-pattern", "--timeout", "--preload", "--coverage-reporter", "--reporter", "--rerun-each",
    ])
    private static let runWords = TaskText.words(["run", "run-script", "rum", "urn"])
    private static let testScripts = TaskText.words(["test", "t", "tst"])

    private static func narrowsWorkspace(_ word: Bytes) -> Bool {
        pmNarrow.takesValue(word) || word.hasPrefix(Bytes(utf8: "--workspace=")) || word.hasPrefix(Bytes(utf8: "--filter="))
    }

    /// npm, pnpm, yarn and bun (TASK-PARSERS 3.6).
    static func packageManager(_ runner: Runner) -> ProgramRule {
        { args, cwd in packageManager(runner, args, cwd) }
    }

    private static func packageManager(_ runner: Runner, _ args: [Bytes], _ cwd: String?) -> Classification {
        var k = 0
        var narrow = false
        while k < args.count, args[k].first == Byte.dash {
            let word = args[k]
            if pmValue.takesValue(word) {
                narrow = narrow || pmNarrow.takesValue(word)
                k += 2
                continue
            }
            if word.hasPrefix(Bytes(utf8: "--workspace=")) || word.hasPrefix(Bytes(utf8: "--filter=")) { narrow = true }
            k += 1
        }
        var rest = k >= args.count ? [] : Array(args[k...])
        guard let sub = rest.first else { return .other }
        if runner == .yarn, sub == Bytes(utf8: "workspace"), rest.count >= 3 {
            var inner = packageManager(runner, Array(rest[2...]), cwd)
            inner.targeted = true
            return inner
        }
        if runner == .bun, sub == Bytes(utf8: "test") {
            let parsed = CommandOptions.parse(Array(rest.dropFirst()), values: bunTestValue)
            return Classification(.test, "bun test", parsed.has("-t") || parsed.has("--test-name-pattern")
                                      || CommandOptions.nameTargeted(parsed.operands, cwd: cwd))
        }
        if runner == .bun, sub == Bytes(utf8: "build") { return Classification(.build, "bun build", false) }
        let before: ArraySlice<Bytes> = {
            if let dashes = rest.firstIndex(of: CommandOptions.dashDash) { return rest[1..<max(1, dashes)] }
            return rest.dropFirst()
        }()
        if before.contains(where: narrowsWorkspace) {
            narrow = true
            let limit = before.count
            let original = rest
            rest = original.enumerated().filter { index, word in
                !(pmNarrow.takesValue(word)
                  || (index > 0 && pmNarrow.takesValue(original[index - 1]) && index <= limit)
                  || word.hasPrefix(Bytes(utf8: "--workspace=")) || word.hasPrefix(Bytes(utf8: "--filter=")))
            }.map(\.element)
        }
        let name: Bytes
        let forwarded: [Bytes]
        if testScripts.contains(sub) {
            name = Bytes(utf8: "test")
            forwarded = Array(rest.dropFirst())
        } else if runWords.contains(sub) {
            guard rest.count >= 2 else { return .other }
            let j = CommandOptions.dropOptions(Array(rest.dropFirst()), from: 0, values: pmNarrow, stopAtDashDash: false) + 1
            guard j < rest.count else { return .other }
            name = rest[j]
            forwarded = Array(rest[(j + 1)...])
        } else if runner == .pnpm || runner == .yarn || runner == .bun, !npmBuiltins.contains(sub),
                  !CommandOptions.hasExtension(sub) {
            let program = ShellCommand.program(sub)
            if ProgramRules.hasRule(program), !runnerNames.contains(program) {
                return ProgramRules.classify(rest, cwd: cwd)
            }
            name = sub
            forwarded = Array(rest.dropFirst())
        } else {
            return .other
        }
        let (commandClass, suite) = scriptClass(runner, name)
        if commandClass == .other { return .other }
        return Classification(commandClass, suite, narrow || forwardedTargeted(forwarded, cwd: cwd))
    }

    private static let nxValue = OptionList(["-t", "--target", "--targets", "-p", "--projects"])

    /// make, ninja, just, task, rake, mise, turbo, nx, composer and deno task (TASK-PARSERS 3.6).
    static func runner(_ runner: Runner, values: OptionList, fallback: Classification?) -> ProgramRule {
        { args, cwd in
            var rest = args
            if let first = rest.first, runner.subcommands.contains(first) { rest.removeFirst() }
            if runner == .nx, let first = rest.first, first == Bytes(utf8: "run-many") || first == Bytes(utf8: "affected") {
                let parsed = CommandOptions.parse(Array(rest.dropFirst()), values: nxValue)
                let target = CommandOptions.firstTruthy(parsed["-t"], parsed["--target"], parsed["--targets"])
                guard let text = target?.text else { return .other }
                let (commandClass, suite) = scriptClass(runner, text.firstPart(byte: 0x2C))
                guard commandClass != .other else { return .other }
                let projects = CommandOptions.firstTruthy(parsed["-p"], parsed["--projects"])?.text
                return Classification(commandClass, suite, projects != nil)
            }
            if runner == .nx, rest.first == Bytes(utf8: "run"), rest.count >= 2, rest[1].contains(Byte.colon) {
                let (commandClass, suite) = scriptClass(runner, rest[1].split(byte: Byte.colon)[1])
                return commandClass == .other ? .other : Classification(commandClass, suite, true)
            }
            var positionals: [Bytes] = []
            var assignments: [Bytes] = []
            var k = 0
            while k < rest.count {
                let word = rest[k]
                if word.first == Byte.dash {
                    if values.takesValue(word) {
                        k += runner == .just && word == Bytes(utf8: "--set") ? 3 : 2
                    } else {
                        k += 1
                    }
                    continue
                }
                if ShellCommand.nameAssign.matches(word) {
                    assignments.append(word.firstPart(byte: Byte.equals))
                } else {
                    positionals.append(word)
                }
                k += 1
            }
            if runner == .composer, let first = positionals.first, composerBuiltins.contains(first) { return .other }
            guard let first = positionals.first else { return fallback ?? .other }
            var commandClass: TaskCommandClass
            var suite: String?
            var narrow: Bool
            if runner == .make || runner == .ninja || runner == .rake {
                var best: (TaskCommandClass, String?)?
                for target in positionals {
                    let candidate = scriptClass(runner, target)
                    if best == nil || candidate.0.rank > best!.0.rank { best = candidate }
                }
                (commandClass, suite) = best!
                narrow = assignments.contains { makeFilterVariable.fullMatches($0.uppercasedASCII()) }
            } else {
                (commandClass, suite) = scriptClass(runner, first)
                narrow = positionals.count > 1
                    && (runner == .just || forwardedTargeted(Array(positionals.dropFirst()), cwd: cwd))
                if runner == .nx, positionals.count > 1 { narrow = true }
            }
            return commandClass == .other ? .other : Classification(commandClass, suite, narrow)
        }
    }
}
