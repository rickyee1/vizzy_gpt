# Task 3 CJK Integration Report

## Scope Implemented

- `unity/VizzyGPT/Assets/VizzyGPT/Runtime/VizzyGptBehaviour.cs`
  - Creates one `SystemCjkFontProvider` in `Awake`.
  - Resolves one `TMP_FontAsset` while mounting the panel and reuses that asset for preview dialogs.
  - Disposes and clears the provider in `OnDestroy`.
- `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/VizzyGptPanelController.cs`
  - Adds nullable `TMP_FontAsset` support to `Bind`.
  - Applies the font to the prompt input, transcript, and status only.
- `unity/VizzyGPT/Assets/VizzyGPT/Runtime/Ui/PreviewDialogController.cs`
  - Adds nullable `TMP_FontAsset` support to `Bind`.
  - Applies the font to summary, added, changed, removed, and warnings only.
- `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/VizzyGptUiLifecycleTests.cs`
  - Adds a map-backed `FakeXmlLayout` and panel binding coverage for the three dynamic targets plus an unchanged button label.
- `unity/VizzyGPT/Assets/VizzyGPT/Tests/EditMode/PreviewDialogControllerTests.cs`
  - Adds a map-backed `FakeXmlLayout` and preview binding coverage for the five dynamic targets plus an unchanged button label.
- `docs/manual-test-checklist.md`
  - Adds the five Chinese-rendering manual acceptance checks as pending.

## TDD Evidence

The two controller tests were written before the production integration. They call the new nullable `Bind` overloads and assert font identity for every required dynamic target and non-identity for representative static button labels.

The RED and GREEN Unity runner executions were not performed in this task because the controller owns the escalated Unity/Core test run and the instruction explicitly prohibits launching Unity or Juno. This is a verification deferral, not a passing-test claim.

## Static Checks

- `git diff --check` exited `0` with no whitespace errors. Git emitted existing CRLF conversion warnings for modified text files.
- Static source inspection confirms `CjkTextFontApplicator` is called only for prompt input, transcript, status, summary, added, changed, removed, and warnings.
- Settings fields and static labels/buttons have no new font application code.

## Deferred Runtime Verification

The controller must run the following before marking the manual items complete:

1. `powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1`
2. `powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1`
3. Build and install `Mods\VizzyGPT.sr2-mod`, then record its size, timestamp, and SHA-256.
4. Perform the five pending Juno Chinese-rendering acceptance checks and inspect `Player.log` for duplicate font warnings.
