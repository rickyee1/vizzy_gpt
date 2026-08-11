# VizzyGPT 0.1.0 Manual Test Checklist

Target: Juno: New Origins 1.4.105.0c on Windows 11

Mod artifact: `VizzyGPT.sr2-mod`
Test program: the saved craft program containing the existing `while true` control loop.

Automated tests support this checklist but do not replace game-lifecycle checks.

## Build And Load

- [x] Build version 0.1.0 from the final source and install it at `UserData/../Mods/VizzyGPT.sr2-mod`.
- [x] Restart Juno with VizzyGPT enabled and confirm one successful load line in `ModLoadLog.txt`.
- [x] Confirm `Player.log` contains no VizzyGPT exception or error in the final package load smoke test.
- [x] Open a Vizzy program with the final package and open VizzyGPT. Expected: the panel mounts and the active editor contract resolves without an ambiguous-contract exception.
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
- Result: passed in Juno 1.4.105.0c with the installed package.

## Editor Modify

- [x] Prompt `add loopback variable`. Expected preview line: `Added variable 'gpt_loopback'.`
- [x] Cancel the first preview and confirm the program is unchanged.
- [x] Generate the same preview again, Apply it, save the craft, leave Vizzy, reopen Vizzy, and confirm `gpt_loopback` persists.
- [x] Return an operation that removes a protected structural root, then return a valid repair. Expected: exactly one repair request and an applicable preview.
- [x] Return protected-root removals for both the initial and repair response. Expected: no Preview action, no mutation, and an expandable technical error row.
- [x] Open the repaired preview and Cancel. Expected: the editor program remains unchanged.
- [ ] With the final selector-schema build, prompt `帮我写一个自动着陆程序，只使用当前 Vizzy 节点目录中的节点`. Expected: the model returns a locally valid patch and Preview opens without a canonical-selector error. Live attempts on 2026-08-04 were blocked before patch parsing by the configured Synapse endpoint: one no-response failure after 60.5 seconds and one HTTP 500 after 23.9 seconds.
- [x] Request setting throttle. Preview showed `Inserted <SetInput> after id 0`; the applied editor program showed one `SetInput` node with `input=throttle`, `style=set-input`, and value `0.5`.
- [x] Cancel the throttle preview and confirm the editor canvas remains unchanged.
- [x] Apply the same verified throttle preview, use native `SAVE TO CRAFT`, leave Vizzy, reopen Vizzy, and confirm `set Throttle to 0.5` persists.
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

- [ ] Return HTTP 401 from the loopback server. Expected: authentication status, no automatic transport retry, and no program mutation.
- [ ] Return HTTP 429. Expected: rate-limit status, no automatic transport retry, and no program mutation.
- [ ] Stop the loopback server. Expected: network failure status and no program mutation.
- [ ] Delay the loopback response beyond the configured timeout. Expected: timeout status and no program mutation.
- [x] Return HTTP 500, 502, 503, 504, 520, 522, 523, or 524 once and then a valid response in Core fixtures. Expected: exactly one automatic retry succeeds within the original timeout budget.
- [x] Throw a typed transient transport failure once and then return a valid response in Core fixtures. Expected: the retry receives only the remaining whole-second timeout budget.
- [x] Return or throw a transient failure twice in Core fixtures. Expected: exactly two total attempts and an error that states one automatic retry already occurred.
- [x] Parse a Chat Completions SSE fixture. Expected: streamed content and provider reasoning summary are reassembled, usage is retained when supplied, and raw `reasoning_content` is ignored.
- [x] Normalize `HTTP request did not receive a response: Empty reply from server`. Expected: the benign reason remains visible and is not replaced by `[REDACTED REQUEST CONTEXT]`.
- [ ] Cancel an active request. Expected: `Request cancelled.` and no program mutation.
- [ ] Return malformed model JSON twice. Expected: one repair request, then a non-applicable error with Preview hidden.
- [ ] Confirm displayed diagnostics and logs contain no API key, bearer token, stack trace, or raw secret.

## Compatibility And Removal

- [x] Run the compatibility fixtures for Compatible, EditorUnavailable, AmbiguousContract, an inactive duplicate editor, and multiple active editors. Expected: inactive instances are ignored; true ambiguity retains Ask, hides Modify/Preview, and reports distinct type/member/instance diagnostics without throwing.
- [ ] Disable VizzyGPT, restart Juno, and confirm the saved stock Vizzy program still loads and runs.
- [ ] Re-enable VizzyGPT and confirm settings and saved programs remain intact.

## Evidence

- Automated Core results: `535/535` passed on 2026-08-12.
- Automated Unity EditMode results: `156/156` passed on 2026-08-12.
- Automated Unity result files: `artifacts/editmode-results.xml`, `artifacts/unity-editmode.log`.
- Final package build log: `artifacts/unity-package-stream-retry.log`.
- Runtime logs to inspect after final restart:
  `C:/Users/rickyee/AppData/LocalLow/Jundroo/SimpleRockets 2/ModLoadLog.txt` and
  `C:/Users/rickyee/AppData/LocalLow/Jundroo/SimpleRockets 2/Player.log`.
- Final load line: `Mod Loaded: VizzyGPT, Version 0.1 - 8/11/2026 5:39:26 PM`.
- Final package size: `16,818,251` bytes.
- Final package hash: `B4D6BC1F93EA7C9935E6AC3BEB8E9F17ACC7643AF4E34DE0A9E86E98D013065C`.
- Final load smoke: the rebuilt package loaded on Juno 1.4.105.0c and the current `Player.log` contained no VizzyGPT error or exception. The existing `CylinderTank`/`PartConnection` craft warnings are unrelated to VizzyGPT.
- Final editor mount acceptance: after entering the Vizzy editor, `Player.log` recorded `VizzyGPT resolved Vizzy runtime contract: Assets.Scripts.Vizzy.UI.VizzyUIScript.FlightProgram` followed by successful bundled CJK glyph validation. No `AmbiguousContract`, `ArgumentException`, or `ConfigureMountedPanel` failure recurred.
- Editor-probe regression coverage: an inactive loader-only editor is excluded from candidate selection, while multiple active candidates return a non-modifiable ambiguity result instead of throwing during panel mount.
- Live resilient-chat acceptance on Juno 1.4.105.0c before the stream-retry rebuild: Chinese input/reply, waiting stage and elapsed time, reasoning disclosure, one-shot repair, non-applicable double failure, contextual preview/cancel, active-conversation clear, and panel-reopen history restoration all passed.
- Live selector-schema follow-up on Juno 1.4.105.0c before the stream-retry rebuild: the package loaded, Chinese input rendered, Enter sent, and Up restored the previous prompt. The configured `https://synapse-ai.uk/` endpoint did not return an automatic-landing patch in either attempt, so that larger patch scenario remains unverified rather than passed.
- Live throttle acceptance on Juno 1.4.105.0c before the stream-retry rebuild: Preview displayed one `SetInput` insertion with `throttle=0.5`; Cancel left the canvas unchanged; Apply followed by native save, leaving Vizzy, and reopening Vizzy restored the node.
- Stream-retry live follow-up: the final package load is verified, but the original Synapse prompt `你能看懂我这个程序吗，bl表示back left，指的后面左边的舵面，以此类推` has not yet been resent with this package because Computer Use could not enumerate the desktop window after restart. Do not record this scenario as passed until a response or bounded retry result is observed.
- Conversation persistence evidence: `UserData/VizzyGPT/Conversations/c397d56845a14713ae6ddbda84714664.json` remained valid UTF-8 JSON with 20 messages, and the updated program hash was linked to that conversation in `index.json`.
