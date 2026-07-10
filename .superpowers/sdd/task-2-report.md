# Task 2 Report: Canonical Vizzy Program Model and Hashing

## Files Changed

- `src/VizzyGPT.Core/Programs/VizzyProgramDocument.cs`
- `src/VizzyGPT.Core/Programs/CanonicalXml.cs`
- `src/VizzyGPT.Core/Programs/VizzyProgramHash.cs`
- `src/VizzyGPT.Core/Programs/VizzyNodeCatalog.cs`
- `tests/VizzyGPT.Core.Tests/Fixtures/minimal.xml`
- `tests/VizzyGPT.Core.Tests/Fixtures/nested.xml`
- `tests/VizzyGPT.Core.Tests/Programs/CanonicalXmlTests.cs`
- `.superpowers/sdd/task-2-report.md`

## Implementation

- Parses only a non-namespaced `Program` root and provides independent deep clones, ID lookup, and deterministic indexed path lookup.
- Canonicalizes attributes with ordinal ordering, removes whitespace-only formatting around element children, preserves text and child order, and hashes UTF-8 canonical XML with lowercase SHA-256 hex.
- Extracts toolbox `Style` IDs and XML element names into private immutable ordinal sets with nullable-safe containment checks.

## Fixture Integrity

Initial and final SHA-256 values are identical:

| Fixture | SHA-256 |
| --- | --- |
| `tests/VizzyGPT.Core.Tests/Fixtures/minimal.xml` | `55C97E01A1A2E818EE613C7DB84EAC3F1D7CF6D88464A327CCE462A02477F267` |
| `tests/VizzyGPT.Core.Tests/Fixtures/nested.xml` | `1B0487289A286266C92218ED99975CD3700B3202637972DB01707BD9089F444F` |

The original user flight-program fixture and `VizzyToolbox.xml` were read only and were not modified.

## Checks

- `git diff --check`: completed with no whitespace errors.
- Static review: C# 9 syntax only; nullable return values and null inputs handled; ordinal comparisons used for XML root, paths, catalog membership, and canonical attribute ordering; no mutable catalog collections are exposed.
- `dotnet build src/VizzyGPT.Core/VizzyGPT.Core.csproj --configuration Release --no-restore`: deferred before compilation by the platform SDK-access restriction.

## RED/GREEN Evidence

The existing Task 2 test-first fixtures and `CanonicalXmlTests.cs` were preserved. Per explicit user authorization, production code was implemented before RED/GREEN execution because the platform blocks the required non-sandbox test execution until 19:33.

- RED command deferred: `dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter FullyQualifiedName~CanonicalXmlTests`
- GREEN command attempted: `powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1`
- Actual result: MSBuild failed before compilation/tests with `MSB4184` while resolving `GetPlatformSDKLocation(Windows, 7.0)`: access to `C:\Users\rickyee\AppData\Local\Microsoft SDKs` was denied. Output reported `0` warnings and `1` error. No non-sandbox escalation or bypass was attempted.

## Concern

Automated RED/GREEN and final test-suite evidence remains deferred. The implementation requires a post-19:33 approved non-sandbox run of `powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1`; until then, compile and NUnit results are unknown.
