namespace AgentSpeed.Core.TaskParsers;

/// <summary>The fixed suite vocabulary <c>SUITES</c> (TASK-PARSERS 3.8, Appendix A.2): every suite is null or one of these.</summary>
internal static class CommandSuites
{
    public static readonly string[] All =
    [
        "astro check", "ava", "basedpyright", "bazel build", "bazel test", "biome", "black", "bun build", "bun run build",
        "bun run check", "bun run lint", "bun run test", "bun run typecheck", "bun test", "cabal build", "cabal test", "cargo build",
        "cargo check", "cargo clippy", "cargo fmt", "cargo nextest", "cargo test", "cc", "clang-tidy", "cmake --build",
        "composer build", "composer check", "composer lint", "composer test", "composer typecheck", "cppcheck", "ctest",
        "cypress run", "dart analyze", "dart test", "deno check", "deno fmt", "deno lint", "deno task build", "deno task check",
        "deno task lint", "deno task test", "deno task typecheck", "deno test", "django test", "dotnet build", "dotnet format",
        "dotnet msbuild", "dotnet pack", "dotnet publish", "dotnet test", "esbuild", "eslint", "flake8", "flutter analyze",
        "flutter build", "flutter test", "go build", "go test", "go vet", "golangci-lint", "gradle assemble", "gradle build",
        "gradle check", "gradle lint", "gradle test", "hadolint", "isort", "javac", "jest", "just build", "just check", "just lint",
        "just test", "just typecheck", "kotlinc", "make", "make build", "make check", "make lint", "make test", "make typecheck",
        "meson compile", "meson test", "mise run build", "mise run check", "mise run lint", "mise run test", "mise run typecheck",
        "mix compile", "mix credo", "mix dialyzer", "mix test", "mocha", "msbuild", "mvn compile", "mvn deploy", "mvn install",
        "mvn integration-test", "mvn package", "mvn process-classes", "mvn test", "mvn test-compile", "mvn verify", "mypy",
        "next build", "next lint", "ng build", "ng lint", "ng test", "ninja", "ninja build", "ninja check", "ninja lint",
        "ninja test", "ninja typecheck", "node --test", "nox", "npm run build", "npm run check", "npm run lint", "npm run typecheck",
        "npm test", "nuxt build", "nx build", "nx check", "nx lint", "nx test", "nx typecheck", "oxlint", "pest", "phpstan",
        "phpunit", "playwright test", "pnpm run build", "pnpm run check", "pnpm run lint", "pnpm run typecheck", "pnpm test",
        "pre-commit", "prettier", "psalm", "pylint", "pyright", "pytest", "rails test", "rake build", "rake check", "rake lint",
        "rake test", "rake typecheck", "rollup", "rspec", "rubocop", "ruff", "ruff format", "rustc", "setup.py test", "shellcheck",
        "stack build", "stack test", "staticcheck", "stylelint", "svelte-check", "swift build", "swift test", "swift-format",
        "swiftc", "swiftlint", "tap", "task build", "task check", "task lint", "task test", "task typecheck", "terraform validate",
        "tox", "tsc", "tsup", "turbo build", "turbo check", "turbo lint", "turbo test", "turbo typecheck", "unittest", "vite build",
        "vitest", "vstest", "vue-tsc", "webpack", "xcodebuild analyze", "xcodebuild build", "xcodebuild test", "yarn run build",
        "yarn run check", "yarn run lint", "yarn run typecheck", "yarn test", "zig build", "zig test",
    ];

    public static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);
}
