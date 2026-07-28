# CJK Live Glyph Validation Report

Date: 2026-07-28

## Scope

This change tests the TMP dynamic-atlas hypothesis only. It does not change
`CjkTextFontApplicator` or any UI application path.

`BundledCjkFontProvider` now requires the representative probe
`你好，请解释当前程序。中文回复预览` to be inserted into the TMP asset before it
caches that asset. The backend contract is
`TryAddCharacters(TMP_FontAsset asset, string characters, out string missing)`.
False, non-empty `missing`, and exceptions reject and release the created
asset. The provider records one warning and does not retry after a rejection.

## Player.log Messages

- Success: `VizzyGPT bundled CJK font glyph validation succeeded.`
- Failure: `VizzyGPT bundled CJK font glyph validation failed; Chinese text may appear as missing glyphs.`

Neither message includes user-entered text.

## Tests

`BundledCjkFontProviderTests` covers successful cached insertion, false return,
reported missing characters, and exceptions. Each failure case asserts a single
release, a single warning, and no retry.

The first automated run exposed two test-compilation defects: a missing
`System` import and ambiguous `Object` references. Both were fixed in the test
source before the verified run.

Full Unity EditMode verification:

```text
Unity EditMode tests passed: 85/85
```

The verified result is in `artifacts/editmode-results.xml`.

## Preserved Worktree Changes

`.superpowers/sdd/task-4-report.md` and the CRLF-only changes in
`unity/VizzyGPT/Packages/manifest.json` and
`unity/VizzyGPT/Packages/packages-lock.json` remain unstaged and unmodified.
