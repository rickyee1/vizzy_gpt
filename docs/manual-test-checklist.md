# VizzyGPT 0.1.0 Manual Test Checklist

Target: Juno: New Origins 1.4.104.0c on Windows 11

Mod artifact: `VizzyGPT.sr2-mod`
Test program: the saved craft program containing the existing `while true` control loop.

Automated tests support this checklist but do not replace game-lifecycle checks.

## Build And Load

- [x] Build version 0.1.0 from the final source and install it at `UserData/../Mods/VizzyGPT.sr2-mod`.
- [x] Restart Juno with VizzyGPT enabled and confirm one successful load line in `ModLoadLog.txt`.
- [x] Confirm `Player.log` contains no VizzyGPT exception or error in the final package load smoke test.
- [x] Record the final package SHA-256 in this file.

## Settings And Chat

- [x] Open Settings and confirm base URL, API mode, model, masked API key, timeout, destination host, Test Connection, Save, and Cancel are visible. Input text renders visibly.
- [x] Save the loopback Responses configuration and receive `Loopback test response.` in Ask mode.
- [x] Send a delayed Chinese Ask request. Expected: the active stage and elapsed time update while waiting, then the Chinese answer and total elapsed time remain visible.
- [x] Expand a provider reasoning summary. Expected: the summary and per-stage timing are visible.
- [ ] Receive a successful response without provider reasoning. Expected: the row does not invent or display a reasoning summary.
- [x] Close and reopen the panel. Expected: the active conversation is restored from persistent storage.
- [x] Clear the active Modify conversation in Settings. Expected: only that conversation is cleared and the endpoint, model, timeout, and masked API key remain unchanged.
- [x] With an empty Modify composer, press Up. Expected: the most recently sent prompt is restored; pressing Enter sends it.
- [ ] With the final build, use prompt `Explain the active Vizzy program and identify its main loop.` Expected: the response describes the active `while true` program; Apply/Preview remains unavailable.
- [ ] Repeat with Chat Completions mode against the loopback endpoint.
- [ ] Configure a custom HTTPS-compatible base URL and confirm the displayed destination host matches it.
- [ ] Use Ask mode for a Vizzy question without receiving the large node reference. Expected: Ask remains usable without that reference.

## Chinese Text Rendering

- [x] Type a Chinese request in the prompt. Expected: every character is visible.
- [x] Send the request and receive a Chinese answer. Expected: transcript text is visible.
- [x] Request a Modify patch with a Chinese summary and open Preview. Expected: summary and warning content are visible.
- [x] Close and reopen the panel. Expected: no duplicate font warning appears in `Player.log`.
- [x] Confirm English labels and buttons retain their existing appearance.
- Result: passed in Juno 1.4.101.0c with the final installed package.

## Editor Modify

- [x] Prompt `add loopback variable`. Expected preview line: `Added variable 'gpt_loopback'.`
- [x] Cancel the first preview and confirm the program is unchanged.
- [x] Generate the same preview again, Apply it, save the craft, leave Vizzy, reopen Vizzy, and confirm `gpt_loopback` persists.
- [x] Return an operation that removes a protected structural root, then return a valid repair. Expected: exactly one repair request and an applicable preview.
- [x] Return protected-root removals for both the initial and repair response. Expected: no Preview action, no mutation, and an expandable technical error row.
- [x] Open the repaired preview and Cancel. Expected: the editor program remains unchanged.
- [ ] With the final selector-schema build, prompt `帮我写一个自动着陆程序，只使用当前 Vizzy 节点目录中的节点`. Expected: the model returns a locally valid patch and Preview opens without a canonical-selector error. Live attempts on 2026-08-04 were blocked before patch parsing by the configured Synapse endpoint: one no-response failure after 60.5 seconds and one HTTP 500 after 23.9 seconds.
- [ ] Request setting throttle. Expected: the generated node is `SetInput` with style `set-input`.
- [ ] Return a fabricated `set-throttle` first response for a throttle request. Expected: exactly one repair request uses the valid `SetInput`/`set-input` template.
- [ ] Return responses outside the catalog for both the initial and repair attempts. Expected: no Preview and no program mutation.
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

- Automated Core results: `509/509` passed on 2026-08-04.
- Automated Unity EditMode results: `146/146` passed on 2026-08-04.
- Automated result files: `artifacts/editmode-results.xml`, `artifacts/unity-editmode.log`.
- Runtime logs to inspect after final restart:
  `C:/Users/rickyee/AppData/LocalLow/Jundroo/SimpleRockets 2/ModLoadLog.txt` and
  `C:/Users/rickyee/AppData/LocalLow/Jundroo/SimpleRockets 2/Player.log`.
- Final load line: `Mod Loaded: VizzyGPT, Version 0.1 - 8/4/2026 2:03:09 PM`.
- Final package size: `16,809,035` bytes.
- Final package hash: `A27E635431340AE80A1FC31CA46520631349A2929CC89616AF9886BAFEE334EB`.
- Live resilient-chat acceptance on Juno 1.4.104.0c: Chinese input/reply, waiting stage and elapsed time, reasoning disclosure, one-shot repair, non-applicable double failure, contextual preview/cancel, active-conversation clear, and panel-reopen history restoration all passed.
- Live selector-schema follow-up on Juno 1.4.104.0c: the final package loaded, Chinese input rendered, Enter sent, and Up restored the previous prompt. The configured `https://synapse-ai.uk/` endpoint did not return a patch in either attempt, so Preview/local catalog validation remains unverified rather than passed.
