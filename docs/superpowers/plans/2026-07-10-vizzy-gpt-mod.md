# VizzyGPT Mod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one Windows `.sr2-mod` that adds a GPT assistant to the Vizzy editor and flight scene, produces locally validated structured program patches, and applies them only after user preview and approval.

**Architecture:** Keep XML normalization, patching, validation, API protocol, and persistence in a pure .NET Standard core library with NUnit tests. Keep Juno lifecycle, Unity UI, `ProgramSerializer`, telemetry, DPAPI, and editor reflection inside a thin Unity runtime assembly behind explicit interfaces. Build and package the runtime with the matching Juno ModTools project.

**Tech Stack:** C# 9, .NET Standard 2.1, .NET 8 test runner, NUnit 3, Newtonsoft.Json 13, Unity 2022.3.62f3, Juno ModTools, `ModApi.Craft.Program.ProgramSerializer`, Unity XML UI, UnityWebRequest, Windows DPAPI.

## Global Constraints

- Target Juno: New Origins `1.4.0.8c` on the `open_beta` branch.
- Target Windows 11 only in the first release.
- Use `D:/Steam/steamapps/common/SimpleRockets2/ModTools/SimpleRockets2_ModTools.unitypackage` as the ModTools source.
- Use Unity `2022.3.62f3`, matching the installed game runtime reported by `Player.log`.
- Deliver one installable `VizzyGPT.sr2-mod`; do not require a sidecar process.
- Never mutate a Vizzy program during flight.
- Never modify files under `UserData/FlightPrograms` during automated tests; copy fixtures into the repository first.
- Require preview-before-apply and a successful backup before every editor mutation.
- Support the OpenAI Responses API and Chat Completions API through official or custom OpenAI-compatible base URLs.
- Reject plain HTTP except for loopback hosts.
- Store mod data under `UserData/VizzyGPT`; encrypt the API key with Windows DPAPI.
- Keep all reflection inside `VizzyRuntimeAdapter`; public ModApi APIs are preferred everywhere else.

---

## Planned File Structure

```text
VizzyGPTMod/
  Directory.Build.props
  VizzyGPT.sln
  src/VizzyGPT.Core/
    VizzyGPT.Core.csproj
    Programs/VizzyProgramDocument.cs
    Programs/CanonicalXml.cs
    Programs/VizzyProgramHash.cs
    Programs/VizzyNodeCatalog.cs
    Patching/PatchDocument.cs
    Patching/PatchOperation.cs
    Patching/NodeSelector.cs
    Patching/NodeSpec.cs
    Patching/VizzyPatchEngine.cs
    Validation/ValidationIssue.cs
    Validation/VizzyProgramValidator.cs
    Changes/ChangeSession.cs
    Changes/PendingChange.cs
    Changes/PendingChangeRebaser.cs
    Storage/IDataStore.cs
    Storage/FileDataStore.cs
    Api/ApiMode.cs
    Api/AiRequest.cs
    Api/AiResponse.cs
    Api/IAiTransport.cs
    Api/OpenAiClient.cs
    Api/ContextBuilder.cs
    Security/SecretRedactor.cs
  tests/VizzyGPT.Core.Tests/
    VizzyGPT.Core.Tests.csproj
    Fixtures/minimal.xml
    Fixtures/nested.xml
    Programs/CanonicalXmlTests.cs
    Patching/VizzyPatchEngineTests.cs
    Validation/VizzyProgramValidatorTests.cs
    Changes/PendingChangeRebaserTests.cs
    Storage/FileDataStoreTests.cs
    Api/OpenAiClientTests.cs
    Security/SecretRedactorTests.cs
  unity/VizzyGPT/
    Assets/VizzyGPT/Runtime/VizzyGPT.Runtime.asmdef
    Assets/VizzyGPT/Runtime/VizzyGptMod.cs
    Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs
    Assets/VizzyGPT/Runtime/Adapters/IVizzyRuntimeAdapter.cs
    Assets/VizzyGPT/Runtime/Adapters/VizzyRuntimeAdapter.cs
    Assets/VizzyGPT/Runtime/Adapters/RuntimeContractProbe.cs
    Assets/VizzyGPT/Runtime/Api/UnityWebRequestTransport.cs
    Assets/VizzyGPT/Runtime/Security/DpapiSecretProtector.cs
    Assets/VizzyGPT/Runtime/Storage/JunoDataPaths.cs
    Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs
    Assets/VizzyGPT/Runtime/Ui/PreviewDialogController.cs
    Assets/VizzyGPT/Runtime/Ui/SettingsDialogController.cs
    Assets/VizzyGPT/Runtime/Flight/FlightContextCollector.cs
    Assets/VizzyGPT/Runtime/Flight/TelemetrySampler.cs
    Assets/VizzyGPT/Runtime/Resources/Ui/VizzyGptPanel.xml
    Assets/VizzyGPT/Runtime/Resources/Ui/PreviewDialog.xml
    Assets/VizzyGPT/Runtime/Resources/Ui/SettingsDialog.xml
    Assets/VizzyGPT/Tests/EditMode/VizzyGPT.EditModeTests.asmdef
    Assets/VizzyGPT/Tests/EditMode/ProgramSerializerContractTests.cs
  tools/Sync-Core.ps1
  tools/Test-Core.ps1
  tools/Test-Unity.ps1
  docs/manual-test-checklist.md
```

The SR2 Mod Builder owns its generated manifest, mod-data asset, root assembly definition, and bundle metadata. Commit those generated files after initialization, but continue editing mod metadata only through the SR2 Mod Builder window.

---

### Task 1: Buildable Core Solution and Loadable Mod Skeleton

**Files:**
- Create: `Directory.Build.props`
- Create: `VizzyGPT.sln`
- Create: `src/VizzyGPT.Core/VizzyGPT.Core.csproj`
- Create: `src/VizzyGPT.Core/BuildInfo.cs`
- Create: `tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj`
- Create: `tests/VizzyGPT.Core.Tests/BuildInfoTests.cs`
- Create: `tools/Sync-Core.ps1`
- Create: `tools/Test-Core.ps1`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGPT.Runtime.asmdef`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptMod.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`

**Interfaces:**
- Produces: `VizzyGPT.Core.BuildInfo.Version`, buildable `VizzyGPT.Core.dll`, and a Juno-discoverable `VizzyGptMod : GameModBase`.
- Produces: `VizzyGptBehaviour` as the Unity lifecycle owner used by later runtime tasks.

- [ ] **Step 1: Verify the required Unity editor before creating the project**

Run:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
if (-not (Test-Path -LiteralPath $unity)) { throw "Install Unity 2022.3.62f3 with Windows Mono build support: $unity" }
& $unity -version
```

Expected: the command prints `2022.3.62f3`. If it is absent, install that exact editor in Unity Hub before continuing.

- [ ] **Step 2: Create the .NET solution and project files**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Create `src/VizzyGPT.Core/VizzyGPT.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>VizzyGPT.Core</AssemblyName>
    <RootNamespace>VizzyGPT.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

Create `tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <ProjectReference Include="..\..\src\VizzyGPT.Core\VizzyGPT.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Run:

```powershell
dotnet new sln --name VizzyGPT
dotnet sln VizzyGPT.sln add src/VizzyGPT.Core/VizzyGPT.Core.csproj
dotnet sln VizzyGPT.sln add tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj
```

Expected: `dotnet sln VizzyGPT.sln list` prints both projects.

- [ ] **Step 3: Write the failing build-info test**

Create `tests/VizzyGPT.Core.Tests/BuildInfoTests.cs`:

```csharp
using NUnit.Framework;
using VizzyGPT.Core;

namespace VizzyGPT.Core.Tests;

public sealed class BuildInfoTests
{
    [Test]
    public void Version_matches_first_release()
    {
        Assert.That(BuildInfo.Version, Is.EqualTo("0.1.0"));
    }
}
```

- [ ] **Step 4: Run the test and verify the expected failure**

Run:

```powershell
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter Version_matches_first_release
```

Expected: FAIL with `The name 'BuildInfo' does not exist`.

- [ ] **Step 5: Add the minimal core implementation and build scripts**

Create `src/VizzyGPT.Core/BuildInfo.cs`:

```csharp
namespace VizzyGPT.Core;

public static class BuildInfo
{
    public const string Version = "0.1.0";
}
```

Create `tools/Test-Core.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $root 'VizzyGPT.sln') --configuration Release
```

Create `tools/Sync-Core.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'src\VizzyGPT.Core\VizzyGPT.Core.csproj') --configuration Release
$source = Join-Path $root 'src\VizzyGPT.Core\bin\Release\netstandard2.1\VizzyGPT.Core.dll'
$targetDir = Join-Path $root 'unity\VizzyGPT\Assets\VizzyGPT\Plugins'
New-Item -ItemType Directory -Force $targetDir | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $targetDir 'VizzyGPT.Core.dll') -Force
```

- [ ] **Step 6: Run core tests and verify they pass**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
```

Expected: PASS, 1 test.

- [ ] **Step 7: Create and initialize the Unity ModTools project**

Run:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
& $unity -batchmode -quit -createProject "$PWD\unity\VizzyGPT"
& $unity -batchmode -quit -projectPath "$PWD\unity\VizzyGPT" -importPackage 'D:\Steam\steamapps\common\SimpleRockets2\ModTools\SimpleRockets2_ModTools.unitypackage'
```

Open the project once, choose `SimpleRockets 2 > Mod Builder Window`, enter name `VizzyGPT`, author `rickyee`, description `GPT assistant for Vizzy`, version `0.1.0`, and click `Start Creating Mod`. Commit every builder-generated manifest and configuration asset without hand-editing it.

- [ ] **Step 8: Add the loadable mod entry point**

Create `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGPT.Runtime.asmdef`:

```json
{
  "name": "VizzyGPT.Runtime",
  "rootNamespace": "VizzyGPT.Runtime",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": true
}
```

Create `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptMod.cs`:

```csharp
using Jundroo.ModTools;
using UnityEngine;

namespace VizzyGPT.Runtime;

public sealed class VizzyGptMod : GameModBase
{
    protected override void OnModInitialized()
    {
        var root = new GameObject("VizzyGPT");
        Object.DontDestroyOnLoad(root);
        root.AddComponent<VizzyGptBehaviour>();
    }
}
```

Create `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`:

```csharp
using UnityEngine;

namespace VizzyGPT.Runtime;

public sealed class VizzyGptBehaviour : MonoBehaviour
{
    private void Awake() => Debug.Log("VizzyGPT 0.1.0 initialized");
}
```

Run `tools/Sync-Core.ps1`, let Unity compile, save `VizzyGPT.sr2-mod` with the SR2 Mod Builder into the game's `Mods` folder, enable it, restart the game, and verify `ModLoadLog.txt` contains `Mod Loaded: VizzyGPT` and `Player.log` contains `VizzyGPT 0.1.0 initialized`.

- [ ] **Step 9: Commit the baseline**

```powershell
git add Directory.Build.props VizzyGPT.sln src tests tools unity
git commit -m "build: create loadable VizzyGPT mod skeleton"
```

---

### Task 2: Canonical Vizzy Program Model and Hashing

**Files:**
- Create: `src/VizzyGPT.Core/Programs/VizzyProgramDocument.cs`
- Create: `src/VizzyGPT.Core/Programs/CanonicalXml.cs`
- Create: `src/VizzyGPT.Core/Programs/VizzyProgramHash.cs`
- Create: `src/VizzyGPT.Core/Programs/VizzyNodeCatalog.cs`
- Create: `tests/VizzyGPT.Core.Tests/Fixtures/minimal.xml`
- Copy: `tests/VizzyGPT.Core.Tests/Fixtures/nested.xml` from a user flight program copy, then redact its name only.
- Create: `tests/VizzyGPT.Core.Tests/Programs/CanonicalXmlTests.cs`

**Interfaces:**
- Produces: `VizzyProgramDocument.Parse(string)`, `Clone()`, `ToXml()`, `FindById(int)`, and `FindByPath(string)`.
- Produces: `VizzyProgramHash.Compute(VizzyProgramDocument)` returning lowercase SHA-256 hex.
- Produces: `VizzyNodeCatalog.FromToolboxXml(string)` for runtime validation.

- [ ] **Step 1: Add representative XML fixtures**

Create `tests/VizzyGPT.Core.Tests/Fixtures/minimal.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Program name="Minimal">
  <Variables><Variable name="pitch" number="0" /></Variables>
  <Instructions>
    <Event event="FlightStart" id="0" style="flight-start" pos="0,0" />
  </Instructions>
  <Expressions />
</Program>
```

Copy `UserData/FlightPrograms/unsdeady FC 2modes.xml` to `tests/VizzyGPT.Core.Tests/Fixtures/nested.xml` and change only the root `name` attribute to `NestedFixture`.

- [ ] **Step 2: Write failing canonicalization tests**

Create `tests/VizzyGPT.Core.Tests/Programs/CanonicalXmlTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Tests.Programs;

public sealed class CanonicalXmlTests
{
    [Test]
    public void Attribute_order_does_not_change_hash()
    {
        var a = VizzyProgramDocument.Parse("<Program name='A'><Variables/><Instructions><Event id='0' style='flight-start' event='FlightStart'/></Instructions><Expressions/></Program>");
        var b = VizzyProgramDocument.Parse("<Program name='A'><Variables/><Instructions><Event event='FlightStart' style='flight-start' id='0'/></Instructions><Expressions/></Program>");
        Assert.That(VizzyProgramHash.Compute(a), Is.EqualTo(VizzyProgramHash.Compute(b)));
    }

    [Test]
    public void Clone_is_independent_and_ids_are_queryable()
    {
        var xml = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "minimal.xml"));
        var original = VizzyProgramDocument.Parse(xml);
        var clone = original.Clone();
        clone.FindById(0)!.SetAttributeValue("event", "ReceiveMessage");
        Assert.That(original.FindById(0)!.Attribute("event")!.Value, Is.EqualTo("FlightStart"));
    }
}
```

- [ ] **Step 3: Run tests and verify they fail**

Run:

```powershell
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter FullyQualifiedName~CanonicalXmlTests
```

Expected: FAIL because `VizzyProgramDocument` and `VizzyProgramHash` do not exist.

- [ ] **Step 4: Implement parsing, canonicalization, lookup, and hashing**

Create `src/VizzyGPT.Core/Programs/VizzyProgramDocument.cs` with an `XElement` root, defensive parse requiring root `Program`, deep clone via `new XElement(Root)`, ID lookup using `DescendantsAndSelf()`, and deterministic indexed paths in the form `/Program[0]/Instructions[0]/Event[0]`.

Create `src/VizzyGPT.Core/Programs/CanonicalXml.cs` so each element is emitted with attributes ordered by ordinal name, insignificant whitespace removed, text preserved, and child order unchanged.

Create `src/VizzyGPT.Core/Programs/VizzyProgramHash.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace VizzyGPT.Core.Programs;

public static class VizzyProgramHash
{
    public static string Compute(VizzyProgramDocument document)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalXml.Write(document.Root)));
        var result = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes) result.Append(value.ToString("x2"));
        return result.ToString();
    }
}
```

Create `VizzyNodeCatalog` as immutable sets of style IDs and XML element names parsed from the current `VizzyToolbox.xml`; expose `ContainsStyle(string)` and `ContainsElement(string)`.

- [ ] **Step 5: Run the complete core test suite**

Run `tools/Test-Core.ps1`.

Expected: all tests pass and both fixtures remain byte-for-byte unchanged after the run.

- [ ] **Step 6: Commit the program model**

```powershell
git add src/VizzyGPT.Core/Programs tests/VizzyGPT.Core.Tests/Programs tests/VizzyGPT.Core.Tests/Fixtures
git commit -m "feat: add canonical Vizzy program model"
```

---

### Task 3: Typed Patch Protocol and Patch Engine

**Files:**
- Create: `src/VizzyGPT.Core/Patching/PatchDocument.cs`
- Create: `src/VizzyGPT.Core/Patching/PatchOperation.cs`
- Create: `src/VizzyGPT.Core/Patching/NodeSelector.cs`
- Create: `src/VizzyGPT.Core/Patching/NodeSpec.cs`
- Create: `src/VizzyGPT.Core/Patching/VizzyPatchEngine.cs`
- Create: `tests/VizzyGPT.Core.Tests/Patching/VizzyPatchEngineTests.cs`

**Interfaces:**
- Consumes: `VizzyProgramDocument` and `VizzyProgramHash.Compute` from Task 2.
- Produces: `PatchDocument { BaseHash, Summary, Operations }` deserializable by Newtonsoft.Json.
- Produces: `VizzyPatchEngine.Apply(VizzyProgramDocument, PatchDocument) -> PatchResult` without mutating the input.

- [ ] **Step 1: Define the JSON contract and failing operation tests**

Define `PatchOperationType` with exact JSON string values `addVariable`, `renameVariable`, `removeVariable`, `insertBefore`, `insertAfter`, `insertChild`, `replaceNode`, `removeNode`, `moveNode`, and `updateAttribute`.

Define `NodeSelector` with exactly one of nullable `Id` or `Path`. Define recursive `NodeSpec` with `Element`, ordinal-keyed `Attributes`, and ordered `Children`.

Write parameterized tests that apply every operation to `minimal.xml` or `nested.xml`, assert that the original hash is unchanged, and assert the exact resulting canonical XML. Add rejection tests for a stale hash, ambiguous path, missing target, duplicate node ID, and a move into the moved node's own descendant.

- [ ] **Step 2: Run patch tests and verify they fail**

Run:

```powershell
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter FullyQualifiedName~VizzyPatchEngineTests
```

Expected: FAIL because patch protocol types do not exist.

- [ ] **Step 3: Implement immutable patch contracts**

Use Newtonsoft.Json attributes with `MissingMemberHandling.Error` during deserialization. Require `BaseHash`, non-empty `Summary`, and at least one operation. `NodeSpec.ToXElement()` must reject empty element names, namespace-qualified names, duplicate attributes, and `id` values that are not integers.

The public result type is:

```csharp
namespace VizzyGPT.Core.Patching;

public sealed class PatchResult
{
    public PatchResult(VizzyProgramDocument document, IReadOnlyList<string> changes)
    {
        Document = document;
        Changes = changes;
    }

    public VizzyProgramDocument Document { get; }
    public IReadOnlyList<string> Changes { get; }
}
```

- [ ] **Step 4: Implement all patch operations on a clone**

`VizzyPatchEngine.Apply` must compare `patch.BaseHash` with the input hash before cloning. Resolve each selector exactly once for every operation. Apply operations in list order. Variable operations target only direct children of `/Program[0]/Variables[0]`. Node inserts and moves require an `Instructions` container or a sibling under one. `replaceNode` preserves the target's `pos` only when the replacement omits `pos`. `updateAttribute` allows `text`, `number`, `bool`, `style`, `event`, `property`, `op`, `variableName`, and `pos`; it rejects `id` changes.

After all operations, scan every `id` attribute and throw `PatchApplyException` on duplicates. Return one human-readable change line per operation for the preview.

- [ ] **Step 5: Run patch tests and the full suite**

Run `tools/Test-Core.ps1`.

Expected: all operation and rejection tests pass.

- [ ] **Step 6: Commit the patch engine**

```powershell
git add src/VizzyGPT.Core/Patching tests/VizzyGPT.Core.Tests/Patching
git commit -m "feat: add typed Vizzy patch engine"
```

---

### Task 4: Validation, Change Sessions, Backups, and Pending Rebase

**Files:**
- Create: `src/VizzyGPT.Core/Validation/ValidationIssue.cs`
- Create: `src/VizzyGPT.Core/Validation/VizzyProgramValidator.cs`
- Create: `src/VizzyGPT.Core/Changes/ChangeSession.cs`
- Create: `src/VizzyGPT.Core/Changes/PendingChange.cs`
- Create: `src/VizzyGPT.Core/Changes/PendingChangeRebaser.cs`
- Create: `src/VizzyGPT.Core/Storage/IDataStore.cs`
- Create: `src/VizzyGPT.Core/Storage/FileDataStore.cs`
- Create: `tests/VizzyGPT.Core.Tests/Validation/VizzyProgramValidatorTests.cs`
- Create: `tests/VizzyGPT.Core.Tests/Changes/PendingChangeRebaserTests.cs`
- Create: `tests/VizzyGPT.Core.Tests/Storage/FileDataStoreTests.cs`

**Interfaces:**
- Consumes: canonical documents and patch results from Tasks 2-3.
- Produces: `VizzyProgramValidator.Validate(document, catalog) -> ValidationReport`.
- Produces: `ChangeSession` with base XML/hash, patch, result XML/hash, and preview lines.
- Produces: `PendingChangeRebaser.TryRebase(pending, current) -> RebaseResult`.
- Produces: `IDataStore.SaveBackupAsync`, `SavePendingAsync`, `LoadPendingAsync`, and `DeletePendingAsync`.

- [ ] **Step 1: Write failing validation and storage tests**

Cover missing `Variables`, `Instructions`, or `Expressions`; duplicate IDs; invalid styles; unresolved global variable references; malformed constants; unsupported child placement; and successful serializer callback validation.

For pending changes, fingerprint every selected target and every variable or custom-node declaration referenced by an operation. Assert that an unrelated node edit rebases successfully, a target edit returns `Conflict`, and a selector that becomes ambiguous returns `Conflict`.

For storage, use a temporary directory, write 21 backups, and assert only the newest 20 remain. Assert that a failed backup write prevents session apply. Assert pending JSON is written atomically through a temporary file and rename.

- [ ] **Step 2: Run the new tests and verify they fail**

Run:

```powershell
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter "FullyQualifiedName~VizzyProgramValidatorTests|FullyQualifiedName~PendingChangeRebaserTests|FullyQualifiedName~FileDataStoreTests"
```

Expected: FAIL because validation, change, and storage types do not exist.

- [ ] **Step 3: Implement layered validation**

`VizzyProgramValidator` must run pure XML checks first, catalog checks second, and an injected `Func<string, ValidationIssue?>` runtime serializer check last. The report exposes `IsValid`, ordered `Issues`, and separate `Errors` and `Warnings`. Any error disables apply; warnings remain visible in preview.

- [ ] **Step 4: Implement change and rebase models**

`ChangeSession.Create` accepts base document, patch, patch result, and validation report and refuses invalid reports. `PendingChange` persists the program fingerprint, base XML/hash, patch JSON, result XML/hash, target fingerprints, created UTC timestamp, and preview lines.

`PendingChangeRebaser.TryRebase` returns `Unchanged` when hashes match. Otherwise, resolve every recorded selector against the current document and compare its canonical subtree hash. If every fingerprint matches, replace only `BaseHash`, reapply the patch to the current document, and return `Rebased`; otherwise return `Conflict` without a document.

- [ ] **Step 5: Implement atomic storage and retention**

`FileDataStore` accepts a root directory. Use UTF-8 without BOM, `Path.GetInvalidFileNameChars()` replacement for fingerprints, temporary sibling files, `File.Replace` when the target exists, and `File.Move` otherwise. Sort backups by UTC timestamp in their filename and delete oldest files after a successful 21st write.

- [ ] **Step 6: Run the complete core suite**

Run `tools/Test-Core.ps1`.

Expected: all tests pass; the test run leaves no files below `UserData`.

- [ ] **Step 7: Commit validation and persistence**

```powershell
git add src/VizzyGPT.Core/Validation src/VizzyGPT.Core/Changes src/VizzyGPT.Core/Storage tests/VizzyGPT.Core.Tests/Validation tests/VizzyGPT.Core.Tests/Changes tests/VizzyGPT.Core.Tests/Storage
git commit -m "feat: validate and persist Vizzy change sessions"
```

---

### Task 5: OpenAI-Compatible Client, Context Builder, and Secret Redaction

**Files:**
- Create: `src/VizzyGPT.Core/Api/ApiMode.cs`
- Create: `src/VizzyGPT.Core/Api/AiRequest.cs`
- Create: `src/VizzyGPT.Core/Api/AiResponse.cs`
- Create: `src/VizzyGPT.Core/Api/IAiTransport.cs`
- Create: `src/VizzyGPT.Core/Api/OpenAiClient.cs`
- Create: `src/VizzyGPT.Core/Api/ContextBuilder.cs`
- Create: `src/VizzyGPT.Core/Security/SecretRedactor.cs`
- Create: `tests/VizzyGPT.Core.Tests/Api/OpenAiClientTests.cs`
- Create: `tests/VizzyGPT.Core.Tests/Security/SecretRedactorTests.cs`

**Interfaces:**
- Consumes: canonical program summaries and `PatchDocument` JSON schema.
- Produces: `IAiTransport.SendAsync(HttpTransportRequest, CancellationToken)`.
- Produces: `OpenAiClient.SendAsync(AiRequest, CancellationToken) -> AiResponse` with one schema-repair attempt.
- Produces: `ContextBuilder.BuildEditorContext` and `BuildFlightContext`.

- [ ] **Step 1: Write fake-transport tests before implementation**

Create a queue-backed `FakeTransport` in the test file. Cover Responses success, Chat Completions success, Auto fallback only on HTTP `404` or `405` with endpoint-not-supported content, no fallback on `401` or `429`, cancellation, timeout propagation, invalid patch JSON followed by one valid repair, and two invalid responses producing text-only `AiResponse` with `CanApply == false`.

Add URI tests that allow HTTPS and `http://localhost`, `http://127.0.0.1`, or `http://[::1]`, and reject all other HTTP hosts.

- [ ] **Step 2: Run client tests and verify they fail**

Run:

```powershell
dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter "FullyQualifiedName~OpenAiClientTests|FullyQualifiedName~SecretRedactorTests"
```

Expected: FAIL because the API and redaction types do not exist.

- [ ] **Step 3: Implement transport-neutral request and response types**

`HttpTransportRequest` contains `Method`, absolute `Uri`, ordinal headers, UTF-8 JSON body, and timeout. `HttpTransportResponse` contains status code and UTF-8 body. `AiRequest` contains mode, prompt, normalized context, model, base URI, API key, and timeout. `AiResponse` contains assistant text, optional `PatchDocument`, `CanApply`, and diagnostics safe for display.

- [ ] **Step 4: Implement API payloads and one repair attempt**

Responses requests use `POST {base}/v1/responses`, `input`, model, and a strict JSON schema for the patch envelope. Chat Completions requests use `POST {base}/v1/chat/completions`, `messages`, model, and `response_format: { type: "json_schema" }` when supported. Normalize both responses into one envelope:

```json
{
  "message": "Human-readable explanation",
  "patch": {
    "baseHash": "lowercase sha256",
    "summary": "Short change summary",
    "operations": []
  }
}
```

When envelope or patch deserialization fails, send exactly one repair request containing the validation error and invalid model output. Never retry authentication, rate-limit, cancellation, or timeout failures automatically.

- [ ] **Step 5: Implement bounded context and redaction**

`ContextBuilder` includes declarations and the complete canonical program below 64 KiB. Above 64 KiB, include declarations, root event/custom-node summaries, and the subtree selected by ID or path. Flight context includes at most 200 recent log entries and 120 telemetry samples summarized into min/max/latest values.

`SecretRedactor.Redact` replaces the configured API key, `Authorization: Bearer` values, and JSON `api_key` values with `[REDACTED]` before any diagnostic log write.

- [ ] **Step 6: Run all core tests**

Run `tools/Test-Core.ps1`.

Expected: all tests pass, including exactly-one-repair assertions.

- [ ] **Step 7: Commit the API layer**

```powershell
git add src/VizzyGPT.Core/Api src/VizzyGPT.Core/Security tests/VizzyGPT.Core.Tests/Api tests/VizzyGPT.Core.Tests/Security
git commit -m "feat: add OpenAI-compatible Vizzy client"
```

---

### Task 6: Juno Runtime Adapter, Contract Probe, Transport, and DPAPI

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Adapters/IVizzyRuntimeAdapter.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Adapters/RuntimeContractProbe.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Adapters/VizzyRuntimeAdapter.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Api/UnityWebRequestTransport.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Security/DpapiSecretProtector.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Storage/JunoDataPaths.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGPT.EditModeTests.asmdef`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/ProgramSerializerContractTests.cs`
- Create: `tools/Test-Unity.ps1`

**Interfaces:**
- Consumes: `IProgramSerializer.DeserializeFlightProgram(XElement)` and `SerializeFlightProgram(FlightProgram)` from ModApi.
- Produces: `IVizzyRuntimeAdapter.TryGetEditorProgramXml`, `TrySetEditorProgramXml`, `TryGetFlightProgramXml`, `ValidateWithProgramSerializer`, and compatibility status.
- Produces: Unity implementations of `IAiTransport`, data paths, and secret protection.

- [ ] **Step 1: Write the public runtime interface and failing serializer test**

Use this exact interface:

```csharp
public interface IVizzyRuntimeAdapter
{
    bool IsEditorAvailable { get; }
    bool IsFlightAvailable { get; }
    bool TryGetEditorProgramXml(out string xml, out string error);
    bool TrySetEditorProgramXml(string xml, out string error);
    bool TryGetFlightProgramXml(out string xml, out string error);
    ValidationIssue? ValidateWithProgramSerializer(string xml);
}
```

Write an EditMode test that loads `minimal.xml`, calls `ProgramSerializer.DeserializeFlightProgram(XElement.Parse(xml))`, serializes it again with `ProgramSerializer.SerializeFlightProgram(program)`, and asserts the resulting root name is `Program` and the event ID remains `0`.

- [ ] **Step 2: Run Unity EditMode tests and verify the expected failure**

Create `tools/Test-Unity.ps1` to invoke Unity in batch mode with `-runTests -testPlatform EditMode -testResults artifacts/editmode-results.xml`. Run it.

Expected: FAIL until the core DLL is synchronized and the EditMode assembly references `ModApi` and `VizzyGPT.Core`.

- [ ] **Step 3: Implement the contract probe without loading unknown types eagerly**

`RuntimeContractProbe` scans already loaded assemblies for a non-abstract type whose full name contains `Vizzy`, owns a readable `FlightProgram` property or field assignable to `ModApi.Craft.Program.FlightProgram`, and has a Unity object instance discoverable through `Resources.FindObjectsOfTypeAll`. It records the resolved member once, logs only type/member names, and returns an incompatible result if zero or multiple candidates remain after filtering.

Do not call static constructors, do not load assemblies from disk, and do not search private members outside the isolated adapter.

- [ ] **Step 4: Implement serializer-based get, set, and rollback**

`VizzyRuntimeAdapter` uses `ProgramSerializer` for all XML conversion. `TrySetEditorProgramXml` deserializes before touching the editor, records the old `FlightProgram`, assigns the new program through the resolved member, invokes the editor's parameterless rebuild/refresh method selected by the probe, and restores the old program if refresh throws.

Flight program lookup uses `Game.Instance.FlightScene` public ModApi access first and the same read-only probe only if no public program property is available.

- [ ] **Step 5: Implement UnityWebRequest transport and DPAPI**

`UnityWebRequestTransport` sends JSON with `UploadHandlerRaw`, `DownloadHandlerBuffer`, bearer auth, timeout seconds rounded up, and cancellation through `Abort`. It returns all HTTP bodies, including non-success bodies, without logging headers.

`DpapiSecretProtector` uses `ProtectedData.Protect` and `Unprotect` with `DataProtectionScope.CurrentUser`, UTF-8, and fixed entropy string `VizzyGPT/0.1`. `JunoDataPaths.Root` resolves to `Path.Combine(Application.persistentDataPath, "UserData", "VizzyGPT")` only after confirming Juno's actual persistent path; if `Application.persistentDataPath` already ends at `SimpleRockets 2`, append only `UserData/VizzyGPT`.

- [ ] **Step 6: Run Unity and core tests**

Run `tools/Sync-Core.ps1`, `tools/Test-Core.ps1`, and `tools/Test-Unity.ps1`.

Expected: all tests pass; the contract test proves current ModApi serialization works.

- [ ] **Step 7: Commit runtime infrastructure**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Adapters unity/VizzyGPT/Assets/VizzyGPT/Runtime/Api unity/VizzyGPT/Assets/VizzyGPT/Runtime/Security unity/VizzyGPT/Assets/VizzyGPT/Runtime/Storage unity/VizzyGPT/Assets/VizzyGPT/Tests tools/Test-Unity.ps1
git commit -m "feat: connect VizzyGPT to Juno runtime"
```

---

### Task 7: Vizzy Editor Panel, Preview, Settings, and Apply Workflow

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/VizzyGptPanel.xml`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/PreviewDialog.xml`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Ui/SettingsDialog.xml`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/PreviewDialogController.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SettingsDialogController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`

**Interfaces:**
- Consumes: runtime adapter, `OpenAiClient`, `ChangeSession`, `FileDataStore`, and DPAPI from Tasks 4-6.
- Produces: `OpenPanel`, `SendPromptAsync`, `ShowPreview`, `ApplySessionAsync`, `UndoLastAsync`, and settings test connection.

- [ ] **Step 1: Build static XML layouts using stock styles**

`VizzyGptPanel.xml` must include a right-anchored 420 px panel, `Ask` and `Modify` toggles, scrollable transcript, multiline input, send icon, cancel icon, status label, settings icon, and pending indicator. Use existing `Ui/Xml/Styles.xml`; do not embed custom fonts or duplicate stock sprites.

`PreviewDialog.xml` must show summary, separate added/changed/removed sections, warnings, and `Apply`/`Cancel`. `SettingsDialog.xml` must include base URL, API mode, model, masked API key, timeout, destination host, `Test Connection`, `Save`, and `Cancel`.

- [ ] **Step 2: Add controller tests with fake dependencies**

In EditMode tests, construct controllers with fake adapter/client/store. Assert text focus sets `Game.Instance.UserInterface.IgnoreKeyboardInputs`, cancellation aborts the request, Ask mode never enables Apply, Modify mode requires a valid session, stale hashes disable Apply, failed backup disables Apply, successful apply calls the adapter once, and undo restores the saved XML.

- [ ] **Step 3: Implement the panel state machine**

Use explicit states `Closed`, `Idle`, `Sending`, `PreviewReady`, `Applying`, and `Error`. Every transition updates button interactability and status text from one `RenderState()` method. Escape all assistant and error text before assigning it to XML UI text controls.

On Modify send: read current editor XML, build context, call the client, apply the patch to a clone, run core validation plus `ValidateWithProgramSerializer`, and create a `ChangeSession`. Never call `TrySetEditorProgramXml` before preview approval.

- [ ] **Step 4: Implement backup-first apply and immediate undo**

On Apply: re-read the editor XML and compare its hash to the session base hash; save the base XML backup; call adapter set; retain the session for immediate undo; and close the preview only after success. On Undo: validate the backup with `ProgramSerializer`, apply it, and write a backup of the state being undone.

- [ ] **Step 5: Implement settings persistence and connection test**

Persist non-secret JSON through `FileDataStore`; encrypt only the API key through DPAPI. Display the parsed destination host before Save. Test Connection sends a minimal non-program request and reports status without persisting changed fields until Save.

- [ ] **Step 6: Integrate UI lifecycle**

`VizzyGptBehaviour` subscribes to `Game.Instance.UserInterface.UserInterfaceLoading` and `UserInterfaceLoaded`. Add the GPT button only when the ID is the Vizzy UI or flight UI. Store every event subscription and remove it in `OnDestroy`. Destroy opened dialogs and cancel requests during scene changes.

- [ ] **Step 7: Run tests and manually verify the editor workflow**

Run core and Unity tests. In game, open Vizzy, confirm the panel does not shift stock controls, use a fake loopback API to create a preview, cancel once, apply once, use stock Save to Craft, leave Vizzy, reopen it, and verify the program reloads.

- [ ] **Step 8: Commit the editor UI**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs unity/VizzyGPT/Assets/VizzyGPT/Tests
git commit -m "feat: add previewable Vizzy GPT editor workflow"
```

---

### Task 8: Flight Logs, Bounded Telemetry, and Pending Changes

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Flight/FlightContextCollector.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Flight/TelemetrySampler.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/FlightContextCollectorTests.cs`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/PendingChangeFlowTests.cs`

**Interfaces:**
- Consumes: `IFlightScene`, `IFlightSceneUI.FlightLog`, current craft, core `ContextBuilder`, `PendingChange`, and `PendingChangeRebaser`.
- Produces: bounded `FlightContextSnapshot` and editor-return pending prompt.

- [ ] **Step 1: Write failing bounded-context tests**

Feed 250 log messages and 200 telemetry samples. Assert only the newest 200 logs remain and only 120 telemetry samples remain. Assert summaries contain latest/min/max values but no continuously streamed request. Assert disposing the collector unsubscribes every event.

For pending flow, assert creating a second pending change requires explicit replace, matching hash shows the saved preview, unrelated edits rebase, target edits conflict, and no pending path calls `TrySetEditorProgramXml` before Apply.

- [ ] **Step 2: Run Unity tests and verify they fail**

Run `tools/Test-Unity.ps1`.

Expected: FAIL because collector and pending orchestration do not exist.

- [ ] **Step 3: Implement bounded telemetry and log collection**

Sample at 2 Hz only while flight UI is active. Record universal time, altitude, surface speed, vertical speed, Mach, angle of attack, pitch, heading, roll, throttle, active craft ID, and current target name when available through public ModApi properties. Use fixed-capacity queues: 120 telemetry samples and 200 logs.

Subscribe to `LogService.LogAdded` when available. If the active flight log service is not public, use one adapter probe and expose read-only log snapshots; never patch logging methods.

- [ ] **Step 4: Implement flight chat and pending-save behavior**

At flight initialization, capture program XML/hash and program fingerprint. Ask mode sends selected summaries. Modify mode validates the returned patch against the launch snapshot and writes one pending JSON record. If a record exists, show `Replace` and `Cancel`; do not silently overwrite it.

- [ ] **Step 5: Implement return-to-editor pending handling**

When Vizzy opens, compute the current program fingerprint and load its pending record. Use `PendingChangeRebaser`. Show the saved preview for `Unchanged`, a newly validated preview for `Rebased`, and a conflict dialog offering `Regenerate` or `Discard` for `Conflict`. Delete pending data only after a successful apply or explicit discard.

- [ ] **Step 6: Run automated and manual flight checks**

Run core and Unity tests. Launch a craft, collect at least 10 seconds of telemetry, ask for analysis, prepare a patch, exit without applying, open matching Vizzy, and verify preview/application. Repeat after manually changing a targeted node and verify regeneration is required.

- [ ] **Step 7: Commit flight support**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Flight unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs unity/VizzyGPT/Assets/VizzyGPT/Tests
git commit -m "feat: prepare Vizzy changes during flight"
```

---

### Task 9: Packaging, Compatibility Failure Mode, and Acceptance Verification

**Files:**
- Create: `docs/manual-test-checklist.md`
- Modify: SR2 Mod Builder-managed version and description assets through the Unity window.
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`

**Interfaces:**
- Consumes: every prior task.
- Produces: release candidate `VizzyGPT.sr2-mod`, clean logs, and a completed acceptance record.

- [ ] **Step 1: Add compatibility-disable integration coverage**

Inject probe results `Compatible`, `EditorUnavailable`, and `AmbiguousContract`. Assert `Compatible` enables Modify, while the other results keep Ask available, hide Apply, and display the exact diagnostic type/member summary without stack traces or secrets.

- [ ] **Step 2: Run all automated verification from a clean checkout**

Run:

```powershell
git clean -ndx
powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1
powershell -ExecutionPolicy Bypass -File tools/Sync-Core.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-Unity.ps1
```

Expected: the dry-run lists only generated build/cache artifacts; all core and EditMode tests pass.

- [ ] **Step 3: Build and install the release candidate**

In the SR2 Mod Builder window set version `0.1.0`, choose `Save Mod (Windows/MacOS)`, and save to:

```text
C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod
```

Enable the mod and restart Juno. Expected: `ModLoadLog.txt` reports VizzyGPT `0.1.0` loaded with no errors.

- [ ] **Step 4: Execute the acceptance matrix**

Create `docs/manual-test-checklist.md` with one checkbox for every acceptance criterion from the design. Include exact test programs, prompt text, expected preview lines, save/reload result, flight pending result, stale conflict result, network/auth/rate-limit result, backup/undo result, disabled-mod result, and paths to relevant sanitized log excerpts.

Complete every checkbox on the current `1.4.0.8c open_beta` installation. Do not mark a criterion complete from automated tests alone when the criterion is user-interface or game-lifecycle behavior.

- [ ] **Step 5: Inspect final game logs and artifact**

Run:

```powershell
Select-String -Path '..\ModLoadLog.txt','..\Player.log' -Pattern 'VizzyGPT|Exception|Error' -Context 1,2
Get-FileHash '..\Mods\VizzyGPT.sr2-mod' -Algorithm SHA256
```

Expected: one successful load line, no VizzyGPT exceptions/errors, and a non-empty SHA-256 hash.

- [ ] **Step 6: Commit release verification**

```powershell
git add docs/manual-test-checklist.md unity/VizzyGPT
git commit -m "test: verify VizzyGPT release candidate"
```

- [ ] **Step 7: Tag the verified first release**

```powershell
git tag -a v0.1.0 -m "VizzyGPT v0.1.0"
git status --short
```

Expected: clean status and tag `v0.1.0` pointing to the verified release commit.
