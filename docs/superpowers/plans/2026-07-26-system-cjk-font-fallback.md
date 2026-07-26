# System CJK Font Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render Chinese prompt, response, status, and preview text with an installed system font while leaving VizzyGPT's static interface unchanged.

**Architecture:** Add a testable provider that chooses and caches one installed CJK-capable dynamic TextMesh Pro font asset. Add a focused applicator for dynamic TMP components, then pass the shared asset from `VizzyGptBehaviour` into the panel and preview controllers.

**Tech Stack:** Unity 2022.3.62f1, TextMesh Pro 3.0.6, C# nullable reference types, NUnit EditMode tests, Juno ModTools.

## Global Constraints

- Do not bundle or redistribute a font file.
- Apply the font only to prompt input, transcript, status, and preview dynamic text.
- Keep static English labels and buttons on the game's existing font.
- Font discovery failure must not prevent the panel from opening.
- Log at most one non-secret warning when no supported system font is found.
- Reuse one resolved TMP font asset for the lifetime of the mod.

---

### Task 1: Resolve And Cache A System CJK Font

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SystemCjkFontProvider.cs`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/SystemCjkFontProviderTests.cs`

**Interfaces:**
- Consumes: Unity `Font.GetOSInstalledFontNames`, `Font.CreateDynamicFontFromOSFont`, and `TMP_FontAsset.CreateFontAsset`.
- Produces: `ISystemCjkFontBackend`, `SystemCjkFontProvider.Resolve()`, and `SystemCjkFontProvider.Dispose()`.

- [ ] **Step 1: Write failing provider tests**

Create fake backend coverage that proves ordered selection, glyph rejection,
cached reuse, single-warning no-font behavior, and release on disposal:

```csharp
[Test]
public void Resolve_skips_missing_and_non_cjk_fonts_then_caches_first_supported_candidate()
{
    var backend = new FakeSystemCjkFontBackend(
        new[] { "DengXian", "SimSun" },
        supportedFamilies: new[] { "SimSun" });
    var warnings = new List<string>();
    var provider = new SystemCjkFontProvider(backend, warnings.Add);

    var first = provider.Resolve();
    var second = provider.Resolve();

    Assert.That(first, Is.SameAs(backend.Assets["SimSun"]));
    Assert.That(second, Is.SameAs(first));
    Assert.That(backend.CreatedFamilies, Is.EqualTo(new[] { "DengXian", "SimSun" }));
    Assert.That(warnings, Is.Empty);
}

[Test]
public void Resolve_without_supported_font_returns_null_and_warns_only_once()
{
    var backend = new FakeSystemCjkFontBackend(new[] { "Arial" }, Array.Empty<string>());
    var warnings = new List<string>();
    var provider = new SystemCjkFontProvider(backend, warnings.Add);

    Assert.That(provider.Resolve(), Is.Null);
    Assert.That(provider.Resolve(), Is.Null);
    Assert.That(warnings, Has.Count.EqualTo(1));
    StringAssert.Contains("CJK system font", warnings[0]);
}

[Test]
public void Dispose_releases_every_created_candidate_once()
{
    var backend = new FakeSystemCjkFontBackend(
        new[] { "DengXian", "SimSun" },
        supportedFamilies: new[] { "SimSun" });
    var provider = new SystemCjkFontProvider(backend, _ => { });

    provider.Resolve();
    provider.Dispose();
    provider.Dispose();

    Assert.That(backend.ReleasedAssets, Is.EquivalentTo(backend.Assets.Values));
    Assert.That(backend.ReleasedAssets, Has.Count.EqualTo(2));
}
```

The fake backend creates `TMP_FontAsset` instances with
`ScriptableObject.CreateInstance<TMP_FontAsset>()` and records calls without
consulting the host operating system.

- [ ] **Step 2: Run the focused EditMode tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: compilation fails because `SystemCjkFontProvider` and
`ISystemCjkFontBackend` do not exist.

- [ ] **Step 3: Implement the provider and Unity backend**

Implement these public contracts:

```csharp
public interface ISystemCjkFontBackend
{
    IReadOnlyList<string> GetInstalledFontNames();
    TMP_FontAsset? CreateDynamicFontAsset(string familyName);
    bool SupportsCharacters(TMP_FontAsset asset, string characters);
    void Release(TMP_FontAsset asset);
}

public sealed class SystemCjkFontProvider : IDisposable
{
    public const string ProbeCharacters = "中文";

    public static readonly IReadOnlyList<string> PreferredFamilies = new[]
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "DengXian",
        "SimSun",
        "NSimSun",
        "SimHei",
        "KaiTi",
        "FangSong",
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "PingFang SC",
        "Heiti SC",
        "WenQuanYi Micro Hei"
    };

    public TMP_FontAsset? Resolve();
    public void Dispose();
    public static SystemCjkFontProvider CreateDefault();
}
```

`Resolve()` must:

1. Cache both success and failure.
2. Compare installed family names case-insensitively.
3. Create candidates only in `PreferredFamilies` order.
4. Release rejected candidates during provider disposal.
5. Call the injected warning delegate once when no candidate passes.

`UnitySystemCjkFontBackend.CreateDynamicFontAsset` must create a 32-point
dynamic OS font and a dynamic, multi-atlas TMP asset:

```csharp
var source = Font.CreateDynamicFontFromOSFont(familyName, 32);
return TMP_FontAsset.CreateFontAsset(
    source,
    32,
    4,
    GlyphRenderMode.SDFAA,
    1024,
    1024,
    AtlasPopulationMode.Dynamic,
    true);
```

`SupportsCharacters` uses `TryAddCharacters(characters, out var missing)` and
requires an empty `missing` result. `Release` destroys the TMP asset and its
`sourceFontFile` using `UnityEngine.Object.Destroy`.

- [ ] **Step 4: Run EditMode tests and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: all EditMode tests pass with no compilation errors.

- [ ] **Step 5: Commit the provider**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SystemCjkFontProvider.cs unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/SystemCjkFontProviderTests.cs
git commit -m "feat: resolve installed CJK fonts"
```

---

### Task 2: Apply The Font Only To Dynamic Text

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/CjkTextFontApplicator.cs`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/CjkTextFontApplicatorTests.cs`

**Interfaces:**
- Consumes: a nullable `TMP_FontAsset`, `TMP_InputField`, and dynamic `TMP_Text` instances.
- Produces: `CjkTextFontApplicator.ApplyToInput` and `CjkTextFontApplicator.ApplyToText`.

- [ ] **Step 1: Write failing applicator tests**

```csharp
[Test]
public void ApplyToInput_updates_visible_text_and_text_placeholder()
{
    var root = new GameObject("input");
    var input = root.AddComponent<TMP_InputField>();
    var text = new GameObject("text").AddComponent<TextMeshProUGUI>();
    var placeholder = new GameObject("placeholder").AddComponent<TextMeshProUGUI>();
    text.transform.SetParent(root.transform);
    placeholder.transform.SetParent(root.transform);
    input.textComponent = text;
    input.placeholder = placeholder;
    var font = ScriptableObject.CreateInstance<TMP_FontAsset>();

    CjkTextFontApplicator.ApplyToInput(input, font);

    Assert.That(text.font, Is.SameAs(font));
    Assert.That(placeholder.font, Is.SameAs(font));
}

[Test]
public void ApplyToText_updates_dynamic_targets_but_not_unlisted_static_label()
{
    var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
    var transcript = new GameObject("transcript").AddComponent<TextMeshProUGUI>();
    var status = new GameObject("status").AddComponent<TextMeshProUGUI>();
    var staticLabel = new GameObject("static").AddComponent<TextMeshProUGUI>();
    var original = staticLabel.font;

    CjkTextFontApplicator.ApplyToText(font, transcript, status);

    Assert.That(transcript.font, Is.SameAs(font));
    Assert.That(status.font, Is.SameAs(font));
    Assert.That(staticLabel.font, Is.SameAs(original));
}

[Test]
public void Null_font_is_a_safe_no_op()
{
    var text = new GameObject("text").AddComponent<TextMeshProUGUI>();
    var original = text.font;

    CjkTextFontApplicator.ApplyToText(null, text);

    Assert.That(text.font, Is.SameAs(original));
}
```

Destroy all created Unity objects in `finally` blocks.

- [ ] **Step 2: Run EditMode tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: compilation fails because `CjkTextFontApplicator` does not exist.

- [ ] **Step 3: Implement the minimal applicator**

```csharp
public static class CjkTextFontApplicator
{
    public static void ApplyToInput(TMP_InputField? input, TMP_FontAsset? font)
    {
        if (input == null || font == null)
        {
            return;
        }

        if (input.textComponent != null)
        {
            input.textComponent.font = font;
        }

        if (input.placeholder is TMP_Text placeholder)
        {
            placeholder.font = font;
        }
    }

    public static void ApplyToText(TMP_FontAsset? font, params TMP_Text?[] targets)
    {
        if (font == null)
        {
            return;
        }

        foreach (var target in targets)
        {
            if (target != null)
            {
                target.font = font;
            }
        }
    }
}
```

- [ ] **Step 4: Run EditMode tests and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: all EditMode tests pass.

- [ ] **Step 5: Commit the applicator**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/CjkTextFontApplicator.cs unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/CjkTextFontApplicatorTests.cs
git commit -m "feat: apply CJK font to dynamic text"
```

---

### Task 3: Integrate, Build, And Verify Chinese Rendering

**Files:**
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/PreviewDialogController.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptUiLifecycleTests.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/PreviewDialogControllerTests.cs`
- Modify: `docs/manual-test-checklist.md`

**Interfaces:**
- Consumes: `SystemCjkFontProvider.Resolve()` and `CjkTextFontApplicator`.
- Produces: panel and preview `Bind` overloads that accept a nullable `TMP_FontAsset`.

- [ ] **Step 1: Write failing integration tests**

Add controller-level tests that bind synthetic layouts and assert:

```csharp
panel.Bind(layout, cjkFont);
Assert.That(promptInput.textComponent.font, Is.SameAs(cjkFont));
Assert.That(transcript.font, Is.SameAs(cjkFont));
Assert.That(status.font, Is.SameAs(cjkFont));
Assert.That(askButtonLabel.font, Is.Not.SameAs(cjkFont));

preview.Bind(layout, model, cjkFont);
Assert.That(summary.font, Is.SameAs(cjkFont));
Assert.That(added.font, Is.SameAs(cjkFont));
Assert.That(changed.font, Is.SameAs(cjkFont));
Assert.That(removed.font, Is.SameAs(cjkFont));
Assert.That(warnings.font, Is.SameAs(cjkFont));
Assert.That(applyButtonLabel.font, Is.Not.SameAs(cjkFont));
```

Add a test-local `FakeXmlLayout : IXmlLayout` backed by a
`Dictionary<string, Component>`. Its generic `GetElementById<T>` returns the
mapped component with `as T`; its non-generic overload returns `null`; its
`GameObject` property returns the test root; `Xml` is an auto-property; and its
remaining layout, visibility, and rebuild members are no-ops or return `null`.
Include every ID required by each controller. Do not select or alter button
label components in production code.

- [ ] **Step 2: Run EditMode tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

Expected: tests fail because the `Bind` methods do not accept a font asset and
the required components retain their original fonts.

- [ ] **Step 3: Integrate the shared provider**

In `VizzyGptBehaviour`:

```csharp
private SystemCjkFontProvider? cjkFontProvider;

private void Awake()
{
    cjkFontProvider = SystemCjkFontProvider.CreateDefault();
    // Existing initialization follows.
}
```

Resolve once while configuring the panel and pass the same result to previews:

```csharp
var cjkFont = cjkFontProvider?.Resolve();
panel.Bind(layoutController.XmlLayout, cjkFont);

dialog.Bind(layoutController.XmlLayout, model, cjkFontProvider?.Resolve());
```

Dispose and clear the provider in `OnDestroy`.

Change panel binding to:

```csharp
public void Bind(IXmlLayout layout, TMP_FontAsset? cjkFont = null)
{
    // Existing required-element binding.
    CjkTextFontApplicator.ApplyToInput(promptInput, cjkFont);
    CjkTextFontApplicator.ApplyToText(cjkFont, transcriptText, statusText);
    // Existing listener and render setup.
}
```

Change preview binding to:

```csharp
public void Bind(IXmlLayout layout, PreviewDialogModel model, TMP_FontAsset? cjkFont = null)
{
    // Existing required-element binding.
    CjkTextFontApplicator.ApplyToText(
        cjkFont,
        summaryText,
        addedText,
        changedText,
        removedText,
        warningsText);
    Render(model);
}
```

- [ ] **Step 4: Run automated verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
git diff --check
```

Expected: all EditMode and core tests pass; `git diff --check` produces no
errors.

- [ ] **Step 5: Build and install the Windows mod**

Close Juno so the installed package is not locked. Build through the existing
Unity ModTools release path used by the current branch, install the resulting
package at:

```text
C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod
```

Record the file size, timestamp, and SHA-256 after installation.

- [ ] **Step 6: Perform live acceptance**

Launch Juno and verify:

1. Type `你好，请介绍当前 Vizzy 程序` in the prompt; every character is visible.
2. Send the request and receive a Chinese answer; transcript text is visible.
3. Request a Modify patch with a Chinese summary and open Preview; summary and
   warning content are visible.
4. Close and reopen the panel; no duplicate font warning appears in
   `Player.log`.
5. Confirm English labels and buttons retain their existing appearance.

Add these checks and their result to `docs/manual-test-checklist.md`.

- [ ] **Step 7: Commit the integration**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/PreviewDialogController.cs unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptUiLifecycleTests.cs unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/PreviewDialogControllerTests.cs docs/manual-test-checklist.md
git commit -m "feat: render Chinese VizzyGPT text"
```
