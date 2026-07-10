# VizzyGPT Mod Design

Date: 2026-07-10
Status: Approved for implementation planning

## Summary

VizzyGPT is a Juno: New Origins mod that adds a GPT assistant to the Vizzy editor and flight scene. It reads Vizzy's structured XML representation, sends a normalized program context to an OpenAI-compatible API, accepts typed JSON patch operations, validates those operations locally, and lets the user preview changes before applying them.

The first release targets the user's Windows 11 installation of Juno: New Origins `1.4.0.8c` on the `open_beta` branch and uses the matching ModTools package installed with the game. It is packaged as one installable `.sr2-mod` and does not require a separate background process.

## Goals

- Explain the current Vizzy program in natural language.
- Generate and modify Vizzy programs from prompts without simulating mouse input.
- Show a reviewable change preview before changing the editor state.
- Support GPT-assisted diagnosis during flight using the program snapshot, Vizzy logs, and rate-limited telemetry.
- Allow a change to be prepared during flight and applied after returning to the Vizzy editor.
- Preserve the original program on every failure path.
- Support the official OpenAI API and configurable OpenAI-compatible base URLs.

## Non-Goals

- Replacing a Vizzy program while it is executing in flight.
- Giving the model unrestricted game control or filesystem access.
- Native adapters for non-OpenAI API formats in the first release.
- An MCP server or external helper process in the first release.
- Mobile, macOS, or Linux support in the first release.
- Editing craft XML or game saves outside the current Vizzy program.

## Confirmed Product Decisions

- The assistant is a game mod with an in-game panel.
- Changes use preview-before-apply behavior.
- API configuration includes an API key, model name, and custom base URL.
- The assistant is available in the Vizzy editor and flight scene.
- The model returns structured patch operations rather than complete replacement XML.
- A flight-generated patch is stored as a pending change and can be applied only in the Vizzy editor.
- Each program has at most one pending flight-generated change in the first release.

## Architecture

### Mod Bootstrap

`VizzyGptMod` owns startup, shutdown, scene transitions, compatibility checks, and service lifetime. It registers through the Juno Mod API and subscribes to user-interface loading events. It must remove all event handlers and destroy all created objects when unloaded.

If required Vizzy hooks cannot be resolved for the running game version, the bootstrap enables read-only chat where possible and disables all modification actions.

### UI Integration

`UiIntegration` adds a stock-styled GPT button to the Vizzy editor and flight UI using Juno's XML layout system.

The Vizzy side panel contains:

- A segmented `Ask` / `Modify` mode control.
- Conversation history for the current program.
- A multiline prompt input.
- Send and cancel controls.
- Request and validation status.
- A settings button.
- A pending-change indicator when applicable.

The preview dialog shows the assistant's summary, additions, changes, removals, validation warnings, and `Apply` / `Cancel` controls. Applying changes updates the in-memory Vizzy editor. The user still uses Vizzy's existing save-to-craft action for final persistence.

The flight panel contains chat, program context status, Vizzy log access, telemetry inclusion controls, and the pending-change status. It never exposes an action that mutates the executing program.

### Vizzy Context

`VizzyContextService` obtains the current in-memory Vizzy program when the editor is open. It serializes the program to XML, parses it into a normalized abstract syntax tree, records a canonical hash, and extracts variables, custom instructions, custom expressions, events, node IDs, styles, and relevant attributes.

During flight it retains the launch-time program snapshot and collects:

- Vizzy and flight log messages associated with the active craft.
- Basic navigation and motion telemetry needed for diagnosis.
- Active craft identity and program fingerprint.

Telemetry is sampled at a bounded rate and summarized before an API request. It is never streamed continuously to the API.

For small and medium programs, the complete normalized tree is sent. For large programs, the context builder sends the relevant subtree plus declarations and a structural summary. The assistant may request one additional named subtree before producing a patch.

### API Client

`OpenAiClient` performs asynchronous HTTPS requests without blocking the Unity main thread. Configuration includes:

- API key.
- Base URL.
- Model name.
- API mode: `Auto`, `Responses`, or `Chat Completions`.
- Request timeout.

`Auto` prefers the Responses API and falls back to Chat Completions only when the configured endpoint explicitly reports that Responses is unsupported. Authentication and rate-limit failures do not trigger endpoint fallback.

Plain HTTP is rejected except for loopback hosts. The settings panel provides a test-connection action and displays the destination host before saving credentials.

### Patch Protocol

The model response has two channels: user-facing explanation text and a typed patch document. The patch document contains a base program hash and an ordered list of operations.

Supported first-release operations are:

- Add, rename, and remove a variable.
- Insert a node before or after a target node.
- Insert a node into an instruction container.
- Replace a node or expression subtree.
- Remove a node.
- Move a node within compatible instruction containers.
- Update an allowed node attribute or constant value.

Targets use stable Vizzy node IDs where available. Declarations and nodes without stable IDs use an unambiguous typed selector derived from their normalized path. A selector that resolves to zero or multiple targets fails validation.

The model cannot submit arbitrary file paths, raw filesystem operations, or executable code through the patch protocol.

### Patch Validation

`PatchValidator` applies a patch to an in-memory copy and rejects it unless all checks pass:

- The base hash matches the program used to generate the patch.
- Every operation is recognized and every target resolves exactly once.
- Node IDs are unique after application.
- Instruction and expression nodes occur only in valid positions.
- Required child counts and parameter types are satisfied.
- Variable and custom-node references resolve.
- Node styles and properties exist in the current game's Vizzy catalog.
- Program size and nesting remain within configured safety limits.
- The result serializes to XML and parses back to the same normalized tree.

Invalid model output receives one automatic schema-repair request. A second invalid result is shown as text only and cannot be applied.

### Change Sessions, Backup, and Undo

`ChangeSessionService` records the base snapshot, patch, validated result, preview summary, and current editor hash.

Immediately before apply, it compares the current editor hash with the base hash. If they differ, the patch is not applied. The user must regenerate against the current program.

Before changing editor state, the original XML is written to the backup store. The latest 20 backups per program are retained. The panel exposes immediate undo of the most recently applied session. Undo also passes through parse and compatibility validation.

### Flight-Prepared Changes

At flight start, the mod records the active program snapshot and hash. A modify request in flight is generated and validated against that snapshot, then stored as one pending change for the program. It does not alter the executing program.

When the user exits flight and opens the matching program in Vizzy:

1. The mod detects the pending change and opens a prompt.
2. If the current hash matches the pending change's base hash, the existing validated preview is shown.
3. If the hash differs, `PendingChangeRebaser` compares fingerprints of every declaration and node targeted or referenced by the patch.
4. If all affected fingerprints are unchanged and every selector still resolves exactly once, it rebuilds the patch with the latest base hash and runs the complete validator again.
5. If any affected fingerprint changed, a selector became ambiguous, or validation fails, the old patch remains unapplied and GPT is asked to regenerate against the latest program.
6. The user must approve the final preview before the editor is changed.

Creating another flight modification for the same program requires replacing or discarding the existing pending change.

## Data Storage and Privacy

Mod-owned data is stored below `UserData/VizzyGPT`:

- `settings.json`: non-secret settings and DPAPI-encrypted API credentials.
- `Conversations/`: conversations keyed by program fingerprint and craft identity.
- `Pending/`: one pending flight-generated change per program.
- `Backups/`: the latest 20 pre-apply program snapshots per program.
- `Logs/`: local diagnostic logs with secrets redacted.

The API key is encrypted with Windows DPAPI for the current user. It is never written to a Vizzy program, craft XML, flight save, conversation transcript, or diagnostic log.

An API request includes only the user's prompt, the required normalized Vizzy context, conversation context, and explicitly selected log or telemetry summaries. Full craft XML and game saves are excluded.

## Error Handling

- Missing credentials, authentication errors, rate limits, network failures, cancellation, and timeouts leave the program unchanged and present a retryable status.
- Invalid JSON or schema output receives one repair attempt and then becomes non-applicable text.
- Validation failure displays the failed rules and keeps `Apply` disabled.
- A stale program hash requires regeneration and never performs a forced merge.
- A backup write failure prevents apply.
- A failed editor update restores the pre-apply snapshot and reports the failure.
- Unsupported game versions disable mutation features rather than attempting reflection calls blindly.
- All API errors and model content are treated as untrusted display data and escaped for the XML UI.

## Compatibility Strategy

The implementation uses public `ModApi` interfaces for lifecycle, UI, settings, and scene access where available. Vizzy-specific behavior not exposed by `ModApi` is isolated behind a `VizzyRuntimeAdapter`. Reflection-based access is allowed only inside this adapter and is guarded by explicit member checks.

The first compatibility target is Juno `1.4.0.8c open_beta` with the ModTools package currently installed at `D:/Steam/steamapps/common/SimpleRockets2/ModTools/SimpleRockets2_ModTools.unitypackage`.

The adapter exposes a small internal interface so a future game update requires changes in one module rather than throughout the mod.

## Testing

### Unit Tests

Pure C# tests cover:

- XML-to-AST and AST-to-XML round trips.
- Canonical hashing.
- Every supported patch operation.
- Stable and path-based target resolution.
- Duplicate IDs, invalid nesting, missing references, and unsupported nodes.
- Serialization round-trip validation.
- Pending-change matching and conflict behavior.
- Backup retention, restore, and undo.
- Credential and log redaction.

Existing files under `UserData/FlightPrograms` are read-only inputs. Tests operate on copies and generated fixtures.

### API Contract Tests

A local fake server covers Responses and Chat Completions payloads, streaming-independent response parsing, malformed JSON, schema repair, authentication failure, rate limiting, timeout, cancellation, custom base URLs, and secret redaction. Automated tests do not require or consume a real API key.

### Unity and Game Integration Tests

Integration checks use the current game and matching ModTools:

- The mod loads without errors in `ModLoadLog.txt` and `Player.log`.
- GPT buttons and panels appear only in the intended scenes.
- Text input does not leak keystrokes into craft controls.
- The current Vizzy program can be read and explained.
- A valid patch can be previewed, applied, saved to the craft, and reloaded.
- Cancel and undo restore the exact original program semantics.
- API and validation failures do not change editor state.
- Flight logs and bounded telemetry reach the context builder.
- A flight-prepared change appears on return to the matching Vizzy editor.
- A stale pending change is rejected or regenerated rather than force-applied.
- Disabling the mod leaves stock Vizzy behavior intact.

## Acceptance Criteria

- The deliverable is one installable `.sr2-mod` for the target Windows game version.
- The user can configure an API key, model, and official or custom OpenAI-compatible base URL in game.
- The assistant can explain the active Vizzy program.
- The assistant can produce valid previews using all first-release patch operations.
- No model or network failure path mutates the current program.
- Applied programs can be saved and reloaded by stock Vizzy.
- Flight-generated changes can be reviewed and applied after returning to Vizzy.
- Backups and immediate undo recover the previous program.
- Removing or disabling the mod does not prevent stock Vizzy programs from loading.

## Future Extensions

- A local MCP server exposing the same context and patch services.
- Cross-platform credential storage and macOS support.
- Multiple queued pending changes and three-way visual merges.
- Native adapters for additional AI providers.
- Model-assisted test flights and richer telemetry tools.
