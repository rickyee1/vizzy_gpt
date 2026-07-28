# Task 4 Automated Verification, Package, And Install Report

Date: 2026-07-29

## Automated Verification

- Unity EditMode: `87/87` passed, `0` failed, `0` skipped, `0` inconclusive.
  Parsed from `artifacts/editmode-results.xml` after `tools/Test-Unity.ps1`.
- Core: the normal `tools/Test-Core.ps1` run was blocked before execution by
  the known Windows testhost parent-process handle failure:
  `System.ComponentModel.Win32Exception (5): Access is denied` while enabling
  the parent-process exit callback.
- Core workaround: the repository's documented `CODEX_SHELL=1` path built with
  `0` warnings and `0` errors, then passed `446/446` tests with `0` failures
  and `0` skipped.
- Two stalled `dotnet` processes launched by the blocked core-test attempts
  (PIDs `2636` and `54080`) were targeted for termination. A subsequent
  `taskkill` reported both PIDs no longer existed.

## Final Source Inspection

Reviewed `285b01f..HEAD` for the requested bundled-font release concerns.

- The unmodified SIL OFL 1.1 text and `NotoSansCJKsc-Regular.otf` are present.
- `ModData.asset` explicitly lists both font and license under `_otherAssets`.
- The runtime creates one dynamic multi-atlas TMP asset and disposes only that
  runtime-created asset in `OnDestroy`.
- CJK application remains limited to prompt/placeholder, transcript, status,
  and preview dynamic text; static UI remains on the game font.
- No production `SystemCjkFontProvider` reference remains.
- No Critical or Important blocker was found. This inspection does not replace
  the independent final review.

## Release Package

- Artifact: `artifacts/VizzyGPT.sr2-mod`
- Size: `16,729,355` bytes
- SHA-256: `48ACE2178DFE54D3E1AAB9877672CF10870251F7978B299F95C1A6608CAC7D30`
- Build: ModTools `BuildAssetBundles` with `StandaloneWindows64` and
  `debugBuild: false`.
- Build log: `artifacts/unity-package-task-4.log` reports both bundled assets:
  `NotoSansCJKsc-Regular.otf` at 15.7 MB and `OFL.txt` at 4.2 KB.
- The package is materially larger than the previous 275,819-byte
  system-font package.

## Install

- Juno (`SimpleRockets2.exe`) was not running before installation and was not
  launched by this task.
- Installed artifact:
  `C:\Users\rickyee\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods\VizzyGPT.sr2-mod`
- Installed size: `16,729,355` bytes
- Installed SHA-256:
  `48ACE2178DFE54D3E1AAB9877672CF10870251F7978B299F95C1A6608CAC7D30`
- Artifact and installed hashes match.

## Cleanup And Working Tree

- The temporary `VizzyGptBatchModBuilder.cs` and its generated file `.meta`
  were removed after the successful build.
- The generated empty-folder metadata `unity/VizzyGPT/Assets/Editor.meta` was
  removed; no release asset includes the helper.
- Preserved existing CRLF-only working-tree modifications:
  `unity/VizzyGPT/Packages/manifest.json` and
  `unity/VizzyGPT/Packages/packages-lock.json`.
- Live acceptance passed for Ask, editor Modify preview/cancel/apply and
  persistence, flight pending handoff, and Chinese prompt/reply/preview text.
- The temporary loopback API was stopped and removed. The user's original
  endpoint settings and protected API key were restored with matching hashes.
