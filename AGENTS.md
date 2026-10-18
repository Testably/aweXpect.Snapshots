# aweXpect.Snapshots Agent Guide

Snapshot testing for [aweXpect](https://github.com/Testably/aweXpect), shipped as three NuGet packages.

## SDK and build

- `global.json` pins the .NET SDK (`10.0.300`, `latestPatch`). The test projects also run on .NET 8 and on .NET Framework 4.8 (Windows only).
- `Source/Directory.Build.props` holds the library settings: `LangVersion` latest, nullable enabled (annotations only on `netstandard2.0`), warnings as errors, XML docs, a package on every build.
- `Tests/Directory.Build.props` holds the test settings and imports the build targets of the package, as a project reference does not.
- Package versions are managed centrally in `Directory.Packages.props`. Assemblies are strong-named with `strongname.snk`; test projects are not, except `aweXpect.Snapshots.DiffEngine.Tests`.

## Design goals

Correctness is a must. The other goals are strong defaults and win in this order when they conflict: stable and compatible, AOT and trimming friendly, a consistent and pleasant API, a fast happy path, extensible, errors at compile time, documented. [Docs/agents/design-goals.md](Docs/agents/design-goals.md) explains each goal, the code rules, the checks before a commit and the goals of this repository.

## Project constraints

- Packages: `aweXpect.Snapshots` (the expectations, namespace `aweXpect` and `aweXpect.Snapshots`), `aweXpect.Snapshots.PublicApi` (`MatchesPublicApiSnapshot`, built on Roslyn) and `aweXpect.Snapshots.DiffEngine` (a reporter that opens a diff tool). `Pipeline/Build.Pack.cs` lists them.
- Libraries target `netstandard2.0;net8.0;net10.0`; tests target `net8.0;net10.0;net48`. Code must compile for all of them: records and `init` work through the `IsExternalInit` package, other missing types need a polyfill in `Source/aweXpect.Snapshots/Polyfills`. Run the `net48` tests when you use a newer language feature.
- The library depends only on `aweXpect.Core`. Its version in `Directory.Packages.props` is the minimum consumers get, so raise it only when the code needs it.
- `DiffEngine` is pinned to 13.0.2 for `netstandard2.0` (its last version for that target) and to the current version elsewhere.
- Trimming and Native AOT: `aweXpect.Snapshots` and `aweXpect.Snapshots.DiffEngine` are `IsTrimmable` and `IsAotCompatible` from `net8.0`; the analyzers only run for `net10.0`. `aweXpect.Snapshots.PublicApi` opts out, as Roslyn cannot be trimmed. Avoid unguarded reflection, and cover new behaviour in the AOT smoke tests (`Tests/Aot`).
- Public API changes show up in the committed API snapshots of `Tests/aweXpect.Snapshots.Api.Tests` and are reviewed with the pull request. Document every public member with XML docs (a missing one only warns, CS1591).
- The `buildTransitive` targets ship in the packages. `aweXpect.Snapshots.targets` records the source root, project directory and target framework in the test assembly; changes there affect every consumer build.
- No em-dashes in `Docs/`, the README or XML docs.

## Validation

- Run the tests of the project you changed before pushing; [workflows](Docs/agents/workflows.md) has the commands. Changes to the library usually need `aweXpect.Snapshots.Tests`, and `aweXpect.Snapshots.Api.Tests` when the public API changed.
- Snapshots are committed next to their test file as `<File>.<Method>[.<Name>][@<Environment>].awexpect-snapshot.<ext>`. A missing or different one writes `*.awexpect-snapshot.pending.*` next to it, which is git-ignored: review it and rename it to accept it. Never commit pending files.
- `aweXpect.Snapshots.Tests`, `.SourceRoots.Tests`, `.Orphans.Tests` and `.DiffEngine.Tests` set the acceptance to `None` in a module initializer, so `AWEXPECT_ACCEPT_SNAPSHOTS` has no effect there; rename pending files by hand. `.PublicApi.Tests` and `.Api.Tests` honour the variable (except on a build server).
- Public API: after an intentional change, run the explicit test `AcceptApiChanges` (see [workflows](Docs/agents/workflows.md#public-api)), or rename the pending files, then review and commit the changed `ApiApprovalTests.*.awexpect-snapshot.txt`.
- Snapshots per target framework (`@net48`, `@net8.0`, `@net10.0`) need a run on each; `net48` only runs on Windows.
- Tests use temporary directories (`SnapshotDirectory`) or `InMemorySnapshotStore` instead of writing next to the project, as tests run in parallel.
- New expectations or options need docs in `Docs/pages/` (published to docs.testably.org) and, for a new expectation, a row in the README overview.

## Code conventions

- `.editorconfig`: tabs, CRLF in the working tree; git stores LF (`* text=auto`). `*.bin` and `*.png` snapshots are binary.
- Most files have no UTF-8 BOM, a few do; keep each file as it is. PowerShell rewrites (`Set-Content`, `Out-File`) and `sed -i` can change the BOM or line endings, which matters in raw string literals. Prefer an editor, and check `git diff` for a stray U+FEFF.
- Tests: xUnit v2 (NUnit in `Api.Tests`), nested classes like `ThatSnapshot.MatchesSnapshot.<Feature>Tests` in a file of the same name, methods named `<Condition>_Should<Outcome>`, reasons in `.Because(...)` instead of comments, and exception messages always pinned with `.WithMessage(...)`.

## Git workflow

- Branch from `main` (e.g. `feat/...`, `fix/...`, `docs/...`) and open a pull request; it is squash-merged.
- Conventional commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `ci:`, `chore:`, with `!` for a breaking change (`feat!:`).
- The commit subject is the PR title; the body explains why, not what. Write each paragraph on a single line. No co-author or attribution trailers.
- Never commit `.claude/`, `Artifacts/`, `TestResults/` or pending snapshots. Do not push or open a PR unless asked.

## References

- [Docs/agents/design-goals.md](Docs/agents/design-goals.md): the goals and code rules for every change, the checks before a commit and the goals of this repository.
- [Docs/agents/workflows.md](Docs/agents/workflows.md): build, test, AOT, benchmark, mutation and documentation commands, pipeline targets, CI workflows and releases.
- [Docs/agents/project-structure.md](Docs/agents/project-structure.md): projects, source locations and how a snapshot expectation is evaluated.
- [CONTRIBUTING.md](CONTRIBUTING.md): pull request and quality gate expectations.
