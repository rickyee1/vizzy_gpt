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

## Phase A Review-Fix Verification

### Reproduced RED

- Ran `powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1` with `CODEX_SHELL=1`.
- Release build: 0 warnings, 0 errors.
- Tests: failed 7, passed 230, skipped 0, total 237.
- Failures were exactly ISO-8601-shaped string coercion, block comment, line comment, trailing object comma, trailing array comma, `NodeSelector` constructor exception type, and `xmlns` attribute acceptance.

### Fixes

- Set `JsonTextReader.DateParseHandling` to `None` before `JObject.Load`.
- Added a string- and escape-aware lexical precheck before loading JSON. It rejects `//` and `/*` only outside strings and rejects commas followed only by whitespace and `}` or `]`.
- Rejects the exact ordinal attribute name `xmlns` in `NodeSpec`.
- Converts both `NodeSelector` constructor contract failures to `PatchApplyException`.

### GREEN Verification

- Re-ran `powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1` with `CODEX_SHELL=1`.
- Release build: 0 warnings, 0 errors.
- Tests: failed 0, passed 237, skipped 0, total 237.
- Scanner self-review: escaped quotes and backslashes retain string state; comment-like and comma-like string content is ignored by the precheck; all other JSON syntax remains delegated to Newtonsoft.

## Strict-JSON Phase A Regression Matrix

- 109 NUnit cases total after adding 22 strict-JSON cases in `VizzyPatchEngineTests`.
- Rejection coverage: single-quoted string values and property names, unquoted property names, invalid `\\'` escapes, raw CR and LF in strings, NBSP outside strings, and Json.NET extension literals `+0`, `00`, `0.`, `NaN`, `Infinity`, `0x0`, and `undefined`.
- Acceptance coverage: comment markers and structural delimiters inside double-quoted strings, every RFC JSON string escape, and legal integral JSON number forms `0`, `-1`, `42`, `1.0`, and `1e0` where a selector ID is accepted.

## Strict-JSON Phase A RED Verification

- RED pending controller execution; tests were intentionally not run in this worktree.

## Strict-JSON Review Fix

### Implementation

- Replaced the comment/trailing-comma scanner with a recursive-descent RFC 8259 syntax validator over the raw patch string.
- The validator enforces JSON whitespace, object/array grammar, double-quoted strings and escapes, number grammar, exact literals, and end-of-input while leaving quoted structural and comment-like text untouched.
- Exact integral JSON number forms within the `Int32` range are canonicalized before Json.NET conversion; selector validation then requires and normalizes an in-range integer token.
- Preserved `DateParseHandling.None`, duplicate-member rejection, and `PatchApplyException` wrapping.

### Verification

RED command:

```powershell
$env:CODEX_SHELL='1'; powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
```

- Build: PASS, 0 warnings, 0 errors.
- Tests: failed 11, passed 248, skipped 0, total 259. The failures matched the strict-JSON regression set.

Focused Task 3 command:

```powershell
dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~VizzyGPT.Core.Tests.Patching.VizzyPatchEngineTests'
```

- Tests: failed 0, passed 242, skipped 0, total 242.

Full GREEN command:

```powershell
$env:CODEX_SHELL='1'; powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
```

- Build: PASS, 0 warnings, 0 errors.
- Tests: failed 0, passed 259, skipped 0, total 259.

## Review Regression Test Matrix

- JSON parser safety: one unknown-member array nested beyond the explicit 64-level protocol limit must fail with a depth-related `PatchApplyException`, avoiding unbounded recursive parsing.
- XML character safety: three `Apply` cases use RFC-valid escaped `\u0001` strings through `updateAttribute`, `addVariable` values, and `NodeSpec` attributes; each must reject with `PatchApplyException` and preserve the input document hash.
- Exact selector-number binding: nine accepted forms cover `Int32` minimum and maximum, negative-zero forms, signed exponents, long fractional zero tails, and exponent-scaled boundaries. Ten rejected forms cover just-outside bounds, near-boundary fractions, fractional exponents, and enormous positive and negative exponents.
- Duplicate protocol members: three cases cover the patch envelope, an operation, and a node selector.
- Parser envelope: three non-object top-level JSON values and one valid object with trailing content must reject.

## Review Regression Static Checks

- Tests are intentionally not run in this test-only worker so the controller can capture RED.
- Scope is limited to the test suite and this Task 3 report; no production code is modified.
- `git diff --check` completed with no whitespace errors; the working tree reports existing LF-to-CRLF conversion warnings only.
- Final ownership review completed: only the two authorized files are modified.

## Depth Guard Clarification

- The depth regression now requires the stable public message fragment `JSON nesting depth exceeds protocol maximum of 64`, distinguishing the custom protocol parser's explicit guard from Json.NET's later `MaxDepth` fallback.
- Tests remain intentionally unrun so the controller can capture RED.

## Parser Bounds and XML Value Review Fix

### Implementation

- The recursive JSON syntax validator counts each object or array as one container level, with the top-level container at level 1, and rejects entry into level 65 with the stable protocol message.
- `XmlConvert.VerifyXmlChars` now validates every patch-controlled string before it is stored as an XML attribute: recursive `NodeSpec` attribute values, `updateAttribute` values, `addVariable` names and values, and `renameVariable` replacement names and references.
- XML validation occurs in operation order against the cloned document and converts `XmlException` failures to `PatchApplyException`, preserving the input document on failure.

### RED Verification

```powershell
$env:CODEX_SHELL='1'; powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
```

- Build: PASS, 0 warnings, 0 errors.
- Tests: failed 4, passed 285, skipped 0, total 289.
- Failures: the parser-owned depth-message case and the three XML-invalid `\u0001` write cases.

### GREEN Verification

Focused Task 3 command:

```powershell
dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~VizzyGPT.Core.Tests.Patching.VizzyPatchEngineTests'
```

- Tests: failed 0, passed 272, skipped 0, total 272.

Full command:

```powershell
$env:CODEX_SHELL='1'; powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
```

- Build: PASS, 0 warnings, 0 errors.
- Tests: failed 0, passed 289, skipped 0, total 289.
- A direct focused run without the repository wrapper built successfully but the sandbox blocked `testhost` parent-process inspection; the focused no-build run succeeded after the wrapper prepared its sandbox-compatible testhost configuration.
