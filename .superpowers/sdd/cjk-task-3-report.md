# Bundled CJK Font Task 3 Report

## Scope

- `Assets.Scripts.Mod.OnModInitialized` injects the mod resource loader through
  `path => global::Assets.Scripts.Mod.Instance.ResourceLoader.LoadAsset<Font>(path)`.
- `VizzyGptMod.EnsureInitialized(Func<string, Font?>)` now owns only the
  delegate-based bootstrap path. It creates the root, adds the behaviour, then
  calls `Initialize(loadFont)`.
- `VizzyGptBehaviour.Awake` is intentionally empty. Its explicit, idempotent
  `Initialize(Func<string, Font?>)` creates the `BundledCjkFontProvider`
  first, then performs all UI, storage, API, settings, and event-subscription
  setup that previously ran in `Awake`.
- The existing panel and preview binding scope remains unchanged. The bundled
  provider is resolved for panel mounting, reused for previews, and disposed in
  `OnDestroy`.
- Deleted `SystemCjkFontProvider`, its EditMode tests, and both `.meta` files
  after all Runtime references had moved to the bundled provider.

## TDD Evidence

### RED

Added lifecycle/source-contract coverage in:

- `VizzyGptBootstrapTests.cs`: mod delegate contract, Runtime loader forwarding,
  and injected bootstrap ownership.
- `VizzyGptUiLifecycleTests.cs`: object-level `GameObject`/`AddComponent`
  lifecycle coverage proving no runtime setup precedes injection, provider
  ownership after `EnsureInitialized`, provider identity across duplicate
  initialization, and provider disposal on destruction.

The first executable RED result was:

```text
Unity EditMode: 77 passed, 5 failed, 82 total.
```

The five failures were the expected missing explicit `Initialize` chain,
missing `initialized` lifecycle state, `Awake`-before-injection setup, and
the cleanup assertion that the review refactor corrects.

### GREEN

```text
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
Unity EditMode tests passed: 82/82
```

The full suite passed after the lifecycle refactor and the focused EditMode
harness corrections. The object-level tests now cover initialization ordering,
provider identity across duplicate `EnsureInitialized` calls, and `OnDestroy`
provider disposal; source assertions remain limited to the generated mod
ResourceLoader boundary.

## Static Checks

- `git diff --check`: no whitespace errors. Git reported only existing CRLF
  conversion notices.
- Runtime source search found no `Assets.Scripts`, `Assembly-CSharp`, or
  `SystemCjkFontProvider` references.
- Confirmed all four deleted legacy provider/test source and `.meta` files are
  absent.
- The pre-existing CRLF-only changes in `Packages/manifest.json` and
  `Packages/packages-lock.json` remain unstaged and are excluded from the Task
  3 commit.
