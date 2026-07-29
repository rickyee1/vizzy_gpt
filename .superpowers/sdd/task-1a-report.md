# Task 1A Report: Buildable Core Solution

## Status

Complete. The source projects, focused test, core scripts, and solution are
implemented within Task 1A scope. No files under `unity/` were created or
modified, and Unity was not invoked.

## Files Changed

- `Directory.Build.props`
- `VizzyGPT.sln`
- `src/VizzyGPT.Core/VizzyGPT.Core.csproj`
- `src/VizzyGPT.Core/BuildInfo.cs`
- `tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj`
- `tests/VizzyGPT.Core.Tests/BuildInfoTests.cs`
- `tools/Test-Core.ps1`
- `tools/Sync-Core.ps1`
- `.superpowers/sdd/task-1a-report.md`

The shared build settings use C# 9, nullable reference types, warnings as errors,
and deterministic builds. The test namespace uses C# 9 block syntax rather than the
plan's C# 10 file-scoped form; its assertion is otherwise unchanged.

## RED Evidence

With .NET SDK `8.0.422`, the focused test was run before `BuildInfo.cs` existed:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter Version_matches_first_release
```

The resulting compiler failure was the expected missing-production-code failure:

```text
BuildInfoTests.cs(11,25): error CS0103: The name 'BuildInfo' does not exist in the current context.
```

## GREEN Evidence

After adding the minimal `BuildInfo.Version` constant, restore and compilation
succeeded under .NET SDK `8.0.422`. In Codex Windows shells, `Test-Core.ps1`
builds Release, updates the generated test runtimeconfig through JSON parsing to
disable the MSTest parent-process query, and disables the generated native
`testhost.exe` so VSTest selects the managed `testhost.dll`. The script then runs
the Release tests with `--no-build`.

The following command reproduces GREEN without manual generated-artifact edits:

```powershell
./tools/Test-Core.ps1
# Passed 1, Failed 0, Skipped 0, Total 1.
```

## Self-Review

- `BuildInfo.Version` is a public constant with the required value `0.1.0`.
- The test was written before production code and observed RED specifically for the
  missing symbol.
- Package versions, target frameworks, and build properties match the task brief.
- Generated `bin/` and `obj/` output is excluded from the commit.
- The Codex-only test-host adaptation is reproducible through `Test-Core.ps1` and
  does not change source or project files.
- No Unity source or project files were touched.
