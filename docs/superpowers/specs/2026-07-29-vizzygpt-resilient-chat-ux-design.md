# VizzyGPT Resilient Modify and Chat UX Design

## Summary

VizzyGPT will replace its flat transcript and status presentation with a
structured, persistent conversation experience. Each request will expose
observable processing stages and elapsed time, show a reasoning summary only
when the configured API actually returns one, and attach preview or error
actions to the relevant assistant message.

Modify mode will become more resilient without weakening its existing
no-mutation-before-Apply guarantee. The patch engine will reject operations
that can mutate the three required root containers, runtime exceptions will be
unwrapped into actionable diagnostics, and one automatic repair request will
be attempted after a generated patch fails validation.

Chat Completions will use Server-Sent Events so long-running responses keep
the connection active. A typed transient transport failure or selected
temporary gateway status may be retried once without resetting the configured
timeout budget.

## Goals

- Prevent model-generated patches from adding, deleting, replacing, or moving
  the root `Variables`, `Instructions`, or `Expressions` containers.
- Preserve the current preview-before-mutation, backup, undo, flight pending,
  and program-hash protections.
- Automatically make one repair request when a Modify result cannot be safely
  previewed.
- Show request stages and total elapsed time for every request.
- Show a collapsible reasoning summary only when the API returns real reasoning
  summary content.
- Show useful, sanitized inner exception details instead of a bare
  `TargetInvocationException` message.
- Present user, assistant, progress, preview, and error information as a
  readable message history similar to Codex.
- Persist the most recent 50 messages for each program across game restarts.
- Keep Chinese input, response, status, reasoning, and error text rendered with
  the bundled CJK font.
- Keep long Chat Completions requests active through streamed responses and
  make one bounded retry for explicitly transient failures.

## Non-Goals

- Do not display or infer hidden chain-of-thought.
- Do not synthesize fake reasoning text when an endpoint provides none.
- Do not change the Responses API request from its existing non-stream contract.
- Do not allow the model to bypass local validation after a failed retry.
- Do not store API keys, authorization headers, complete program XML, complete
  request context, or raw generated patches in conversation history.
- Do not redesign the settings dialog beyond adding a clear-history command.

## Request Lifecycle

Sending a prompt creates a conversation turn immediately. The turn records the
user message, Ask or Modify mode, start time, and current program fingerprint
when one is available.

The controller reports these stages in order:

1. Reading the current Vizzy program or flight snapshot.
2. Building the request context.
3. Waiting for the model response.
4. Parsing the response envelope.
5. Applying the patch to an in-memory program.
6. Running structural, catalog, and Juno serializer validation.
7. Preparing the preview or saving a flight pending change.

Ask requests end after the model response is parsed. Modify requests end in
Preview Ready, Pending Saved, Error, or Cancelled. Every terminal state records
the total elapsed duration. A UI timer updates while a request is active, but
the persisted duration is the final measured value rather than a sequence of
timer events.

## API Response Metadata

`AiResponse` will carry optional response metadata in addition to the existing
message and patch:

- A reasoning summary string when supplied by the endpoint.
- Provider-reported usage values when present.
- A flag indicating whether the result came from an automatic repair request.

For the Responses API, the parser will accept standard reasoning summary
content associated with reasoning output items. Unsupported or unknown output
items remain ignored unless they invalidate the existing assistant output
contract. For Chat Completions, the request uses streaming and the parser
accepts both Server-Sent Events and a non-stream JSON fallback from compatible
relays. No reasoning is assumed. A compatible `reasoning_summary` field may be
accepted only when it is a plain string and does not alter the existing
assistant content requirements. Raw `reasoning_content` is ignored.

Raw hidden reasoning tokens are never requested for display and never written
to disk. If no reasoning summary exists, the collapsed reasoning row exposes
only locally measured processing stages and durations.

## Modify Safety Boundary

The three direct children required by a Vizzy program are protected roots:

- `/Program[0]/Variables[0]`
- `/Program[0]/Instructions[0]`
- `/Program[0]/Expressions[0]`

The patch engine will reject:

- Removing any protected root.
- Replacing any protected root.
- Moving any protected root.
- Inserting a second direct container with one of the protected names.
- Inserting or moving a node in a way that makes a protected container cease to
  be a direct child of `Program`.

Operations on descendants remain allowed when they satisfy the existing
selector, catalog, placement, identifier, variable, constant, and runtime
serializer rules. Variable declaration operations continue through their
dedicated patch operations rather than direct replacement of `Variables`.

These restrictions are enforced in code. The model instruction will also state
them explicitly, but prompt instructions are treated as guidance rather than a
security boundary.

## Automatic Repair

An automatic repair is attempted only for a model-produced Modify response that
cannot become a safe preview because of:

- Patch contract or selector failure.
- Protected-root violation.
- Pure XML validation failure.
- Catalog or placement validation failure.
- Juno `ProgramSerializer` rejection.

Transport errors, authentication failures, cancellation, timeouts, stale
program hashes, editor unavailability, backup failures, and Apply failures do
not trigger a schema-repair request.

Transport retry is a separate API-client concern. A typed transient transport
failure or HTTP 500, 502, 503, 504, 520, 522, 523, or 524 is retried at most
once. The second attempt receives only the whole seconds remaining from the
original endpoint timeout. Authentication failures, HTTP 429, cancellation,
explicit timeout exceptions, and arbitrary client exceptions are not retried.
If the second attempt fails, the diagnostic states that one automatic retry
already occurred. Because the first request may have reached the provider, a
retry can still duplicate provider-side usage.

The repair request contains:

- The original user request.
- The same original program context and base hash.
- A concise machine-readable validation code, path, and sanitized message.
- An instruction to return a complete replacement patch envelope against the
  original base hash.

It does not contain or apply the failed output document. The original source
document remains the base for the second attempt. Exactly one repair request is
allowed per user send. If it fails, the turn ends in Error and no editor
mutation occurs.

The message timeline shows that an automatic repair is in progress and includes
its time in the final request duration.

## Exception Diagnostics

Runtime error normalization will walk through
`TargetInvocationException.InnerException` and other single-inner wrapper
exceptions until it reaches the most specific meaningful exception. The
resulting diagnostic contains:

- A stable error code.
- The request stage.
- A short user-facing Chinese summary.
- The sanitized root exception type and message.
- A validation path when one exists.

The UI displays the summary by default and exposes technical details through a
collapsed disclosure. `Player.log` receives the sanitized technical diagnostic.
API keys, bearer tokens, authorization headers, and complete response bodies
remain excluded.

## Conversation Model

Conversation rendering will use structured entries instead of concatenating
escaped text into one `TranscriptText` value. Entries support:

- User message.
- Assistant response.
- Active progress with current stage and elapsed time.
- Reasoning disclosure with optional provider summary and local stage timings.
- Modify preview-ready action.
- Error summary and technical-detail disclosure.
- Cancelled state.

The controller owns immutable render models. The Unity view creates and updates
message rows from those models. This keeps persistence and request behavior
testable without requiring live Unity UI objects.

## Panel Layout

The panel remains a right-side tool and becomes wider while respecting the
available screen bounds.

- Header: VizzyGPT title, Ask/Modify segmented control, settings, and close.
- Body: one vertical scrolling message stream ordered oldest to newest.
- Message row: compact role label, body, timestamp or elapsed metadata, and
  contextual disclosures or actions.
- Active assistant row: current stage and live elapsed timer.
- Completed assistant row: response followed by a collapsed
  `Thought for 8.4s`-style disclosure translated for the active UI language.
- Modify success: a `Preview changes` action attached to the assistant row.
- Error: Chinese summary with a collapsed `Technical details` disclosure.
- Composer: fixed multiline input with Send; while active, Send changes to
  Cancel.

The standalone status line and pending indicator will no longer overlap or
compete with the composer. Preview remains a separate dialog with Apply and
Cancel so no patch mutates the editor from the conversation row.

All dynamically created TMP components receive the bundled CJK font through the
existing loader-first font lifecycle.

## Persistence

Conversation files live under:

`UserData/VizzyGPT/Conversations`

The store selects a stable conversation ID through a small index that maps
known program hashes to conversation IDs. A new, previously unseen program hash
creates a random conversation ID. After Apply or a successful pending-change
handoff, both the old and new program hashes are mapped to the same conversation
ID. This prevents a successful modification from making its own history appear
to disappear merely because the content hash changed.

A general conversation ID is used when Ask mode has no program hash. Changing
to an unrelated program loads its corresponding history. A program changed
outside VizzyGPT and not connected by the index is treated as a new program,
avoiding accidental history sharing between unrelated craft that happen to use
the same display name.

Each file keeps the most recent 50 messages and stores only:

- Schema version.
- Stable conversation ID and the non-secret program-hash aliases that resolve
  to it.
- Role and message kind.
- Display text.
- Optional reasoning summary.
- Stage names and measured durations.
- Final status and sanitized error metadata.
- Ask or Modify mode.
- UTC timestamp.

Writes use a temporary file followed by replacement. A malformed or unsupported
file is ignored with a sanitized warning and does not prevent the panel from
opening. Settings will provide a command to clear the active conversation
history.

The persistence schema excludes complete program XML, raw patch JSON, complete
request context, API keys, authorization values, and raw HTTP response bodies.

## Compatibility

- Responses endpoints with reasoning summaries show the summary.
- Responses endpoints without summaries show local stage timings only.
- Chat Completions endpoints support streamed SSE output and non-stream JSON
  fallback without requiring reasoning metadata.
- Existing OpenAI-compatible endpoint fallback behavior remains unchanged.
- Existing editor and flight workflows remain available when conversation
  persistence cannot be read or written.
- Juno runtime compatibility failures continue to disable Modify while leaving
  Ask available.

## Testing

Core tests will cover:

- Every protected-root operation is rejected without mutating its input.
- Ordinary descendant operations remain valid.
- Repair context uses the original base hash and a sanitized structured error.
- Responses reasoning summaries are normalized.
- Streamed Chat Completions content, optional summary/usage, and `[DONE]` are
  normalized; raw reasoning content is ignored.
- Typed transient transport failures and temporary gateway statuses retry once
  within the original timeout budget.
- Authentication, rate-limit, cancellation, explicit timeout, and arbitrary
  client failures do not automatically retry.
- Conversation serialization enforces the 50-message limit.
- Sensitive request fields and raw program or patch content are absent from
  persisted conversation JSON.
- Corrupt history fails open.

Unity EditMode tests will cover:

- Exactly one automatic repair attempt occurs for repairable Modify failures.
- A second invalid result ends in Error without calling the editor mutation
  adapter.
- Non-repairable stale-hash and cancellation failures do not trigger schema
  repair; transport retries remain confined to the API-client policy above.
- Progress stages and elapsed duration reach the render model.
- Wrapped reflection exceptions render the root diagnostic.
- Message rows, disclosures, composer actions, and preview actions bind to
  required XML controls.
- Dynamic message text retains the bundled CJK font after lifecycle refreshes.
- Conversation history is loaded when the panel opens and saved after terminal
  request states.

Full verification will run the complete Core and Unity EditMode suites. Manual
Juno acceptance will exercise:

1. Ask with Chinese input and response.
2. A response with reasoning summary and one without it.
3. Modify success through Preview, Cancel, Preview, and Apply.
4. A protected-root violation followed by one successful automatic repair.
5. A double validation failure that leaves the original program unchanged and
   shows useful technical details.
6. Flight pending change creation and editor-return preview.
7. Game restart with the active program's conversation restored.
8. Clear-history behavior.

## Delivery

The implementation will be committed to
`feature/vizzy-gpt-implementation`, pushed to the existing draft pull request,
rebuilt as `VizzyGPT.sr2-mod`, installed locally, and accepted in Juno before the
pull request is marked ready for review.
