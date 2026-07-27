# Bundled CJK Font Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Package an OFL-licensed Simplified Chinese font with VizzyGPT and use it for arbitrary Chinese prompt, response, status, and preview text in Juno.

**Architecture:** Replace the unreliable operating-system font discovery service with a provider that loads one packaged `UnityEngine.Font` from `Resources` and creates a cached dynamic, multi-atlas TextMesh Pro font asset. Keep the existing `CjkTextFontApplicator` and controller bindings so only dynamic user/model text changes font.

**Tech Stack:** Unity 2022.3.62f1c1, TextMesh Pro 3.0.6, NUnit EditMode tests, Juno ModTools, Noto Sans CJK SC 2.004 under SIL OFL 1.1.

## Global Constraints

- Bundle `NotoSansCJKsc-Regular.otf` from the official `notofonts/noto-cjk` `Sans2.004` tag.
- Bundle the unmodified SIL Open Font License 1.1 text with the font.
- Do not redistribute a Windows system font.
- Use a dynamic, multi-atlas TMP asset so model responses are not limited to a predefined character subset.
- Apply the font only to prompt input/placeholder, transcript, status, and preview dynamic text.
- Keep static English titles, labels, toggles, and buttons on the game's existing font.
- Cache one TMP asset for the behavior lifetime and release only the runtime-created TMP asset.
- Missing resources or TMP creation failures must leave the panel usable and emit at most one non-secret warning.

---

## File Structure

- Create `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/NotoSansCJKsc-Regular.otf`: packaged source font.
- Create `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/OFL.txt`: license distributed in the mod.
- Create Unity `.meta` files for the font, license, and resource folders through Unity import.
- Create `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/BundledCjkFontProvider.cs`: bundled resource loading, TMP creation, caching, warning, and disposal.
- Create `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/BundledCjkFontProviderTests.cs`: provider unit and real-resource integration tests.
- Delete `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SystemCjkFontProvider.cs` and its `.meta`.
- Delete `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/SystemCjkFontProviderTests.cs` and its `.meta`.
- Modify `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`: own the bundled provider instead of the system provider.
- Modify `docs/manual-test-checklist.md`: replace deferred system-font checks with packaged-font acceptance evidence.

---

### Task 1: Import The Licensed Font Resource

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/NotoSansCJKsc-Regular.otf`
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/OFL.txt`
- Test: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/BundledCjkFontProviderTests.cs`

**Interfaces:**
- Produces resource key `Fonts/VizzyGPT/NotoSansCJKsc-Regular` as `UnityEngine.Font`.
- Produces resource key `Fonts/VizzyGPT/OFL` as `UnityEngine.TextAsset`.

- [ ] **Step 1: Write failing resource contract tests**

Create the test file with:

```csharp
#nullable enable

using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class BundledCjkFontProviderTests
    {
        [Test]
        public void Packaged_font_resource_can_create_chinese_glyphs()
        {
            var source = Resources.Load<Font>(
                "Fonts/VizzyGPT/NotoSansCJKsc-Regular");

            Assert.That(source, Is.Not.Null);
            var asset = TMP_FontAsset.CreateFontAsset(
                source,
                32,
                4,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            try
            {
                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.TryAddCharacters("中文", out var missing), Is.True);
                Assert.That(missing, Is.Empty);
            }
            finally
            {
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
            }
        }

        [Test]
        public void Packaged_font_includes_the_OFL_license()
        {
            var license = Resources.Load<TextAsset>("Fonts/VizzyGPT/OFL");

            Assert.That(license, Is.Not.Null);
            Assert.That(license.text, Does.Contain("SIL OPEN FONT LICENSE Version 1.1"));
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run Unity EditMode with filter:

```powershell
& 'D:\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe' `
  -batchmode -nographics `
  -projectPath "$PWD\unity\VizzyGPT" `
  -runTests -testPlatform EditMode `
  -testFilter 'VizzyGPT.Tests.EditMode.BundledCjkFontProviderTests' `
  -testResults "$PWD\artifacts\bundled-font-red.xml" `
  -logFile "$PWD\artifacts\bundled-font-red.log"
```

Expected: both tests fail because both resources are missing.

- [ ] **Step 3: Download the pinned font and license**

Create the resource directory, then download from the official repository:

```powershell
$fontDir = 'unity\VizzyGPT\Assets\VizzyGPT\Runtime\Resources\Fonts\VizzyGPT'
New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
Invoke-WebRequest `
  -Uri 'https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf' `
  -OutFile (Join-Path $fontDir 'NotoSansCJKsc-Regular.otf')
Invoke-WebRequest `
  -Uri 'https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/LICENSE' `
  -OutFile (Join-Path $fontDir 'OFL.txt')
```

Record the SHA-256 and size in the implementation report:

```powershell
Get-Item "$fontDir\NotoSansCJKsc-Regular.otf" | Select-Object Length
Get-FileHash "$fontDir\NotoSansCJKsc-Regular.otf" -Algorithm SHA256
```

- [ ] **Step 4: Let Unity import the resources**

Open the project once in batch mode:

```powershell
& 'D:\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath "$PWD\unity\VizzyGPT" `
  -logFile "$PWD\artifacts\bundled-font-import.log"
```

Verify that Unity generated `.meta` files and that
`NotoSansCJKsc-Regular.otf.meta` contains `includeFontData: 1`.

- [ ] **Step 5: Run the resource tests and verify GREEN**

Repeat the filtered EditMode test. Expected: `2/2` passed and no
`Unable to load font face` line in the log.

- [ ] **Step 6: Commit**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/Resources/Fonts `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/BundledCjkFontProviderTests.cs*
git commit -m "feat: bundle licensed Chinese font"
```

---

### Task 2: Replace System Discovery With Bundled Resolution

**Files:**
- Create: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/BundledCjkFontProvider.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/BundledCjkFontProviderTests.cs`
- Delete: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/SystemCjkFontProvider.cs`
- Delete: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/SystemCjkFontProviderTests.cs`

**Interfaces:**
- Produces `IBundledCjkFontBackend.LoadSourceFont(string): Font?`.
- Produces `IBundledCjkFontBackend.CreateDynamicFontAsset(Font): TMP_FontAsset?`.
- Produces `IBundledCjkFontBackend.Release(TMP_FontAsset): void`.
- Produces `BundledCjkFontProvider.Resolve(): TMP_FontAsset?`.

- [ ] **Step 1: Add failing provider behavior tests**

Add tests using a fake backend:

```csharp
[Test]
public void Resolve_loads_packaged_font_once_and_caches_the_TMP_asset()
{
    var source = new Font();
    var expected = ScriptableObject.CreateInstance<TMP_FontAsset>();
    var backend = new FakeBundledCjkFontBackend(source, expected);
    var provider = new BundledCjkFontProvider(backend, _ => { });

    Assert.That(provider.Resolve(), Is.SameAs(expected));
    Assert.That(provider.Resolve(), Is.SameAs(expected));
    Assert.That(backend.LoadCount, Is.EqualTo(1));
    Assert.That(backend.CreateCount, Is.EqualTo(1));
}

[Test]
public void Resolve_without_the_packaged_font_warns_only_once()
{
    var warnings = new List<string>();
    var backend = new FakeBundledCjkFontBackend(null, null);
    var provider = new BundledCjkFontProvider(backend, warnings.Add);

    Assert.That(provider.Resolve(), Is.Null);
    Assert.That(provider.Resolve(), Is.Null);
    Assert.That(warnings, Has.Count.EqualTo(1));
    StringAssert.Contains("bundled CJK font", warnings[0]);
}

[Test]
public void Dispose_releases_only_the_created_TMP_asset()
{
    var source = new Font();
    var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
    var backend = new FakeBundledCjkFontBackend(source, asset);
    var provider = new BundledCjkFontProvider(backend, _ => { });
    provider.Resolve();

    provider.Dispose();
    provider.Dispose();

    Assert.That(backend.ReleasedAssets, Is.EqualTo(new[] { asset }));
}
```

- [ ] **Step 2: Run the provider tests and verify RED**

Expected: compile failure because `BundledCjkFontProvider` and its backend do
not exist.

- [ ] **Step 3: Implement the bundled provider**

Create:

```csharp
#nullable enable

using System;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VizzyGPT.Runtime.Ui
{
    public interface IBundledCjkFontBackend
    {
        Font? LoadSourceFont(string resourcePath);
        TMP_FontAsset? CreateDynamicFontAsset(Font source);
        void Release(TMP_FontAsset asset);
    }

    public sealed class BundledCjkFontProvider : IDisposable
    {
        public const string ResourcePath =
            "Fonts/VizzyGPT/NotoSansCJKsc-Regular";

        private readonly IBundledCjkFontBackend backend;
        private readonly Action<string> logWarning;
        private TMP_FontAsset? resolvedAsset;
        private bool attempted;
        private bool disposed;

        public BundledCjkFontProvider(
            IBundledCjkFontBackend backend,
            Action<string> logWarning)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        }

        public static BundledCjkFontProvider CreateDefault()
        {
            return new BundledCjkFontProvider(
                new UnityBundledCjkFontBackend(),
                message => Debug.LogWarning(message));
        }

        public TMP_FontAsset? Resolve()
        {
            if (disposed || attempted)
            {
                return resolvedAsset;
            }

            attempted = true;
            try
            {
                var source = backend.LoadSourceFont(ResourcePath);
                if (source != null)
                {
                    resolvedAsset = backend.CreateDynamicFontAsset(source);
                }
            }
            catch
            {
                resolvedAsset = null;
            }

            if (resolvedAsset == null)
            {
                try
                {
                    logWarning(
                        "VizzyGPT could not load its bundled CJK font. Chinese text may appear as missing glyphs.");
                }
                catch
                {
                }
            }

            return resolvedAsset;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (resolvedAsset != null)
            {
                backend.Release(resolvedAsset);
                resolvedAsset = null;
            }
        }
    }

    public sealed class UnityBundledCjkFontBackend : IBundledCjkFontBackend
    {
        public Font? LoadSourceFont(string resourcePath)
        {
            return Resources.Load<Font>(resourcePath);
        }

        public TMP_FontAsset? CreateDynamicFontAsset(Font source)
        {
            var asset = TMP_FontAsset.CreateFontAsset(
                source,
                32,
                4,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (asset != null)
            {
                asset.name = "VizzyGPT Bundled CJK";
                asset.isMultiAtlasTexturesEnabled = true;
            }

            return asset;
        }

        public void Release(TMP_FontAsset asset)
        {
            UnityEngine.Object.Destroy(asset);
        }
    }
}
```

Delete the system provider and its tests. Keep
`CjkTextFontApplicator.cs` unchanged.

- [ ] **Step 4: Run provider tests and verify GREEN**

Expected: all bundled provider tests pass, including resource integration.

- [ ] **Step 5: Commit**

```powershell
git add -A unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode
git commit -m "fix: load Chinese font from mod resources"
```

---

### Task 3: Wire The Bundled Provider Into The Mod Lifecycle

**Files:**
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`
- Modify: `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptUiLifecycleTests.cs`

**Interfaces:**
- Consumes `BundledCjkFontProvider.CreateDefault()`.
- Preserves existing `panel.Bind(IXmlLayout, TMP_FontAsset?)`.
- Preserves existing `dialog.Bind(IXmlLayout, PreviewDialogModel, TMP_FontAsset?)`.

- [ ] **Step 1: Update lifecycle test expectations first**

Add a source contract assertion that the behavior creates the bundled provider
and no longer references the system provider:

```csharp
[Test]
public void Behaviour_owns_the_bundled_CJK_provider()
{
    var source = File.ReadAllText(
        Path.Combine(
            Application.dataPath,
            "VizzyGPT/Runtime/VizzyGptBehaviour.cs"));

    Assert.That(source, Does.Contain("BundledCjkFontProvider.CreateDefault()"));
    Assert.That(source, Does.Not.Contain("SystemCjkFontProvider"));
}
```

- [ ] **Step 2: Run the test and verify RED**

Expected: failure because the behavior still names `SystemCjkFontProvider`.

- [ ] **Step 3: Replace lifecycle ownership**

Change the provider field and construction:

```csharp
private BundledCjkFontProvider? cjkFontProvider;

private void Awake()
{
    cjkFontProvider = BundledCjkFontProvider.CreateDefault();
    // Existing initialization remains unchanged.
}
```

Keep the existing `Resolve`, panel bind, preview bind, and `Dispose` calls.

- [ ] **Step 4: Run lifecycle and UI tests**

Run the filtered bundled-provider, lifecycle, panel, and preview test fixtures.
Expected: all pass and static-label assertions remain green.

- [ ] **Step 5: Commit**

```powershell
git add unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs `
  unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptUiLifecycleTests.cs
git commit -m "fix: use bundled font in VizzyGPT lifecycle"
```

---

### Task 4: Verify, Package, Install, And Accept In Juno

**Files:**
- Modify: `docs/manual-test-checklist.md`
- Produce: `artifacts/VizzyGPT.sr2-mod`
- Install: `C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod`

**Interfaces:**
- Produces the final community-installable `.sr2-mod`.

- [ ] **Step 1: Run complete automated verification**

Run the full Unity EditMode suite and parse `artifacts/editmode-results.xml`.
Expected: all tests pass.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
```

Expected: all core tests pass. If Windows blocks `testhost`, record that
environmental failure separately; it does not replace the required Unity suite.

- [ ] **Step 2: Review the final source range**

Review from commit `285b01f` to `HEAD`, checking font licensing, package resource
inclusion, disposal, static UI scope, and no remaining system-font fallback.
Fix every Critical or Important finding before packaging.

- [ ] **Step 3: Build the release package**

Use the existing temporary `VizzyGptBatchModBuilder` pattern to invoke ModTools
`BuildAssetBundles` for `StandaloneWindows64` with `debugBuild: false`. Delete
the temporary Editor builder and generated helper metadata after the package is
written.

Verify:

```powershell
Get-Item artifacts\VizzyGPT.sr2-mod | Select-Object Length, LastWriteTime
Get-FileHash artifacts\VizzyGPT.sr2-mod -Algorithm SHA256
```

Expected: package size is materially larger than the previous 275,819-byte
system-font package and the build log contains the font resource.

- [ ] **Step 4: Install with Juno closed**

Confirm `SimpleRockets2.exe` is not running, then copy the package:

```powershell
Copy-Item `
  -LiteralPath artifacts\VizzyGPT.sr2-mod `
  -Destination 'C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod' `
  -Force
```

Verify the installed SHA-256 equals the artifact SHA-256.

- [ ] **Step 5: Complete live acceptance**

Open Juno and VizzyGPT, then:

1. Type `你好，请解释当前 Vizzy 程序。` and confirm every character is visible.
2. Send a request that returns Chinese and confirm the transcript has no boxes.
3. Generate a Modify response and confirm Chinese preview fields are visible.
4. Close and reopen the panel and confirm rendering remains correct.
5. Inspect `Player.log` and confirm there is no
   `could not load its bundled CJK font` or `Unable to load font face` message
   associated with `NotoSansCJKsc-Regular`.

- [ ] **Step 6: Record evidence and commit**

Update `docs/manual-test-checklist.md` with the Unity/core counts, artifact size,
SHA-256, installed path, Juno version, and live Chinese acceptance results.

```powershell
git add docs/manual-test-checklist.md
git commit -m "docs: record bundled Chinese font acceptance"
```
