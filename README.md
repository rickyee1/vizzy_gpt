# VizzyGPT

VizzyGPT is a Juno: New Origins mod that connects the Vizzy editor and flight scene to an OpenAI-compatible API. It reads the structured Vizzy program, sends bounded context, validates typed patch operations locally, and requires a preview before any editor change.

Target: Juno 1.4.104.0c on Windows 11.

## Install

Place `VizzyGPT.sr2-mod` in:

```text
C:\Users\<user>\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods
```

Enable VizzyGPT in Juno's Mods screen and restart the game.

## Configure

Open the VizzyGPT settings button and set:

- API mode: Responses, Chat Completions, or Auto
- Base URL: official or OpenAI-compatible endpoint
- Model
- API key
- Timeout

Plain HTTP is accepted only for loopback hosts. The API key is protected with Windows DPAPI and stored separately from non-secret settings.

## Use

- **Ask** sends a read-only normalized program context. In flight it also includes bounded telemetry and recent logs.
- **Modify** requests a typed patch, validates it locally, and opens a preview before Apply.
- Ask and Modify keep separate persistent conversations. Completed requests show elapsed time and expandable reasoning summaries when the provider returns one.
- Invalid Modify output gets one constrained repair attempt. If repair also fails, the patch remains non-applicable and technical details can be expanded from the error message.
- **Undo** restores the pre-apply program after another validated backup.
- **Flight Modify** saves a pending change without replacing the running program. Return to Vizzy to rebase, preview, and apply it.
- Chinese prompt and response text use the bundled Noto Sans CJK SC font.
- Settings can clear only the active conversation without changing the endpoint, model, timeout, or protected API key.

If the running Juno version does not expose one unambiguous Vizzy editor contract, Ask remains available while Modify and Preview are hidden.

## Development

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Sync-Core.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-Unity.ps1
```

The Unity project is under `unity/VizzyGPT`. Core code and tests are under `src` and `tests`. Manual release coverage is tracked in `docs/manual-test-checklist.md`.

Last updated: 2026-07-30.
