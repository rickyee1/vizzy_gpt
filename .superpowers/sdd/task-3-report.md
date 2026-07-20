# Task 3 Report

## Files Changed

- `tests/VizzyGPT.Core.Tests/Patching/VizzyPatchEngineTests.cs`
- `.superpowers/sdd/task-3-report.md`

## Test Matrix

- 81 NUnit cases total.
- 10 JSON enum round trips, 2 unknown-member cases, and 31 invalid contract/selector/field cases.
- 11 exact canonical-output cases covering all ten operations plus explicit `pos` override; every operation preserves the input hash.
- 25 engine rule cases covering stale hashes, selectors, duplicate IDs, containers, structural roots, moves, and the attribute allowlist.
- 2 defensive-copy and deterministic change-list cases.

## Static Checks

- Static self-review complete.
- `git diff --check` complete with no errors.

## RED Verification

The controller ran `tools/Test-Core.ps1` from commits containing only the Task 3 tests. The core project built, then test compilation failed with 18 expected errors because `VizzyGPT.Core.Patching`, `PatchOperationType`, `PatchDocument`, and `NodeSpec` did not exist.

## Phase B1: Patch Contracts

### Files Implemented

- `src/VizzyGPT.Core/Patching/PatchDocument.cs`
- `src/VizzyGPT.Core/Patching/PatchOperation.cs`
- `src/VizzyGPT.Core/Patching/NodeSelector.cs`
- `src/VizzyGPT.Core/Patching/NodeSpec.cs`

### Contract Coverage

- Exact string-backed operation enum values and strict patch-envelope deserialization.
- Duplicate and unknown JSON member rejection at document, operation, selector, and recursive node-spec levels.
- Required/non-null document and per-operation field validation, including operation-specific field allowlists.
- Defensive read-only copies for patch operations, node attributes, and recursive ordered children.
- Exactly-one selector validation with canonical absolute indexed path syntax.
- Ordinal node attributes, XML NCName validation, unqualified names, duplicate attribute rejection, and 32-bit integer `id` validation.

### Verification

- Focused contract test command compiled both projects, then the environment aborted `testhost` with `Win32Exception (5): Access denied`; no NUnit cases executed.
- `dotnet build src\\VizzyGPT.Core\\VizzyGPT.Core.csproj --no-restore`: PASS, 0 warnings and 0 errors.
- `git diff --cached --check`: PASS with no whitespace errors.
- Staged ownership review: PASS; exactly the four contract files and this report are staged. The shared `VizzyPatchEngine.cs` remains unstaged and untouched.

## Phase B2: Patch Engine

### File Implemented

- `src/VizzyGPT.Core/Patching/VizzyPatchEngine.cs`

### Engine Coverage

- Verifies the base hash before cloning and never mutates the input document.
- Applies all ten operations in order, resolves each selector once, and validates variable scope, instruction containers, protected roots, and move ancestry.
- Preserves `pos` on replacement when omitted, restricts mutable attributes, and rejects duplicate final IDs.
- Returns one deterministic single-line change description per operation.

## GREEN Verification

The controller ran the complete suite after implementation:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
```

```text
Build succeeded: 0 warnings, 0 errors.
Tests: failed 0, passed 231, skipped 0, total 231.
```

## Phase A Review-Fix

- 87 NUnit cases total after adding six strict-protocol regression cases.
- Added coverage for ISO-8601-shaped JSON strings, block and line comments, trailing commas in objects and arrays, and the reserved `xmlns` attribute name.
- Updated direct invalid `NodeSelector` construction expectations to require `PatchApplyException`.
- RED pending controller execution: the current parser allows comments and trailing commas, parses ISO-shaped strings as dates, `NodeSpec` permits `xmlns`, and direct `NodeSelector` validation throws `ArgumentException`.
