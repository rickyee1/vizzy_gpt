# VizzyGPT 0.1.0 Manual Test Checklist

Target: Juno: New Origins 1.4.101.0c on Windows 11

Mod artifact: `VizzyGPT.sr2-mod`
Test program: the saved craft program containing the existing `while true` control loop.

Automated tests support this checklist but do not replace game-lifecycle checks.

## Build And Load

- [x] Build version 0.1.0 from the final source and install it at `UserData/../Mods/VizzyGPT.sr2-mod`.
- [x] Restart Juno with VizzyGPT enabled and confirm one successful load line in `ModLoadLog.txt`.
- [x] Confirm `Player.log` contains no VizzyGPT exception or error in the final package load smoke test.
- [ ] Record the final package SHA-256 in this file.

## Settings And Chat

- [x] Open Settings and confirm base URL, API mode, model, masked API key, timeout, destination host, Test Connection, Save, and Cancel are visible. Input text renders visibly.
- [x] Save the loopback Responses configuration and receive `Loopback test response.` in Ask mode.
- [ ] With the final build, use prompt `Explain the active Vizzy program and identify its main loop.` Expected: the response describes the active `while true` program; Apply/Preview remains unavailable.
- [ ] Repeat with Chat Completions mode against the loopback endpoint.
- [ ] Configure a custom HTTPS-compatible base URL and confirm the displayed destination host matches it.

## Editor Modify

- [x] Prompt `add loopback variable`. Expected preview line: `Added variable 'gpt_loopback'.`
- [x] Cancel the first preview and confirm the program is unchanged.
- [x] Generate the same preview again, Apply it, save the craft, leave Vizzy, reopen Vizzy, and confirm `gpt_loopback` persists.
- [ ] Exercise every first-release operation with a valid fixture: add/rename/remove variable, insert before/after, insert into container, replace subtree, remove node, move node, and update attribute/constant.
- [ ] Change the program after a preview is generated, then press Apply. Expected: stale hash error, no mutation, and regeneration required.
- [ ] Apply a valid change, press Undo, and confirm the exact original program semantics return.
- [ ] Force backup storage failure. Expected: Apply fails before the editor program is changed.

## Flight Pending

- [x] Launch the craft, wait at least 10 seconds, and use Ask. Expected: a response is received while the flight remains active.
- [x] In flight use prompt `add flight pending variable`. Expected preview line: `Added variable 'flight_pending_test'.`
- [x] Apply in flight. Expected status: `Pending change saved. Return to Vizzy to apply it.` The running program is not replaced.
- [x] Return to Vizzy, preview the rebased pending change, Apply it, save, leave and reopen Vizzy, and confirm `flight_pending_test` persists.
- [ ] Prepare another flight pending change, manually edit its targeted node before returning, then open Vizzy. Expected: conflict status and no forced apply; Regenerate or Discard is required.

## Failure Paths

- [ ] Return HTTP 401 from the loopback server. Expected: retryable authentication status and no program mutation.
- [ ] Return HTTP 429. Expected: retryable rate-limit status and no program mutation.
- [ ] Stop the loopback server. Expected: network failure status and no program mutation.
- [ ] Delay the loopback response beyond the configured timeout. Expected: timeout status and no program mutation.
- [ ] Cancel an active request. Expected: `Request cancelled.` and no program mutation.
- [ ] Return malformed model JSON twice. Expected: one repair request, then a non-applicable error with Preview hidden.
- [ ] Confirm displayed diagnostics and logs contain no API key, bearer token, stack trace, or raw secret.

## Compatibility And Removal

- [ ] Run the compatibility fixtures for Compatible, EditorUnavailable, and AmbiguousContract. Expected: incompatible states retain Ask, hide Modify/Preview, and show only type/member diagnostics.
- [ ] Disable VizzyGPT, restart Juno, and confirm the saved stock Vizzy program still loads and runs.
- [ ] Re-enable VizzyGPT and confirm settings and saved programs remain intact.

## Evidence

- Automated Core results: `446/446` passed on 2026-07-23.
- Automated Unity EditMode results: `63/63` passed on 2026-07-23.
- Automated result files: `artifacts/editmode-results.xml`, `artifacts/unity-editmode.log`.
- Runtime logs to inspect after final restart:
  `C:/Users/rickyee/AppData/LocalLow/Jundroo/SimpleRockets 2/ModLoadLog.txt` and
  `C:/Users/rickyee/AppData/LocalLow/Jundroo/SimpleRockets 2/Player.log`.
- Final load line: `Mod Loaded: VizzyGPT, Version 0.1 - 7/23/2026 3:28:56 AM`.
- Final package hash: `AC8418964694122C442D18D2F8B1CF60E06EFF00FCF9F25229138E4146197B8D`.
