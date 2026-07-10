# Task 1A Report: Buildable Core Solution

## Status

Complete with an environment-specific test-host concern. The source projects,
focused test, core scripts, and solution are implemented within Task 1A scope. No
files under `unity/` were created or modified, and Unity was not invoked.

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
succeeded under .NET SDK `8.0.422`. The following generated-artifact-only
workaround was applied after the successful build:

- Set `MSTest.EnableParentProcessQuery` to `false` in the generated test
  runtimeconfig.
- Temporarily renamed generated `bin/.../testhost.exe` so VSTest selected the
  managed `testhost.dll`.

No source, project, or script workaround was added. With those untracked `bin/`
changes in place, the focused and full runs passed:

```powershell
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --no-build --filter Version_matches_first_release
# Passed 1, Failed 0, Skipped 0, Total 1.

dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --no-build
# Passed 1, Failed 0, Skipped 0, Total 1.
```

## Environment Concern

In the Codex Windows process model, the generated native `testhost.exe` exits with
`-1073741502 (0xC0000142)`. The managed test host also fails its parent-process
monitoring with `System.ComponentModel.Win32Exception (5): Access denied` unless
the generated workaround above is applied. As a result, `tools/Test-Core.ps1`
cannot execute end-to-end in this process model without modifying generated `bin/`
artifacts after its Release build. The script itself remains exactly as planned.

## Self-Review

- `BuildInfo.Version` is a public constant with the required value `0.1.0`.
- The test was written before production code and observed RED specifically for the
  missing symbol.
- Package versions, target frameworks, and build properties match the task brief.
- Generated output is untracked and excluded from the commit.
- No Unity source or project files were touched.
