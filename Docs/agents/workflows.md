# Workflows

Commands run from the repository root. The examples use PowerShell; on Linux and macOS use `./build.sh` instead of `.\build.ps1`.

## Build

```powershell
dotnet build aweXpect.Snapshots.slnx
```

- Builds every project in `Debug`, including a package per library under `Source/*/bin` (`GeneratePackageOnBuild`).
- `Pipeline/Build.csproj` is part of the solution but excluded from its build; `build.ps1` builds and runs it.

## Test

Build first, then run single projects with `--no-build`:

```powershell
dotnet test Tests/aweXpect.Snapshots.Tests/aweXpect.Snapshots.Tests.csproj --no-build -f net10.0 --filter "FullyQualifiedName~DiskStoreTests"
dotnet test Tests/aweXpect.Snapshots.Tests/aweXpect.Snapshots.Tests.csproj --no-build -f net48
dotnet test Tests/aweXpect.Snapshots.Api.Tests/aweXpect.Snapshots.Api.Tests.csproj --no-build
```

| Project                                    | Covers                                                                                                   |
|--------------------------------------------|----------------------------------------------------------------------------------------------------------|
| `aweXpect.Snapshots.Tests`                 | the expectations, rules, maskings, stores, comparers, reporters and acceptance                           |
| `aweXpect.Snapshots.SourceRoots.Tests`     | resolving snapshots through recorded source roots; separate, as it loads assemblies that affect the process |
| `aweXpect.Snapshots.Orphans.Tests`         | `OrphanedSnapshots` with the disk store; separate, as it lists every snapshot of the project directory  |
| `aweXpect.Snapshots.PublicApi.Tests`       | `MatchesPublicApiSnapshot` and `PublicApiDeclarations`, against `PublicApi.Fixture` and `PublicApi.PackageFixture` |
| `aweXpect.Snapshots.DiffEngine.Tests`      | `DiffEngineReporter`; DiffEngine is disabled, so no diff tool starts                                     |
| `aweXpect.Snapshots.Api.Tests`             | the public API of the three packages for every target framework (NUnit, `net8.0` only)                    |

- The unit test projects target `net8.0;net10.0;net48`. `net48` only runs on Windows.
- `.\build.ps1 UnitTests` runs the five unit test projects for every target framework (on Windows also `net48`, through the xUnit console runner). `.\build.ps1 CodeCoverage` adds the coverage report under `TestResults/reports`.

## Snapshots

- A missing or different snapshot writes `<File>.<Method>.awexpect-snapshot.pending.<ext>` next to the test file. Review it and rename it to `<File>.<Method>.awexpect-snapshot.<ext>` to accept it.
- `AWEXPECT_ACCEPT_SNAPSHOTS=all` (or `new`) accepts changed (or only missing) snapshots, but only in `aweXpect.Snapshots.PublicApi.Tests` and `aweXpect.Snapshots.Api.Tests`; the other test projects set the acceptance to `None` so that their results do not depend on the environment. It is ignored when `CI`, `TF_BUILD`, `JENKINS_URL` or `TEAMCITY_VERSION` is set.
- An accepted snapshot still fails the test once; run again to confirm, then review the diff before committing.

## Public API

The API snapshots are `Tests/aweXpect.Snapshots.Api.Tests/ApiApprovalTests.<Assembly>.<Target>.awexpect-snapshot.txt` (dots escaped as `%2E`). The tests read the build output of every target framework in `Source/Directory.Build.props`, so build the solution first.

```powershell
dotnet test Tests/aweXpect.Snapshots.Api.Tests/aweXpect.Snapshots.Api.Tests.csproj --no-build
dotnet test Tests/aweXpect.Snapshots.Api.Tests/aweXpect.Snapshots.Api.Tests.csproj --no-build --filter "Name=AcceptApiChanges"
```

The second command runs the explicit test `AcceptApiChanges`, which overwrites the API snapshots with the current API. `.\build.ps1 ApiChecks` runs the API tests through the pipeline, as CI does.

## Native AOT

```powershell
.\build.ps1 AotSmokeTests
```

Runs `Tests/Aot/aweXpect.Snapshots.Aot` (a console app, not a test project) from the build output, then publishes it with Native AOT for the current platform and runs it again. Trimmer and AOT warnings fail the publish. Each check in `Checks.cs` returns `null` or a failure; its snapshots are committed next to it. Native AOT needs the platform's native toolchain (on Windows the C++ build tools).

## Benchmarks

```powershell
.\build.ps1 BenchmarkDotNet
```

Builds `Benchmarks/aweXpect.Snapshots.Benchmarks` (`net8.0`) in `Release` and runs all benchmarks, writing results to `Artifacts/Benchmarks`. `Benchmarks` also prints the report. Compare before and after a change on the same machine.

## Mutation tests

`.\build.ps1 MutationTestExecution` installs Stryker into `TestResults/dotnet-stryker` and mutates `aweXpect.Snapshots` on `net10.0` with `aweXpect.Snapshots.Tests`, `.SourceRoots.Tests` and `.Orphans.Tests`. Off `main` it only mutates the changes since `main`. Reports go to `Artifacts/Stryker`. It takes long; leave it to CI unless asked.

## Documentation

- `Docs/pages/*.md` is the user documentation, published to [docs.testably.org](https://docs.testably.org/aweXpect/extensions/aweXpect.Snapshots/) after a successful build of `main` (`notify-docs-site.yml`). `00-index.md` embeds the README through `{README}`.
- The README links into `Docs/pages`. `UpdateReadme` (part of `Pack`) writes a copy with release badges to `Artifacts/README.md`, which goes into the packages.
- `Docs/agents/` is for contributors and agents and is not published.

## Pipeline targets

The pipeline in `Pipeline/` uses [Fallout](https://www.nuget.org/packages/Fallout.Common). `.\build.ps1 <Target>` builds and runs it; `.\build.ps1 --help` lists the targets (in a non-interactive shell it may report a failure after the list). Without a target it runs the defaults `ApiChecks`, `Benchmarks` and `CodeAnalysis`, so always name one. `--configuration Release` overrides the default (`Debug` locally, `Release` on a build server).

| Target                  | Does                                                                                     |
|-------------------------|------------------------------------------------------------------------------------------|
| `Compile`               | cleans `Artifacts` and `TestResults`, restores and builds the solution with the GitVersion version |
| `UnitTests`             | runs the unit test projects (`DotNetUnitTests`, and `DotNetFrameworkUnitTests` on Windows) |
| `CodeCoverage`          | `UnitTests` plus an OpenCover report with ReportGenerator                                |
| `ApiChecks`             | runs `aweXpect.Snapshots.Api.Tests`                                                      |
| `AotSmokeTests`         | runs and publishes the AOT smoke tests                                                   |
| `Pack`                  | `UpdateReadme` and `Compile`, then collects the packages into `Artifacts/Packages`       |
| `Benchmarks`            | `BenchmarkDotNet` and `BenchmarkResult`                                                  |
| `MutationTests`         | `MutationTestExecution` and `MutationComment` (the comment only on a pull request)       |
| `CodeAnalysis`          | SonarCloud analysis around `Compile` and `CodeCoverage`; needs `SonarToken`              |
| `BenchmarkComment`, `MutationTestDashboard` | CI only: post results to the pull request and the Stryker dashboard  |

## CI workflows

- `ci.yml` (pull requests to `main`): unit tests with coverage and AOT smoke tests on Ubuntu, Windows and macOS, API tests, mutation tests, benchmarks (not for Dependabot), SonarCloud (not for Dependabot or forks), and a test result summary.
- `ci-analysis.yml` (after `CI`): uploads the mutation report and comments mutation and benchmark results on the pull request.
- `build.yml` (pushes to `main` and tags `v*`): the same jobs plus `Pack`. A tag also pushes the packages to nuget.org, creates a GitHub release for a non-prerelease tag (`vX.Y.Z`) and comments on the released issues and pull requests.
- `notify-docs-site.yml`: triggers a rebuild of the documentation site after a successful `Build` on `main`.
- Dependabot updates GitHub Actions and NuGet weekly with `chore:` commits; Mergify squash-merges them when the unit and API tests pass.
- Versions come from GitVersion. Local builds are `-DEV`, CI builds `-CI`; a tag like `v1.2.3-beta.1` gives a prerelease.
- CONTRIBUTING.md: SonarCloud issues must be solved and coverage must stay above 90%.
