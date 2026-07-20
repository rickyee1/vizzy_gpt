# Task 4 Phase A Report

## Scope

Phase A defines RED contracts only. No production files, project files, fixtures, or pre-existing tests are changed, and the test suite is intentionally not run because the controller owns the RED capture.

## Test Matrix

| File | Contract cases |
| --- | --- |
| `Validation/VizzyProgramValidatorTests.cs` | Missing each required root container; duplicate IDs; unknown styles; unresolved global variables; missing, non-numeric, non-boolean, and conflicting constants; expression placement under `Instructions`; successful runtime serializer callback; report partitions; XML/catalog/runtime issue ordering. |
| `Changes/PendingChangeRebaserTests.cs` | `ChangeSession` snapshot fields and invalid-report refusal; pending serialization payload and dependency fingerprints; unchanged hash; unrelated-edit rebase with only `baseHash` changed; target conflict; ambiguous selector conflict; referenced variable declaration conflict; referenced custom-node declaration conflict. |
| `Storage/FileDataStoreTests.cs` | Pending save/load/delete; UTF-8 without BOM; fingerprint sanitization and root containment; temporary-sibling cleanup across create and replace; 21 backups retain newest 20; backup failure propagates before caller mutation. |

## Planned Public API

```csharp
namespace VizzyGPT.Core.Validation
{
    public enum ValidationSeverity { Error, Warning }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string code, string message, string? path = null);
        public ValidationSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string? Path { get; }
    }

    public sealed class ValidationReport
    {
        public ValidationReport(IReadOnlyList<ValidationIssue> issues);
        public bool IsValid { get; }
        public IReadOnlyList<ValidationIssue> Issues { get; }
        public IReadOnlyList<ValidationIssue> Errors { get; }
        public IReadOnlyList<ValidationIssue> Warnings { get; }
    }

    public sealed class VizzyProgramValidator
    {
        public VizzyProgramValidator(Func<string, ValidationIssue?> runtimeSerializerValidator);
        public ValidationReport Validate(VizzyProgramDocument document, VizzyNodeCatalog catalog);
    }
}

namespace VizzyGPT.Core.Changes
{
    public sealed class ChangeSession
    {
        public static ChangeSession Create(
            VizzyProgramDocument baseDocument,
            PatchDocument patch,
            PatchResult result,
            ValidationReport validation);

        public string BaseXml { get; }
        public string BaseHash { get; }
        public PatchDocument Patch { get; }
        public string ResultXml { get; }
        public string ResultHash { get; }
        public IReadOnlyList<string> PreviewLines { get; }
    }

    public sealed class TargetFingerprint
    {
        public NodeSelector Selector { get; }
        public string Hash { get; }
    }

    public enum DeclarationKind { Variable, CustomNode }

    public sealed class DeclarationFingerprint
    {
        public DeclarationKind Kind { get; }
        public string Name { get; }
        public string Hash { get; }
    }

    public sealed class PendingChange
    {
        public static PendingChange Create(string programFingerprint, ChangeSession session, DateTime createdUtc);
        public string ProgramFingerprint { get; }
        public string BaseXml { get; }
        public string BaseHash { get; }
        public string PatchJson { get; }
        public string ResultXml { get; }
        public string ResultHash { get; }
        public IReadOnlyList<TargetFingerprint> TargetFingerprints { get; }
        public IReadOnlyList<DeclarationFingerprint> DeclarationFingerprints { get; }
        public DateTime CreatedUtc { get; }
        public IReadOnlyList<string> PreviewLines { get; }
    }

    public enum RebaseStatus { Unchanged, Rebased, Conflict }

    public sealed class RebaseResult
    {
        public RebaseStatus Status { get; }
        public PatchDocument? Patch { get; }
        public VizzyProgramDocument? Document { get; }
    }

    public sealed class PendingChangeRebaser
    {
        public RebaseResult TryRebase(PendingChange pending, VizzyProgramDocument current);
    }
}

namespace VizzyGPT.Core.Storage
{
    public interface IDataStore
    {
        Task SaveBackupAsync(string programFingerprint, string xml, DateTime createdUtc, CancellationToken cancellationToken = default);
        Task SavePendingAsync(PendingChange pending, CancellationToken cancellationToken = default);
        Task<PendingChange?> LoadPendingAsync(string programFingerprint, CancellationToken cancellationToken = default);
        Task DeletePendingAsync(string programFingerprint, CancellationToken cancellationToken = default);
    }

    public sealed class FileDataStore : IDataStore
    {
        public FileDataStore(string rootDirectory);
    }
}
```

## Contract Details

- Validation issue codes used by the tests are `MissingContainer`, `DuplicateId`, `UnknownStyle`, `UnresolvedVariable`, `MalformedConstant`, and `InvalidChildPlacement`; runtime callbacks retain their supplied code and severity.
- Global variables are direct `Variables/Variable` declarations keyed by ordinal `name`, and references are elements with `local="false"` and `variableName`.
- Custom-node declarations are direct `Expressions/CustomNode` elements keyed by ordinal `name`; references use `customNodeName`.
- Target fingerprints include every operation `target` and `destination`. Declaration fingerprints include declarations referenced by selected subtrees or inserted/replacement `NodeSpec` trees.
- `Conflict` carries neither a patch nor a document. `Unchanged` exposes the stored patch/result; `Rebased` exposes the base-hash-adjusted patch and its freshly applied result.
- Storage replaces every invalid filename character with `_`, writes pending files below `Pending`, writes timestamped snapshots below `Backups/<sanitized fingerprint>`, and leaves no temporary sibling after a successful operation.

## Phase B Implementation

- Added immutable validation issues/reports and layered Vizzy validation in pure XML, catalog, then runtime-callback order. The pure XML layer checks exact direct root containers, duplicate numeric IDs, ordinal global-variable resolution, constant values, and expression/container placement. Catalog style checks and runtime callback output retain deterministic document order.
- Added validated `ChangeSession` snapshots. Session creation rejects null or inconsistent inputs and any report containing errors, verifies the supplied patch result by reapplying the patch, stores canonical XML/hashes, and appends validation warnings after patch preview lines.
- Added explicit Newtonsoft contracts for pending changes and their selector/declaration fingerprints. Fingerprints use canonical SHA-256 subtree hashes for every target/destination and for ordinal variable/custom-node dependencies found in selected and inserted/replacement trees.
- Added conflict-safe pending rebasing. Matching hashes return the stored patch/result; stale bases require every selector and declaration to resolve exactly once with its recorded hash before a base-hash-only patch clone is applied to the current document.
- Added caller-rooted atomic file storage with invalid-character sanitization, UTF-8 without BOM, sibling temporary files, move/replace publication, failure cleanup, UTC collision-safe backup names, and newest-20 retention.

## Verification Evidence

| Command | Result |
| --- | --- |
| `dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter "FullyQualifiedName~VizzyProgramValidatorTests|FullyQualifiedName~PendingChangeRebaserTests|FullyQualifiedName~FileDataStoreTests"` | RED confirmed before implementation: exit 1 with exactly 17 compiler errors, all missing Task 4 namespaces/types. |
| `dotnet build src/VizzyGPT.Core/VizzyGPT.Core.csproj --no-restore` | Exit 0; 0 warnings, 0 errors after implementation and after final tightening. |
| `powershell -ExecutionPolicy Bypass -File tools\\Test-Core.ps1` | Exit 0; build 0 warnings/0 errors; 319 passed, 0 failed, 0 skipped. |
| `dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~VizzyProgramValidatorTests|FullyQualifiedName~PendingChangeRebaserTests|FullyQualifiedName~FileDataStoreTests"` | Exit 0; 30 passed, 0 failed, 0 skipped. |
| `git status --short -- UserData` plus scoped `UserData` inspection | Clean; `UserData` directory is absent, so the test runs created no game-data files. |
| `git diff --cached --check` | Exit 0; no whitespace errors. |

## Independent Review Regression Matrix

This test-only follow-up adds 24 deterministic cases. They are intentionally not executed here because the controller owns the RED capture.

| File | Added cases | Public behavior |
| --- | ---: | --- |
| `Validation/VizzyProgramValidatorTests.cs` | 11 | Reject nonnumeric and out-of-`Int32` IDs with `InvalidId`; classify catalog instructions/expressions by toolbox section for root and nested placement; reject structural containers under invalid direct parents; preserve pure XML, catalog, runtime ordering. |
| `Changes/PendingChangeRebaserTests.cs` | 5 | Fingerprint `renameVariable`, `removeVariable`, and `updateAttribute(variableName)` base declarations and conflict after declaration edits; allow selectors and declaration references introduced by earlier operations without requiring base fingerprints. |
| `Storage/FileDataStoreTests.cs` | 8 | Reject self-consistent result snapshots that are not patch output; reject removed, empty, or altered target/declaration fingerprint sets; reject a loaded payload whose `ProgramFingerprint` differs from the requested key. |

## Deferred Minor Findings

- Windows reserved device names such as `CON`, `PRN`, and `NUL` are not covered by this Task 4 regression pass. Revisit filename hardening during final branch review.
- Runtime serializer callback exception semantics are not specified by this Task 4 regression pass. Decide whether exceptions propagate or become validation issues during final branch review.
- `PendingChange.Create` analyzes dependencies before construction and the integrity-validating constructor analyzes them again. Removing this duplicate analysis is a performance-only optimization deferred to final branch review.

## Remaining Important Regression Matrix

This second test-only follow-up adds 6 deterministic cases. They are intentionally not executed here because the controller owns the RED capture.

| File | Added cases | Public behavior |
| --- | ---: | --- |
| `Changes/PendingChangeRebaserTests.cs` | 3 | A local `variableName` update succeeds with or without a same-named global and records no global declaration fingerprint; a `replaceNode`-created `CustomNode` may reference its own patch-owned declaration while persisting the applied result. |
| `Storage/FileDataStoreTests.cs` | 3 | Reject empty or altered deterministic preview prefixes; preserve a valid `applied.Changes` prefix followed by legitimate `ChangeSession` warning lines across save/load. |

## Concerns

- A direct Debug `dotnet test` invocation builds successfully but the Codex Windows environment denies the testhost parent-process query. The repository wrapper applies its existing `MSTest.EnableParentProcessQuery=false` workaround; all focused and full tests pass with the wrapper-prepared Release testhost.
- Independent review Important findings are now represented by unexecuted RED contracts; production changes remain outside this commit.

## Independent Review Fix Implementation

- Replaced base-only dependency discovery with an ordered patch analyzer. It applies operations to an annotated clone, preserves immutable base-origin snapshots across in-place edits and moves, and excludes nodes/declarations created or replaced by earlier operations from stale-base fingerprints.
- Added named base declaration dependencies for `renameVariable` and `removeVariable`, plus the declaration assigned by `updateAttribute` when `attribute` is `variableName`. Existing selected-subtree and `NodeSpec` reference discovery remains active and resolves against sequential patch state.
- Hardened the `PendingChange` constructor so persisted base, patch, result, and fingerprints form one derived record. It reapplies the patch, compares canonical result XML/hash, recomputes deterministic target/declaration fingerprints, and rejects missing, extra, duplicated, reordered, or altered records.
- Bound `FileDataStore.LoadPendingAsync` to the requested program fingerprint after deserialization.
- Extended `VizzyNodeCatalog` with immutable ordinal instruction/expression category sets sourced from the toolbox sections. Validation now rejects noncanonical/non-`Int32` IDs, classifies direct instruction/expression placement from catalog metadata, and enforces structural-container parent rules while allowing nested `Instructions` under catalog instructions.

## Patch Operation Audit

| Operation | Dependency/provenance behavior |
| --- | --- |
| `addVariable` | Creates a patch-owned declaration with no stale-base fingerprint; later references resolve against sequential state. |
| `renameVariable` | Fingerprints the named declaration only when it has base provenance; preserves provenance through rename and reference updates. |
| `removeVariable` | Fingerprints the named declaration only when it has base provenance before removal. |
| `insertBefore` | Fingerprints a base-derived target and its referenced declarations; analyzes inserted `NodeSpec` references against current sequential declarations. |
| `insertAfter` | Same as `insertBefore`; later selectors can target the patch-created node without a base fingerprint. |
| `insertChild` | Fingerprints a base-derived destination target and analyzes inserted `NodeSpec` references. |
| `replaceNode` | Fingerprints the replaced base subtree and its references; replacement nodes are patch-owned while preserving patch-engine `pos` behavior. |
| `removeNode` | Fingerprints the removed base subtree and declarations referenced by it. |
| `moveNode` | Fingerprints base-derived target and destination once each, preserves target provenance during the move, and analyzes both subtrees' references. |
| `updateAttribute` | Fingerprints a base-derived target and existing subtree references; `variableName` updates additionally resolve/fingerprint the new base declaration, while patch-created declarations remain patch-owned. |

## Independent Review Verification

| Command | Result |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File tools\\Test-Core.ps1` before fixes | RED reproduced: build 0 warnings/0 errors; 21 failed, 322 passed, 0 skipped, 343 total. |
| `dotnet build src/VizzyGPT.Core/VizzyGPT.Core.csproj --no-restore` | Exit 0; 0 warnings, 0 errors after pending and validation changes. |
| `powershell -ExecutionPolicy Bypass -File tools\\Test-Core.ps1` | Exit 0; build 0 warnings/0 errors; 343 passed, 0 failed, 0 skipped. |
| `dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~VizzyProgramValidatorTests|FullyQualifiedName~PendingChangeRebaserTests|FullyQualifiedName~FileDataStoreTests"` | Exit 0; 54 passed, 0 failed, 0 skipped. |
| `git diff --check` | Exit 0; no whitespace errors. |

## Independent Review Fix Concerns

- The previously documented direct Debug testhost restriction remains environmental; the repository wrapper and wrapper-prepared focused run are green.
- The deferred Windows reserved-device-name, runtime-callback-exception, and duplicate-analysis performance findings remain outside this review fix scope.
- The remaining Important findings are represented by unexecuted test contracts; this follow-up contains no production edits.

## Final Important Regression Fixes

- `updateAttribute(variableName)` now reads the selected target's sequential-state `local` attribute. Exact `local="true"` references remain local and never resolve or fingerprint same-named globals; absent/false local state retains global dependency behavior.
- Same-operation replacement declarations are registered as patch-owned before `NodeSpec` reference analysis. A replacement direct `Expressions/CustomNode` can reference its own new ordinal name, while references to every other declaration in the same tree still resolve and fingerprint normally.
- Persisted previews now require at least the deterministic patch-change count and exact ordinal equality for the complete `VizzyPatchEngine.Apply(...).Changes` prefix. Additional validation warning lines remain permitted after that prefix.
- Existing canonical result replay, dependency recomputation, and defensive collection copies remain unchanged.

## Final Important Verification

| Command | Result |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File tools\\Test-Core.ps1` before fixes | RED reproduced: build 0 warnings/0 errors; 5 failed, 344 passed, 0 skipped, 349 total. |
| `dotnet build src/VizzyGPT.Core/VizzyGPT.Core.csproj --no-restore` | Exit 0; 0 warnings, 0 errors. |
| `powershell -ExecutionPolicy Bypass -File tools\\Test-Core.ps1` | Exit 0; build 0 warnings/0 errors; 349 passed, 0 failed, 0 skipped. |
| `dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~VizzyProgramValidatorTests|FullyQualifiedName~PendingChangeRebaserTests|FullyQualifiedName~FileDataStoreTests"` | Exit 0; 60 passed, 0 failed, 0 skipped. |
| `git diff --check` | Exit 0; no whitespace errors. |

## Final Important Concerns

- No new production concerns were found. The previously deferred Windows reserved-device-name, runtime-callback-exception, and duplicate-analysis performance items remain outside Task 4's Important regression scope.
