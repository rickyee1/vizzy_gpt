# Bundled CJK Font Task 2 Report

## Scope

- Added `BundledCjkFontProvider` and `UnityBundledCjkFontBackend`.
- Added provider behavior tests for one-time loading/caching, one-time failure
  warning, and disposal that releases only the runtime TMP asset.
- Retained `SystemCjkFontProvider` and its EditMode tests temporarily so the
  untouched lifecycle code continues to compile. Task 3 will remove them after
  it changes the lifecycle dependency.
- Did not modify `Mod.cs`, `VizzyGptMod.cs`, or `VizzyGptBehaviour.cs`.

## RED

Focused Unity EditMode invocation for
`VizzyGPT.Tests.EditMode.BundledCjkFontProviderTests` failed before production
code was added. Unity did not produce a result XML because compilation failed.

```text
Assets\\VizzyGPT\\Tests\\EditMode\\BundledCjkFontProviderTests.cs(136,58):
error CS0246: The type or namespace name 'IBundledCjkFontBackend' could not be
found (are you missing a using directive or an assembly reference?)
```

This is the expected RED boundary: the behavior tests referenced the absent
provider backend interface.

Test count: 0/7 executed because Unity stopped at the expected compilation
error before it could run the 3 new behavior tests and 4 resource tests.

## GREEN

Focused Unity EditMode verification passed after restoring the legacy provider
files. Their deletion is intentionally deferred to Task 3, when
`VizzyGptBehaviour` switches to the bundled provider.

```text
VizzyGPT.Tests.EditMode.BundledCjkFontProviderTests: 7/7 passed, 0 failed.
```
