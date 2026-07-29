# VizzyGPT Resilient Chat UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Modify failures diagnosable and self-repairing while replacing the flat VizzyGPT transcript with a persistent Codex-style conversation showing real reasoning summaries, processing stages, and elapsed time.

**Architecture:** Core owns provider response metadata, protected patch rules, conversation records, and atomic history persistence. The Unity workflow owns request stages, one validation-repair attempt, preview state, and immutable render models; a separate Unity message-list view renders those models and keeps font behavior isolated from request logic.

**Tech Stack:** C# 8-compatible runtime code, .NET Standard 2.1 core library, Newtonsoft.Json, NUnit, Unity 2022.3.62f1/f3, TextMeshPro, ModApi XML UI, PowerShell verification scripts.

## Global Constraints

- Preserve preview-before-mutation, backup, undo, stale-hash, and flight-pending behavior.
- Permit exactly one automatic repair request for a repairable Modify validation failure.
- Never retry authentication, transport, timeout, cancellation, stale-hash, backup, or Apply failures.
- Never display inferred or hidden chain-of-thought; show only provider reasoning summaries and local stage timings.
- Persist at most 50 messages per stable conversation ID.
- Never persist API keys, authorization headers, full program XML, full request context, raw patch JSON, or raw HTTP response bodies.
- Continue applying the bundled Noto Sans CJK SC font to every dynamic prompt, response, reasoning, status, and error text component.
- Keep the unrelated Unity-generated changes in `unity/VizzyGPT/Packages/manifest.json` and `unity/VizzyGPT/Packages/packages-lock.json` out of all commits.

---

### Task 1: Actionable Validation Diagnostics

**Files:**
- Create: `src/VizzyGPT.Core/Diagnostics/ExceptionDiagnostic.cs`
- Modify: `src/VizzyGPT.Core/Validation/VizzyProgramValidator.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Adapters/VizzyRuntimeAdapter.cs`
- Test: `tests/VizzyGPT.Core.Tests/Diagnostics/ExceptionDiagnosticTests.cs`
- Test: `tests/VizzyGPT.Core.Tests/Validation/VizzyProgramValidatorTests.cs`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyRuntimeAdapterDiagnosticTests.cs`

**Interfaces:**
- Produces: `ExceptionDiagnostic.From(Exception exception, string stage)` and `ExceptionDiagnostic.DisplayMessage`.
- Produces: structural container errors that include the actual direct-container count.
- Consumed by: Tasks 4 and 5 for repair context and expandable technical details.

- [ ] **Step 1: Write failing Core tests for wrapper unwrapping and sanitization**

Add tests equivalent to:

```csharp
[Test]
public void From_unwraps_target_invocation_and_redacts_bearer_tokens()
{
    var wrapped = new TargetInvocationException(
        new InvalidOperationException("Bad style. Authorization: Bearer secret-token"));

    var diagnostic = ExceptionDiagnostic.From(wrapped, "RuntimeValidation");

    Assert.That(diagnostic.Code, Is.EqualTo("InvalidOperationException"));
    Assert.That(diagnostic.Stage, Is.EqualTo("RuntimeValidation"));
    Assert.That(diagnostic.DisplayMessage, Does.Contain("Bad style"));
    Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("secret-token"));
    Assert.That(diagnostic.TechnicalDetails, Does.Contain("[REDACTED]"));
}
```

Add a validator test that passes a `Program` with zero direct `Instructions`
children and asserts the error includes `found 0`.

- [ ] **Step 2: Run the focused Core tests and verify RED**

Run:

```powershell
$env:CODEX_SHELL='1'
dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ExceptionDiagnosticTests|FullyQualifiedName~VizzyProgramValidatorTests"
```

Expected: compilation fails because `ExceptionDiagnostic` does not exist, or the count assertion fails against the current generic message.

- [ ] **Step 3: Implement the diagnostic value and counted container message**

Create this public shape:

```csharp
public sealed class ExceptionDiagnostic
{
    private static readonly Regex Bearer = new Regex(
        @"(?i)(authorization\s*:\s*bearer\s+|bearer\s+)[^\s,;]+",
        RegexOptions.CultureInvariant);

    private ExceptionDiagnostic(string code, string stage, string displayMessage, string technicalDetails)
    {
        Code = code;
        Stage = stage;
        DisplayMessage = displayMessage;
        TechnicalDetails = technicalDetails;
    }

    public string Code { get; }
    public string Stage { get; }
    public string DisplayMessage { get; }
    public string TechnicalDetails { get; }

    public static ExceptionDiagnostic From(Exception exception, string stage)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));
        if (string.IsNullOrWhiteSpace(stage)) throw new ArgumentException("Stage is required.", nameof(stage));

        var root = exception;
        while (root.InnerException != null &&
            (root is TargetInvocationException ||
             root is TypeInitializationException ||
             root is AggregateException aggregate && aggregate.InnerExceptions.Count == 1))
        {
            root = root.InnerException;
        }

        var safe = Bearer.Replace(root.Message ?? root.GetType().Name, "$1[REDACTED]");
        return new ExceptionDiagnostic(root.GetType().Name, stage, safe, root.GetType().FullName + ": " + safe);
    }
}
```

Change the structural error to:

```csharp
"Program must contain exactly one direct " + requiredContainer +
    " container; found " + count.ToString(CultureInfo.InvariantCulture) + "."
```

- [ ] **Step 4: Write the failing Unity adapter test**

Add a test seam to `VizzyRuntimeAdapter` through an internal constructor that
accepts `Func<string, Exception?> runtimeValidation`. Assert a wrapped exception
produces a `ValidationIssue` containing the inner message, stage, and no outer
`target of an invocation` text.

- [ ] **Step 5: Run the focused Unity test and verify RED**

Run Unity EditMode with:

```powershell
& 'D:\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe' -batchmode -nographics `
  -projectPath "$PWD\unity\VizzyGPT" -runTests -testPlatform EditMode `
  -testFilter 'VizzyGPT.Tests.EditMode.VizzyRuntimeAdapterDiagnosticTests' `
  -testResults "$PWD\artifacts\task1-results.xml" `
  -logFile "$PWD\artifacts\task1-unity.log"
```

Expected: FAIL because the adapter still exposes `exception.Message`.

- [ ] **Step 6: Use `ExceptionDiagnostic` at every runtime serializer boundary**

Replace direct wrapper messages in `ValidateWithProgramSerializer`,
`TrySetEditorProgramXml`, and refresh rollback reporting with
`ExceptionDiagnostic.From(exception, "<stage>")`. Keep the existing friendly
prefix and append the sanitized root message.

- [ ] **Step 7: Run focused and full tests**

Run the focused Core and Unity commands, then:

```powershell
$env:CODEX_SHELL='1'
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: all existing tests plus new diagnostics tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/VizzyGPT.Core/Diagnostics src/VizzyGPT.Core/Validation/VizzyProgramValidator.cs `
  tests/VizzyGPT.Core.Tests/Diagnostics tests/VizzyGPT.Core.Tests/Validation/VizzyProgramValidatorTests.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Runtime/Adapters/VizzyRuntimeAdapter.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyRuntimeAdapterDiagnosticTests.cs
git commit -m "fix: expose actionable Vizzy validation errors"
```

---

### Task 2: Source Validation, Patch Guardrails, and Agent Rules

**Files:**
- Modify: `src/VizzyGPT.Core/Patching/VizzyPatchEngine.cs`
- Modify: `src/VizzyGPT.Core/Api/OpenAiClient.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Test: `tests/VizzyGPT.Core.Tests/Patching/VizzyPatchEngineTests.cs`
- Test: `tests/VizzyGPT.Core.Tests/Api/OpenAiClientTests.cs`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptPanelWorkflowTests.cs`

**Interfaces:**
- Produces: `VizzyPatchEngine` guarantees protected roots survive every operation.
- Produces: `RequestContext.SourceValidation` so invalid source programs fail before transport.
- Consumed by: Task 4 to decide whether a failed result is repairable.

- [ ] **Step 1: Add failing protected-root regression tests**

Add explicit cases for selectors addressing:

```text
/Program[0]/Variables[0]
/Program[0]/Instructions[0]
/Program[0]/Expressions[0]
```

For `replaceNode`, `removeNode`, and `moveNode`, assert
`PatchApplyException` contains `protected structural root` and that
`document.ToXml()` is unchanged. Add a case replacing an editable instruction
with a top-level `Variables` node and assert rejection before catalog
validation.

- [ ] **Step 2: Run the patch tests and verify the missing guard RED**

Run:

```powershell
$env:CODEX_SHELL='1'
dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VizzyPatchEngineTests"
```

Expected: existing root selectors are rejected with the older generic message,
while the top-level protected node-spec case is accepted by the patch engine.

- [ ] **Step 3: Implement explicit protected-root checks**

Add:

```csharp
private static readonly HashSet<string> ProtectedRootNames =
    new HashSet<string>(new[] { "Variables", "Instructions", "Expressions" }, StringComparer.Ordinal);

private static void RejectProtectedRoot(XElement target, VizzyProgramDocument document, string context)
{
    if (ReferenceEquals(target.Parent, document.Root) &&
        ProtectedRootNames.Contains(target.Name.LocalName))
    {
        throw new PatchApplyException(context + " cannot target a protected structural root.");
    }
}

private static void RejectProtectedNodeSpec(NodeSpec node)
{
    if (ProtectedRootNames.Contains(node.Element))
    {
        throw new PatchApplyException(
            "Patch node cannot create a protected structural root inside an editable subtree.");
    }
}
```

Call `RejectProtectedRoot` before mutation in replace, remove, and move. Call
`RejectProtectedNodeSpec` for the operation's top-level node in insert-before,
insert-after, insert-child, and replace. Preserve nested `Instructions` inside
an `Event` or `While` node spec; only reject a protected name as the operation's
top-level replacement/insertion node.

- [ ] **Step 4: Add failing prompt and source-validation tests**

Assert the system instruction sent to both endpoint modes contains:

```text
Never add, remove, replace, or move the direct Program containers Variables,
Instructions, or Expressions. Modify only their permitted descendants.
```

Add a workflow test with a source program missing direct `Instructions`; assert
`sendAsync` is never called and status reports `Current Vizzy program is not
safe to modify`.

- [ ] **Step 5: Run the focused tests and verify RED**

Run the focused Core filter and Unity workflow filter. Expected: the API prompt
lacks the rule and the workflow currently calls the model before source
validation.

- [ ] **Step 6: Add endpoint rules and validate source before transport**

Use one shared constant in `OpenAiClient` for both Responses input and Chat
system content. In `BuildRequestContext`, after parsing the source, run:

```csharp
var sourceReport = new VizzyProgramValidator(adapter.ValidateWithProgramSerializer)
    .Validate(sourceDocument, createCatalog());
if (!sourceReport.IsValid)
{
    throw new InvalidOperationException(
        "Current Vizzy program is not safe to modify: " + sourceReport.Errors[0].Message);
}
```

Do not classify source validation as a model repair candidate.

- [ ] **Step 7: Run focused and full suites, then commit**

Expected: all tests pass and source-invalid programs make zero transport calls.

```powershell
git add src/VizzyGPT.Core/Patching/VizzyPatchEngine.cs src/VizzyGPT.Core/Api/OpenAiClient.cs `
  tests/VizzyGPT.Core.Tests/Patching/VizzyPatchEngineTests.cs tests/VizzyGPT.Core.Tests/Api/OpenAiClientTests.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptPanelWorkflowTests.cs
git commit -m "fix: guard Vizzy structural roots"
```

---

### Task 3: Provider Reasoning Metadata

**Files:**
- Create: `src/VizzyGPT.Core/Api/AiResponseMetadata.cs`
- Modify: `src/VizzyGPT.Core/Api/AiResponse.cs`
- Modify: `src/VizzyGPT.Core/Api/OpenAiClient.cs`
- Test: `tests/VizzyGPT.Core.Tests/Api/OpenAiClientTests.cs`

**Interfaces:**
- Produces: `AiResponse.Metadata.ReasoningSummary`.
- Produces: optional `InputTokens`, `OutputTokens`, and `WasSchemaRepair`.
- Consumed by: Task 5 conversation render and persistence models.

- [ ] **Step 1: Write failing Responses and Chat Completions metadata tests**

Use a Responses body containing:

```json
{
  "output": [
    {
      "type": "reasoning",
      "summary": [
        { "type": "summary_text", "text": "Checked the control branches." }
      ]
    },
    {
      "type": "message",
      "role": "assistant",
      "content": [
        { "type": "output_text", "text": "{\"message\":\"Done\",\"patch\":{...}}" }
      ]
    }
  ],
  "usage": { "input_tokens": 100, "output_tokens": 20 }
}
```

Assert the summary is normalized with newline joins and usage is retained.
Assert ordinary Chat Completions returns `ReasoningSummary == null`. Add a
schema-repair test asserting `WasSchemaRepair == true`.

- [ ] **Step 2: Run the OpenAI client tests and verify RED**

Expected: `AiResponse.Metadata` does not compile.

- [ ] **Step 3: Implement immutable metadata**

Create:

```csharp
public sealed class AiResponseMetadata
{
    public static readonly AiResponseMetadata Empty =
        new AiResponseMetadata(null, null, null, false);

    public AiResponseMetadata(
        string? reasoningSummary,
        int? inputTokens,
        int? outputTokens,
        bool wasSchemaRepair)
    {
        ReasoningSummary = string.IsNullOrWhiteSpace(reasoningSummary) ? null : reasoningSummary;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        WasSchemaRepair = wasSchemaRepair;
    }

    public string? ReasoningSummary { get; }
    public int? InputTokens { get; }
    public int? OutputTokens { get; }
    public bool WasSchemaRepair { get; }
}
```

Add an optional metadata constructor argument to `AiResponse` while preserving
existing call sites through a default of `null`.

- [ ] **Step 4: Parse only real reasoning summary content**

Extend `ModelExtraction` to carry metadata. For Responses, collect only
`output[type=reasoning].summary[type=summary_text].text` strings. Ignore
encrypted content and unknown reasoning fields. For Chat Completions, accept
`message.reasoning_summary` only when it is a plain string; otherwise leave it
null. Read non-negative integer usage values when present.

When the client's existing JSON-schema repair succeeds, clone metadata with
`WasSchemaRepair = true`.

- [ ] **Step 5: Run full Core tests and commit**

```powershell
$env:CODEX_SHELL='1'
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
git add src/VizzyGPT.Core/Api tests/VizzyGPT.Core.Tests/Api/OpenAiClientTests.cs
git commit -m "feat: normalize provider reasoning metadata"
```

Expected: the full Core suite passes with no warnings.

---

### Task 4: Persistent Conversation Store

**Files:**
- Create: `src/VizzyGPT.Core/Conversations/ConversationMessage.cs`
- Create: `src/VizzyGPT.Core/Conversations/ConversationHistory.cs`
- Create: `src/VizzyGPT.Core/Conversations/IConversationStore.cs`
- Create: `src/VizzyGPT.Core/Conversations/FileConversationStore.cs`
- Test: `tests/VizzyGPT.Core.Tests/Conversations/FileConversationStoreTests.cs`

**Interfaces:**
- Produces: `LoadOrCreateAsync`, `SaveAsync`, `LinkProgramHashAsync`, and `ClearAsync`.
- Produces: stable `ConversationId` aliases across known before/after program hashes.
- Consumed by: Tasks 5 and 7.

The exact store contract is:

```csharp
public interface IConversationStore
{
    Task<ConversationHistory> LoadOrCreateAsync(
        string? programHash,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ConversationHistory history,
        CancellationToken cancellationToken = default);

    Task LinkProgramHashAsync(
        string conversationId,
        string programHash,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        string conversationId,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing retention, alias, corruption, and secrecy tests**

Tests must prove:

```csharp
Assert.That(history.Messages, Has.Count.EqualTo(50));
Assert.That(afterApply.ConversationId, Is.EqualTo(beforeApply.ConversationId));
Assert.That(File.ReadAllText(historyPath), Does.Not.Contain("<Program"));
Assert.That(File.ReadAllText(historyPath), Does.Not.Contain("\"operations\""));
Assert.That(File.ReadAllText(historyPath), Does.Not.Contain("Bearer"));
```

Also write malformed `index.json` and history JSON, then assert
`LoadOrCreateAsync` returns an empty valid history rather than throwing.

- [ ] **Step 2: Run the conversation tests and verify RED**

Expected: compilation fails because conversation types do not exist.

- [ ] **Step 3: Implement the persistent records**

Use these public concepts:

```csharp
public enum ConversationRole { User, Assistant, System }
public enum ConversationMessageKind { Message, Progress, Error, Cancelled }
public enum ConversationMode { Ask, Modify }

public sealed class ConversationStageTiming
{
    public ConversationStageTiming(string stage, double elapsedSeconds)
    {
        Stage = stage;
        ElapsedSeconds = elapsedSeconds;
    }

    public string Stage { get; }
    public double ElapsedSeconds { get; }
}

public sealed class ConversationError
{
    public ConversationError(
        string code,
        string stage,
        string summary,
        string technicalDetails,
        string? path)
    {
        Code = code;
        Stage = stage;
        Summary = summary;
        TechnicalDetails = technicalDetails;
        Path = path;
    }

    public string Code { get; }
    public string Stage { get; }
    public string Summary { get; }
    public string TechnicalDetails { get; }
    public string? Path { get; }
}

public sealed class ConversationMessage
{
    public string Id { get; }
    public ConversationRole Role { get; }
    public ConversationMessageKind Kind { get; }
    public ConversationMode Mode { get; }
    public string Text { get; }
    public string? ReasoningSummary { get; }
    public IReadOnlyList<ConversationStageTiming> Stages { get; }
    public double? ElapsedSeconds { get; }
    public ConversationError? Error { get; }
    public DateTime CreatedUtc { get; }
}

public sealed class ConversationHistory
{
    public ConversationHistory(
        int schemaVersion,
        string conversationId,
        IReadOnlyList<ConversationMessage> messages)
    {
        SchemaVersion = schemaVersion;
        ConversationId = conversationId;
        Messages = messages;
    }

    public int SchemaVersion { get; }
    public string ConversationId { get; }
    public IReadOnlyList<ConversationMessage> Messages { get; }
}
```

Constructors reject null text, non-UTC timestamps, negative durations, unknown
enum values, and unsanitized error fields longer than 2,048 characters.

- [ ] **Step 4: Implement atomic files and stable aliasing**

`FileConversationStore` uses:

```text
Conversations/index.json
Conversations/general.json
Conversations/<conversation-id>.json
```

`LoadOrCreateAsync(programHash)` resolves an existing alias or creates a GUID
conversation ID. `LinkProgramHashAsync(id, newHash)` updates the index
atomically. `SaveAsync` applies `Messages.TakeLast(50)` before serialization.
Reuse the UTF-8, containment, temporary-file, and replace behavior established
by `FileDataStore`, but keep conversation concerns in the new class.

On malformed data, rename nothing and delete nothing; return an empty history
and invoke an injected `Action<string>` warning sink with a sanitized message.

- [ ] **Step 5: Run focused and full Core tests, then commit**

```powershell
$env:CODEX_SHELL='1'
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
git add src/VizzyGPT.Core/Conversations tests/VizzyGPT.Core.Tests/Conversations
git commit -m "feat: persist bounded conversation history"
```

Expected: all Core tests pass and disk assertions confirm excluded content is
absent.

---

### Task 5: Workflow Timeline and One Modify Repair

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/ConversationRenderModels.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/ModifyValidationFailure.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptPanelWorkflowTests.cs`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/ConversationWorkflowPersistenceTests.cs`

**Interfaces:**
- Produces: immutable `ConversationEntryRenderModel` list on `VizzyGptPanelRenderState`.
- Produces: `RefreshElapsed()` for UI timer refresh without persisted timer spam.
- Consumes: `IConversationStore`, `AiResponse.Metadata`, and Task 1 diagnostics.
- Consumed by: Task 6 message-list view.

- [ ] **Step 1: Write failing timeline and retry tests**

Create tests with a controllable UTC clock and queued model responses. Assert:

```csharp
Assert.That(sendCount, Is.EqualTo(2));
Assert.That(secondRequest.Context, Does.Contain("ProtectedRoot"));
Assert.That(secondRequest.Context, Does.Contain(originalHash));
Assert.That(secondRequest.Context, Does.Contain(originalProgramContext));
Assert.That(secondRequest.Context, Does.Not.Contain("failed-output-marker"));
Assert.That(adapter.SetCalls, Is.Zero);
Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
```

Add a double-invalid case asserting exactly two calls, Error state, useful inner
diagnostic, and zero mutations. Add transport, cancellation, stale-hash, and
source-invalid cases asserting exactly one or zero calls as appropriate.

Add stage tests that advance the clock and assert ordered stages:
`ReadingProgram`, `BuildingContext`, `WaitingForModel`, `ParsingPatch`,
`ValidatingPatch`, `PreparingPreview`.

- [ ] **Step 2: Run focused Unity tests and verify RED**

Expected: render state has no structured entries, and validation failure makes
one request only.

- [ ] **Step 3: Add render and failure models**

Use:

```csharp
public enum RequestStage
{
    ReadingProgram,
    BuildingContext,
    WaitingForModel,
    ParsingPatch,
    ValidatingPatch,
    RepairingPatch,
    PreparingPreview,
    SavingPending
}

public sealed class ModifyValidationFailure
{
    public ModifyValidationFailure(
        string code,
        string? path,
        string message,
        string technicalDetails,
        bool isRepairable)
    {
        Code = code;
        Path = path;
        Message = message;
        TechnicalDetails = technicalDetails;
        IsRepairable = isRepairable;
    }

    public string Code { get; }
    public string? Path { get; }
    public string Message { get; }
    public string TechnicalDetails { get; }
    public bool IsRepairable { get; }
}
```

Define the render model as:

```csharp
public sealed class ConversationEntryRenderModel
{
    public ConversationEntryRenderModel(
        string id,
        ConversationRole role,
        string text,
        string? reasoningSummary,
        IReadOnlyList<ConversationStageTiming> stages,
        RequestStage? currentStage,
        double? elapsedSeconds,
        ConversationError? error,
        bool canPreview)
    {
        Id = id;
        Role = role;
        Text = text;
        ReasoningSummary = reasoningSummary;
        Stages = stages;
        CurrentStage = currentStage;
        ElapsedSeconds = elapsedSeconds;
        Error = error;
        CanPreview = canPreview;
    }

    public string Id { get; }
    public ConversationRole Role { get; }
    public string Text { get; }
    public string? ReasoningSummary { get; }
    public IReadOnlyList<ConversationStageTiming> Stages { get; }
    public RequestStage? CurrentStage { get; }
    public double? ElapsedSeconds { get; }
    public ConversationError? Error { get; }
    public bool CanPreview { get; }
}
```

Expanded/collapsed disclosure state remains in `ConversationMessageListView`
and is keyed by message ID; it is not part of the persistent or workflow model.

- [ ] **Step 4: Refactor one request attempt behind a result**

Extract:

```csharp
private async Task<ModifyAttemptResult> RunModifyAttemptAsync(
    string prompt,
    RequestContext source,
    string? repairContext,
    CancellationToken cancellationToken)
```

It sends one `AiRequest`, applies only to `source.SourceDocument`, validates,
and returns either `ChangeSession` or `ModifyValidationFailure`. It never calls
`TrySetEditorProgramXml`.

Build repair context from code, path, message, and original hash only:

```text
MODIFY REPAIR
The previous patch could not be safely previewed.
Error code: <code>
Path: <path-or-root>
Error: <sanitized-message>
Return a complete replacement patch against original base hash <hash>.
Do not add, remove, replace, or move direct Program structural containers.
```

- [ ] **Step 5: Add exactly one repair branch**

`SendPromptAsync` performs the first attempt. When
`ModifyValidationFailure.IsRepairable` is true, advance to `RepairingPatch` and
perform one second attempt. Never loop. The second failure becomes the final
error entry.

Append the user's message immediately. Replace one active progress entry with
the final assistant entry so retries do not duplicate the user's turn.

- [ ] **Step 6: Add history load/save and stable alias updates**

Inject `IConversationStore`. On panel setup, resolve history for the current
program hash or general Ask history. Persist after terminal states. After Apply,
Undo, or editor-return pending Apply, link the resulting hash to the same
conversation ID before saving.

Persistence exceptions add a sanitized warning entry but never disable Ask,
Modify, Preview, or Apply.

- [ ] **Step 7: Add elapsed refresh without persistence writes**

`RefreshElapsed()` re-renders only while Sending. `VizzyGptPanelController`
calls it at most four times per second from `Update()` using unscaled time.
Stage transitions record final durations; timer refreshes do not write history.

- [ ] **Step 8: Run focused and full Unity/Core tests, then commit**

```powershell
powershell -ExecutionPolicy Bypass -File tools\Sync-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
$env:CODEX_SHELL='1'
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui `
  unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode
git commit -m "feat: add resilient Modify conversation workflow"
```

Expected: all tests pass; retry tests prove the adapter is never mutated.

---

### Task 6: Codex-Style Message List and Composer

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/ConversationMessageListView.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/VizzyGptPanel.xml`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/XmlUiComponentContractTests.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/ConversationMessageListViewTests.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/CjkTextFontApplicatorTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<ConversationEntryRenderModel>`.
- Produces: preview, reasoning-toggle, error-toggle, and clear-composer callbacks.
- Preserves: CJK font on every dynamic `TMP_Text`.

- [ ] **Step 1: Write failing XML and dynamic-view tests**

Update the XML contract test to require:

```text
conversation-scroll
conversation-content
composer-input
send-button
cancel-request-button
undo-button
```

Assert obsolete `transcript-text`, `status-text`, `pending-indicator`, and global
`preview-button` are absent.

Create a GameObject-based EditMode test that renders one user and one assistant
entry, then asserts role/body/meta text, one reasoning disclosure, and one
preview action. Toggle reasoning and assert the summary object activates
without resizing the composer.

- [ ] **Step 2: Run focused Unity tests and verify RED**

Expected: required IDs and `ConversationMessageListView` are missing.

- [ ] **Step 3: Replace the panel layout**

Use a `500 x 700` preferred panel constrained by the parent canvas. XML layout:

```xml
<VerticalScrollView id="conversation-scroll" class="no-image"
  anchorMin="0 0" anchorMax="1 1" offsetMin="16 148" offsetMax="-16 -112">
  <VerticalLayout id="conversation-content" class="no-image"
    childForceExpandHeight="false" spacing="12" />
</VerticalScrollView>
<TextMeshProInputField id="composer-input" lineType="MultiLineNewline"
  rectAlignment="LowerCenter" height="88" width="468" offsetXY="0 50">
  <TMP_Text color="White" />
</TextMeshProInputField>
```

Keep the existing header controls. Put Undo at lower left and one Send/Cancel
icon at lower right. Do not retain a status label that can overlap messages.

- [ ] **Step 4: Implement dynamic rows**

`ConversationMessageListView` owns row GameObjects under
`conversation-content`. Each row uses a stable message ID for incremental
updates and creates:

- role label,
- body text,
- elapsed metadata,
- collapsed reasoning button and detail text,
- collapsed technical-error button and detail text,
- contextual Preview button.

Use `VerticalLayoutGroup`, `ContentSizeFitter`, `LayoutElement`, `Image`,
`Button`, and `TextMeshProUGUI`. Use square or at most 6px-equivalent corner
styling available from the stock panel; do not create nested decorative cards.
Assistant rows remain full width, user rows use a restrained contrasting
background, and error details use text emphasis rather than a saturated panel.

- [ ] **Step 5: Wire actions and scrolling**

On a new user send or assistant completion, rebuild layout and scroll to bottom.
Do not force scrolling while the user has manually scrolled upward. Preview
calls the existing `ShowPreview`. Disclosure toggles remain local UI state and
do not alter persisted messages.

Clear the composer after a send is accepted, not when validation rejects an
empty prompt. While Sending, show Cancel in the same command position as Send.

- [ ] **Step 6: Apply CJK font to all dynamic text**

Pass the bundled font into `ConversationMessageListView`. Every created
`TextMeshProUGUI` must call:

```csharp
CjkTextFontApplicator.ApplyToText(cjkFont, textComponent);
```

`RefreshDynamicTextFonts` must include rows created after initial bind.

- [ ] **Step 7: Run full Unity tests and commit**

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui `
  unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/VizzyGptPanel.xml `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode
git commit -m "feat: add Codex-style VizzyGPT conversation UI"
```

Expected: full EditMode suite passes and layout tests prove removed controls
cannot return accidentally.

---

### Task 7: Clear History, Packaging, and Live Acceptance

**Files:**
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SettingsDialogController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/SettingsDialog.xml`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/SettingsDialogControllerTests.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/XmlUiComponentContractTests.cs`
- Modify: `docs/manual-test-checklist.md`
- Modify: `.superpowers/sdd/progress.md`

**Interfaces:**
- Consumes: `IConversationStore.ClearAsync(conversationId)`.
- Produces: a settings command that clears only the active conversation.
- Produces: final installed `.sr2-mod` and updated draft PR.

- [ ] **Step 1: Write failing clear-history tests**

Inject a `Func<CancellationToken, Task> clearHistoryAsync` into the settings
controller. Assert one click calls it once, reports `Current conversation
cleared.`, and does not save or clear endpoint settings.

Update XML contract tests to require `clear-history-button`.

- [ ] **Step 2: Run focused Unity tests and verify RED**

Expected: constructor/callback and XML button are absent.

- [ ] **Step 3: Implement clear-history**

Add a secondary button above the settings footer:

```xml
<Button id="clear-history-button" class="btn" rectAlignment="LowerLeft"
  width="170" offsetXY="20 64" onClick="OnClearHistoryButtonClicked();">
  <TextMeshPro text="Clear Chat History" />
</Button>
```

The view awaits the callback, updates status, and asks the active panel workflow
to replace its render list with an empty history. It does not close settings.

- [ ] **Step 4: Run complete automated verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Sync-Core.ps1
$env:CODEX_SHELL='1'
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
git diff --check
git status --short
```

Expected: zero Core failures, zero Unity failures, no compiler warnings or
errors, and only intended files plus the two pre-existing package-manifest
changes appear.

- [ ] **Step 5: Build and install the release candidate**

Close Juno and Unity. Create the temporary Editor helper
`unity/VizzyGPT/Assets/Editor/VizzyGptBatchModBuilder.cs` following the existing
`VizzyGptBatchModBuilder` implementation recorded in
`.superpowers/sdd/task-4-report.md`. Invoke ModTools `BuildAssetBundles` for
`BuildTarget.StandaloneWindows64`, `debugBuild: false`, and output:

```text
artifacts/VizzyGPT.sr2-mod
```

Run Unity with `-batchmode -nographics -executeMethod
VizzyGptBatchModBuilder.Build -quit` and log to
`artifacts/unity-package-resilient-chat.log`. Delete the helper, its `.meta`,
and an empty generated `Assets/Editor.meta` after a successful build.

Install the artifact at:

```text
C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod
```

Record file size, UTC timestamp, and SHA-256 before launching Juno.

- [ ] **Step 6: Perform manual Juno acceptance**

Verify every item from the design:

1. Chinese Ask input/response and persistent history after game restart.
2. Stage names and live elapsed time while waiting.
3. A Responses reply with real reasoning summary and a reply without one.
4. Modify preview/cancel, regenerate, preview/apply, save, reopen, and persist.
5. A fake/local response violating a protected root, followed by one valid
   automatic repair.
6. Two invalid responses produce one repair only, no mutation, and useful
   expandable inner error details.
7. Flight pending creation, editor-return rebase, preview, and Apply.
8. Clear current history without changing endpoint settings.
9. No overlapping text or controls at `2560x1600`, `1920x1080`, and a narrow
   windowed viewport.

- [ ] **Step 7: Update evidence and commit**

Update the checklist and progress file with exact test counts, game version,
artifact hash, and acceptance outcomes.

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SettingsDialogController.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/SettingsDialog.xml `
  unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode `
  docs/manual-test-checklist.md .superpowers/sdd/progress.md
git commit -m "feat: finish persistent VizzyGPT chat experience"
```

- [ ] **Step 8: Review, push, and update the draft PR**

Use the requesting-code-review skill, address all accepted findings, rerun the
full verification commands, and then:

```powershell
git push origin feature/vizzy-gpt-implementation
```

Update draft PR `rickyee1/vizzy_gpt#1` with the new UX, retry behavior, test
counts, Juno acceptance, and final package SHA-256. Keep it draft until live
acceptance is complete.
