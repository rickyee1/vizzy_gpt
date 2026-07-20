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
