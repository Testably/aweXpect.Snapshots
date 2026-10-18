# Project structure

## Layout

| Path                    | Contents                                                                                  |
|-------------------------|-------------------------------------------------------------------------------------------|
| `Source/`               | the three packages                                                                        |
| `Tests/`                | unit, API and fixture projects; `Tests/Aot/` holds the Native AOT smoke tests             |
| `Benchmarks/`           | BenchmarkDotNet project                                                                   |
| `Pipeline/`             | the Fallout build (`Build.*.cs`, one file per area), run by `build.ps1`, `build.sh` and `build.cmd` |
| `Docs/pages/`           | user documentation, published to docs.testably.org                                        |
| `Docs/agents/`          | contributor and agent reference (not published)                                           |
| `.github/workflows/`    | CI, see [workflows](workflows.md#ci-workflows)                                            |

## aweXpect.Snapshots

`Source/aweXpect.Snapshots`, depends only on `aweXpect.Core`.

- `ThatSnapshot.MatchesSnapshot.cs` and `.Binary.cs`: the `MatchesSnapshot()` extensions (namespace `aweXpect`) for strings, objects, binary data and files, and the constraints that read, compare, record and accept a snapshot.
- `Results/`: `SnapshotResult<…>` with the options of an expectation (`.Named(…)`, `.NamedAfter(…)`, `.WithExtension(…)`, `.UniqueFor`, `.For`, `.ComparedWith(…)`, `.Accepting(…)`), `BinarySnapshotResult`, and `BinarySnapshotWithoutExtension`, which is no expectation until an extension is set.
- `Snapshots/` (namespace `aweXpect.Snapshots`):
  - Identity and storage: `SnapshotId`, `SnapshotStore` (abstract), `DiskSnapshotStore` (the default, next to the test file), `IFileSnapshotStore`, `SnapshotExtension`, `SnapshotTextEncoding`.
  - Locating files: `SourceRoots` reads the source root, project directory and target framework that `buildTransitive/aweXpect.Snapshots.targets` records as assembly metadata, so that snapshots are found in deterministic builds; `SnapshotEnvironment` provides target framework and operating system for `.UniqueFor`.
  - Rules: `SnapshotFor`, `SnapshotMembers*`, `SnapshotValues*`, `SnapshotLines*`, `SnapshotText`, `SnapshotPattern`, `SnapshotKnownText`, `SnapshotPaths`, `SnapshotEmptyCollections`, applied through `ValueSelection`, `ValueRule`, `LineRule`, `TextMasking`, `StructuralRules` and `StringRules`. `SnapshotRule` defines the same rules for all snapshots; a test (`For_ShouldOfferTheSameRulesAsForAllSnapshots`) keeps both surfaces equal.
  - Extension points: `SnapshotComparer` and `SnapshotComparerRegistration`, `SnapshotReporter` with `SnapshotMismatch` and `SnapshotMatch`. Both are abstract classes, so members can be added with a default.
  - Settings: `SnapshotsAwexpectCustomizationExtensions` adds `Customize.aweXpect.Snapshots()` (store, reporters, comparers, acceptance, rules); `SnapshotSettings` reads them when `MatchesSnapshot()` is called, `SnapshotOptions` holds the options of one expectation.
  - Acceptance: `SnapshotAcceptance` and `AcceptEnvironment`, which reads `AWEXPECT_ACCEPT_SNAPSHOTS` and detects build servers.
  - Orphans: `ClaimedSnapshots` records every snapshot a run uses; `OrphanedSnapshots.CheckAsync()` reports or deletes the others.
- `Polyfills/`: `CallerArgumentExpressionAttribute` and `OverloadResolutionPriorityAttribute` for older targets.

### Evaluating a snapshot

1. `MatchesSnapshot()` resolves the test file through `SourceRoots` from the caller file path and reads the current settings.
2. On evaluation the subject is turned into content with the rules of the expectation and then those for all snapshots applied: a string as text, an object written by the aweXpect formatter, binary data unchanged.
3. The content is compared with the snapshot from the store, by a matching `SnapshotComparer` or by default.
4. A mismatch writes the pending file or, when accepting, replaces the snapshot (the expectation still fails). Reporters are told about mismatches and matches, never on a build server.

## aweXpect.Snapshots.PublicApi

`Source/aweXpect.Snapshots.PublicApi`, namespace `aweXpect.Snapshots.PublicApi`. `PublicApiDeclarations` reads an assembly with Roslyn (`Microsoft.CodeAnalysis.CSharp`) from its metadata and writes its public API as C# declarations, configured by `PublicApiOptions`. Missing package dependencies are resolved from the library's `deps.json` and the NuGet packages folder. `ThatPublicApi.MatchesPublicApiSnapshot` compares the result with a snapshot. Not trimmable or AOT compatible.

## aweXpect.Snapshots.DiffEngine

`Source/aweXpect.Snapshots.DiffEngine`, namespace `aweXpect.Snapshots`. `DiffEngineReporter` opens a diff tool through `IDiffRunner` (`DiffEngineRunner` in production, a recording runner in tests). `buildTransitive/aweXpect.Snapshots.DiffEngine.targets` compiles `aweXpect.Snapshots.DiffEngine.Registration.cs` into consuming projects on .NET 5+ with C# 9+, a module initializer that registers the reporter; `<AweXpectSnapshotsDiffEngine>false</AweXpectSnapshotsDiffEngine>` turns it off.

## Tests

- `aweXpect.Snapshots.Tests`: one file per feature (`ThatSnapshot.MatchesSnapshot.<Feature>Tests.cs`); helpers in `TestHelpers/` (`SnapshotDirectory`, `InMemorySnapshotStore`, `RecordingReporter`, `EnvironmentScope`, `IgnoreAcceptEnvironment`, ...).
- `aweXpect.Snapshots.SourceRoots.Tests` and `aweXpect.Snapshots.Orphans.Tests`: isolated in their own projects, as they affect process-wide state or the project directory.
- `aweXpect.Snapshots.PublicApi.Tests` with `PublicApi.Fixture` (a library covering the language features) and `PublicApi.PackageFixture` (a `netstandard2.0` library whose package dependency is not in its build output).
- `aweXpect.Snapshots.DiffEngine.Tests`: signed, to see the internals of the DiffEngine package.
- `aweXpect.Snapshots.Api.Tests`: the API snapshots of all packages, see [workflows](workflows.md#public-api).
- `Tests/Aot/aweXpect.Snapshots.Aot`: Native AOT smoke tests built deterministically (`ContinuousIntegrationBuild`), so they also cover finding snapshots through the recorded source root.
- Test projects import `aweXpect.Snapshots.targets` through `Tests/Directory.Build.props` (and `Tests/Aot/Directory.Build.props`), since a project reference does not import a package's build targets.
