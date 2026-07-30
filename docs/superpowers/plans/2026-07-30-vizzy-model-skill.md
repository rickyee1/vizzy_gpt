# Vizzy Model Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every Modify request a bundled Vizzy programming guide and an exact, version-matched node template catalog so the model stops inventing nodes such as `SetThrottle style="set-throttle"`.

**Architecture:** Extend `VizzyNodeCatalog` to retain deterministic canonical templates parsed from the same toolbox XML already used for validation. A new `VizzyModelSkill` formats the semantic guide, bounded template reference, and error-focused repair hints. `VizzyGptPanelWorkflow` captures one catalog snapshot per Modify request and reuses it for the initial model call, local validation, and the one automatic repair.

**Tech Stack:** C# 8-compatible source targeting .NET Standard 2.1, LINQ to XML, NUnit 3, Unity 2022.3.62f1/f3 EditMode tests, Juno ModTools.

## Global Constraints

- `VizzyToolbox.xml` from the running or packaged game resource is the authoritative node schema.
- Do not perform network requests or scrape tutorial websites at game runtime.
- Ask mode must not pay the token cost of the complete node catalog.
- Modify mode may use only element/style combinations present in the captured catalog.
- Keep the catalog reference deterministic and at most 24,000 characters.
- Exactly one workflow-level repair attempt remains allowed.
- Preserve preview-before-apply, protected roots, source hashes, serializer validation, undo, and flight pending changes.
- Do not touch the pre-existing uncommitted `unity/VizzyGPT/Packages/manifest.json` or `packages-lock.json` changes.

---

### Task 1: Deterministic Node Template Catalog

**Files:**
- Modify: `src/VizzyGPT.Core/Programs/VizzyNodeCatalog.cs`
- Create: `src/VizzyGPT.Core/Programs/VizzyModelSkill.cs`
- Create: `tests/VizzyGPT.Core.Tests/Programs/VizzyModelSkillTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<string> VizzyNodeCatalog.Templates`.
- Produces: `string VizzyModelSkill.BuildModifyReference(VizzyNodeCatalog catalog)`.
- Produces: `string VizzyModelSkill.BuildRepairReference(VizzyNodeCatalog catalog, string code, string? path, string message)`.

- [ ] **Step 1: Write failing catalog tests**

Create `VizzyModelSkillTests` with a representative toolbox:

```csharp
private const string Toolbox =
    "<VizzyToolbox><Styles>" +
    "<Style id='set-input' color='CraftInstruction' />" +
    "<Style id='constant' color='Expression' />" +
    "</Styles><Categories><Category name='Craft Instructions'>" +
    "<SetInput style='set-input' input='throttle'><Constant number='0' /></SetInput>" +
    "<SetInput style='set-input' input='throttle'><Constant number='0' /></SetInput>" +
    "</Category></Categories></VizzyToolbox>";
```

Assert that `VizzyNodeCatalog.FromToolboxXml(Toolbox).Templates`:

- contains exactly one duplicate-collapsed template,
- contains `<SetInput input="throttle" style="set-input">`,
- contains its immediate `<Constant number="0" />` child,
- excludes `<Style>` declarations,
- is stable across repeated parses.

Add tests proving `BuildModifyReference` contains the semantic rules, the exact
throttle template, and `Use only listed element/style combinations`, while its
length is at most 24,000 characters for a toolbox with thousands of templates.

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj `
  --filter FullyQualifiedName~VizzyModelSkillTests
```

Expected: compilation fails because `Templates` and `VizzyModelSkill` do not
exist.

- [ ] **Step 3: Retain canonical templates in `VizzyNodeCatalog`**

During `FromToolboxXml`, enumerate direct node children of each
`Categories/Category`, remove UI-only category metadata, clone each node, sort
attributes by ordinal name, remove insignificant whitespace, and serialize
with `SaveOptions.DisableFormatting`.

Store distinct strings in ordinal order:

```csharp
private readonly string[] templates;

public IReadOnlyList<string> Templates => Array.AsReadOnly(templates);
```

Keep existing style, element, instruction, and expression sets unchanged so
validator behavior does not regress. Support existing compact test toolboxes
whose nodes live in `Instructions` or `Expressions` by collecting those direct
children as fallback templates.

- [ ] **Step 4: Implement `VizzyModelSkill`**

Create a static class with:

```csharp
public const int MaximumReferenceCharacters = 24000;

public static string BuildModifyReference(VizzyNodeCatalog catalog);

public static string BuildRepairReference(
    VizzyNodeCatalog catalog,
    string code,
    string? path,
    string message);
```

`BuildModifyReference` emits:

```text
VIZZY MODEL SKILL
- Events start cooperative instruction sequences.
- Instructions execute sequentially; expressions supply typed values.
- Persistent loops must yield with the catalog's wait instruction.
- Craft controls use SetInput; do not invent dedicated throttle/pitch/roll nodes.
- Preserve Program/Variables, Program/Instructions, and Program/Expressions.
- Use only listed element/style combinations and preserve each template's child shape.

CURRENT VIZZY NODE TEMPLATES
...
```

Append only complete template lines. Reserve room for
`[CATALOG TRUNCATED]`, stop before 24,000 characters, and never split XML.

`BuildRepairReference` tokenizes the code, path, and message on punctuation,
selects templates containing meaningful tokens of at least four characters,
and emits at most 4,000 characters. For `set-throttle`, the token `throttle`
must select the valid `SetInput input="throttle"` template. If no template
matches, return the bounded complete reference.

- [ ] **Step 5: Run focused and complete Core tests**

```powershell
dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj `
  --filter FullyQualifiedName~VizzyModelSkillTests
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
```

Expected: all new tests pass, then the complete Core suite passes with zero
warnings and errors.

- [ ] **Step 6: Commit the Core catalog unit**

```powershell
git add src\VizzyGPT.Core\Programs\VizzyNodeCatalog.cs `
  src\VizzyGPT.Core\Programs\VizzyModelSkill.cs `
  tests\VizzyGPT.Core.Tests\Programs\VizzyModelSkillTests.cs
git commit -m "feat: build versioned Vizzy model skill"
```

---

### Task 2: Modify and Repair Context Integration

**Files:**
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptPanelWorkflowTests.cs`
- Modify generated binary: `unity/VizzyGPT/Assets/VizzyGPT/Plugins/VizzyGPT.Core.dll`

**Interfaces:**
- Consumes: `VizzyModelSkill.BuildModifyReference` and
  `BuildRepairReference`.
- Changes private `RequestContext` to retain `VizzyNodeCatalog? NodeCatalog`.
- Produces: one catalog snapshot reused by context generation, validation, and
  repair.

- [ ] **Step 1: Write failing initial-context tests**

In `VizzyGptPanelWorkflowTests`, use a catalog containing:

```xml
<SetInput style="set-input" input="throttle">
  <Constant number="0" />
</SetInput>
```

Capture sent requests and assert:

```csharp
Assert.That(requests[0].Context, Does.Contain("VIZZY MODEL SKILL"));
Assert.That(requests[0].Context, Does.Contain("style=\"set-input\""));
Assert.That(requests[0].Context, Does.Contain("input=\"throttle\""));
Assert.That(createCatalogCalls, Is.EqualTo(1));
```

Add an Ask test asserting its context does not contain
`CURRENT VIZZY NODE TEMPLATES` and its catalog factory is not called.

- [ ] **Step 2: Write failing repair-context test**

Make the first model response insert:

```xml
<SetThrottle style="set-throttle">
  <Constant number="0" />
</SetThrottle>
```

under a valid instruction container so local validation returns
`UnknownStyle`. Return a valid patch on the second send. Assert the second
request contains:

```text
Error code: UnknownStyle
SetInput
style="set-input"
input="throttle"
```

Also assert it does not contain a toolbox `<Style>` declaration, the catalog
factory was called once, and `adapter.SetCalls` remains zero before Apply.

- [ ] **Step 3: Sync Core and run focused Unity tests to verify RED**

```powershell
powershell -ExecutionPolicy Bypass -File tools\Sync-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: the new context assertions fail because the controller does not yet
append the skill or repair templates.

- [ ] **Step 4: Capture one catalog snapshot in `BuildRequestContext`**

For Modify:

```csharp
var catalog = createCatalog();
var sourceReport = new VizzyProgramValidator(adapter.ValidateWithProgramSerializer)
    .Validate(document, catalog);
var skillReference = VizzyModelSkill.BuildModifyReference(catalog);
```

Append `skillReference` after the editor and optional flight contexts. Extend
`RequestContext` with `NodeCatalog`, passing `null` for Ask and `catalog` for
Modify.

In `RunModifyAttemptAsync`, validate with `source.NodeCatalog` and treat a null
Modify catalog as `MissingCatalog` without contacting the provider again.

- [ ] **Step 5: Add focused repair templates**

Change:

```csharp
BuildRepairContext(attempt.Failure, requestContext.SourceHash!)
```

to pass the captured catalog. Append:

```csharp
VizzyModelSkill.BuildRepairReference(
    catalog,
    failure.Code,
    failure.Path,
    failure.Message)
```

The existing original base hash and protected-root instructions remain in the
repair context. Do not include the failed generated document.

- [ ] **Step 6: Run Unity tests and commit**

```powershell
powershell -ExecutionPolicy Bypass -File tools\Sync-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
git diff --check
git add unity\VizzyGPT\Assets\VizzyGPT\Runtime\Ui\VizzyGptPanelController.cs `
  unity\VizzyGPT\Assets\VizzyGPT\Tests\EditMode\VizzyGptPanelWorkflowTests.cs `
  unity\VizzyGPT\Assets\VizzyGPT\Plugins\VizzyGPT.Core.dll
git commit -m "feat: ground Modify requests in Vizzy catalog"
```

Expected: the complete Unity EditMode suite passes and only one catalog
snapshot is used per Modify request.

---

### Task 3: Documentation and Automated Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/manual-test-checklist.md`

**Interfaces:**
- Documents that the model skill is bundled and version-matched.
- Adds regression acceptance for exact throttle-node generation.

- [ ] **Step 1: Update user documentation**

Add a concise README section:

```markdown
### Vizzy model skill

Modify requests include a bundled Vizzy programming guide and node templates
derived from the installed toolbox. No separate Codex skill or documentation
download is required. Local catalog and serializer validation remain the final
authority before Preview.
```

State that unsupported general-purpose libraries such as PyTorch cannot be
implemented inside Vizzy and that generated nodes are restricted to the local
game version.

- [ ] **Step 2: Add manual acceptance items**

Add checklist entries for:

- A request to set throttle produces `SetInput` with `set-input`.
- A fabricated `set-throttle` first response is repaired once using the valid
  template.
- No preview or mutation occurs if both attempts remain outside the catalog.
- Ask mode remains usable without receiving the large node reference.

- [ ] **Step 3: Run full automated verification**

```powershell
$env:CODEX_SHELL='1'
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Sync-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
git diff --check
git status --short
```

Expected: all Core and Unity tests pass, no compiler warnings or errors, and
the two pre-existing package manifest changes remain uncommitted.

- [ ] **Step 4: Commit documentation**

```powershell
git add README.md docs\manual-test-checklist.md
git commit -m "docs: explain bundled Vizzy model skill"
```

---

### Task 4: Package, Install, and Live Acceptance

**Files:**
- Create temporarily: `unity/VizzyGPT/Assets/Editor/VizzyGptBatchModBuilder.cs`
- Produce: `artifacts/VizzyGPT.sr2-mod`
- Install: `C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod`
- Modify after acceptance: `.superpowers/sdd/progress.md`

**Interfaces:**
- Produces the community-installable package with no external skill files.
- Produces live evidence that the valid throttle template reaches Preview.

- [ ] **Step 1: Confirm Juno and Unity are closed**

Do not overwrite the installed package while Juno holds it open. Ask the user
to exit Juno if it is running, then verify no `SimpleRockets2` or Unity editor
process remains.

- [ ] **Step 2: Build the release package**

Create the temporary batch builder using the established
`VizzyGptBatchModBuilder` pattern from prior plans. Invoke ModTools
`BuildAssetBundles` for `BuildTarget.StandaloneWindows64`, with
`debugBuild: false`, outputting:

```text
artifacts/VizzyGPT.sr2-mod
```

Run Unity with `-batchmode -nographics -executeMethod
VizzyGptBatchModBuilder.Build -quit`. Remove the temporary helper and generated
`.meta` files after a successful build. Record:

```powershell
Get-Item artifacts\VizzyGPT.sr2-mod | Select-Object Length, LastWriteTimeUtc
Get-FileHash artifacts\VizzyGPT.sr2-mod -Algorithm SHA256
```

- [ ] **Step 3: Install and launch for acceptance**

Copy the verified artifact to the Mods directory, launch Juno, enable the mod
if required, and open an empty Vizzy program.

- [ ] **Step 4: Verify model skill behavior live**

Using the configured endpoint:

1. Send `把油门设置为 50%` in Modify mode.
2. Confirm the response reaches Preview without `UnknownStyle`.
3. Confirm preview XML/change summary corresponds to `SetInput`,
   `style="set-input"`, `input="throttle"`, and numeric constant `0.5`.
4. Cancel once and confirm the editor remains unchanged.
5. Send an automatic-landing request and confirm every generated node passes
   the local catalog and serializer checks.
6. Apply only after preview, save, reopen Vizzy, and confirm persistence.
7. Confirm Ask mode still answers Chinese Vizzy questions and history remains.

- [ ] **Step 5: Record evidence and publish**

Update `.superpowers/sdd/progress.md` with exact Core/Unity counts, game
version, artifact size/hash, and live outcomes. Commit only the progress file,
push `feature/vizzy-gpt-implementation`, update PR #3, and replace or add the
release asset only after all acceptance steps pass.

Use the requesting-code-review and verification-before-completion skills before
the final push and release update.
