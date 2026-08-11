# Vizzy Model Skill Design

## Summary

VizzyGPT will provide Modify requests with a bundled Vizzy programming guide
and a machine-derived catalog of the exact nodes available in the running game
version. The guide teaches model-level semantics; the catalog prevents the
model from inventing XML element names, styles, attributes, and child shapes.

The same knowledge will be supplied to the single automatic repair attempt.
When validation identifies an unknown node or style, the repair context will
also include relevant valid templates instead of repeating only the rejection
message.

## Goals

- Teach the model the core Vizzy execution model: events, instructions,
  expressions, variables, cooperative loops, flight inputs, and safe update
  cadence.
- Make the installed game's toolbox resource the authority for generatable XML
  nodes.
- Include exact element, `style`, attribute, and immediate child templates in
  Modify context.
- Prevent common aliases such as `SetThrottle` and `set-throttle` when the
  actual node is `SetInput style="set-input" input="throttle"`.
- Give automatic repair enough valid catalog information to correct an invalid
  node in one retry.
- Preserve local validation, preview-before-apply, protected roots, stale-hash
  checks, undo, and flight pending changes.

## Non-Goals

- Do not scrape the website at game runtime.
- Do not treat community tutorials as an authoritative XML schema.
- Do not fine-tune or retrain the configured API model.
- Do not allow generated XML that is absent from the local node catalog.
- Do not include hidden reasoning or chain-of-thought instructions.
- Do not implement a complete autonomous landing controller as part of this
  change.

## Knowledge Sources

The bundled semantic guide will contain concise, original summaries of:

- Vizzy events, sequential instructions, expressions, variables, and custom
  instructions.
- Cooperative execution and the need for a `Wait 0`-style yield in persistent
  loops.
- Craft inputs such as throttle, pitch, roll, yaw, sliders, and translation.
- Safety rules for preview-only generation and protected program containers.

The guide is informed by official Jundroo material but is maintained as source
text in this repository. It will not download or quote website content at
runtime.

The packaged `VizzyToolbox.xml`, loaded through the existing runtime resource
path, is the authoritative structural source. This keeps the skill aligned
with the Juno version that supplies the toolbox.

## Catalog Representation

A new core formatter will turn toolbox XML into a bounded model-facing
reference. Each node template records:

- Category name.
- XML element name.
- Exact `style` value.
- Other fixed attributes from the toolbox template.
- Immediate child template structure, recursively bounded to preserve useful
  expression slots.

Templates will use canonical XML and deterministic ordering so requests and
tests remain stable. Style declarations and UI-only toolbox metadata will not
be emitted. Duplicate templates will be collapsed.

The full packaged toolbox is small enough to parse locally, but the request
reference must remain bounded. The formatter will prioritize concrete category
nodes and omit redundant style-definition metadata. If the formatted reference
exceeds its fixed character budget, it will end at a complete template and
state that the list was truncated; local validation remains the final
authority.

## Request Flow

For Modify mode, the controller will build context in this order:

1. Program base hash.
2. Current editor program context.
3. Flight context when applicable.
4. Bundled Vizzy semantic guide.
5. Current node catalog reference.
6. Explicit instruction to use only catalog-listed element and style
   combinations.

Ask mode may use the semantic guide when explaining Vizzy, but it does not need
the full structural catalog because Ask cannot produce an applicable patch.

Catalog construction will occur once per Modify request and the same immutable
reference will be reused by validation repair. This avoids inconsistencies
between the original and repair calls.

## Automatic Repair

The repair request retains the original source program and base hash. In
addition to the validation code, path, and sanitized message, it will include:

- A reminder that guessed element and style names are forbidden.
- Relevant valid templates selected by matching the invalid element, style,
  path, and error text against catalog tokens.
- The complete bounded catalog reference already supplied to the first call
  when no useful narrow match is available.

For the observed error, the repair information must make the valid throttle
template discoverable:

```xml
<SetInput style="set-input" input="throttle">
  <Constant number="0" />
</SetInput>
```

Exactly one repair attempt remains allowed. Failure after repair still ends
without mutating the editor.

## Error Handling

If the toolbox resource cannot be loaded or parsed, Modify will fail before
contacting the API with an actionable local error. The controller will never
silently fall back to an invented or stale catalog.

Catalog formatting errors will not expose complete local paths, request
headers, API keys, or raw provider bodies. Existing diagnostic sanitization
continues to apply.

## Testing

Core tests will verify deterministic catalog formatting, exact throttle
template output, duplicate removal, escaping, and the character budget.

Controller tests will verify:

- Initial Modify context contains the semantic guide and valid throttle
  template.
- Ask context does not carry the large catalog.
- Unknown-style repair includes the correct valid template.
- A failed repair still leaves the editor unchanged.

Existing core and Unity EditMode suites must remain green. Live acceptance will
send a Modify request for throttle control and confirm that Preview uses
`SetInput style="set-input"` without an unknown-style error.

## Distribution

The semantic guide and catalog formatter ship inside `VizzyGPT.sr2-mod`.
Community users therefore receive the same behavior without installing a
separate Codex skill or downloading documentation. A rebuilt package will be
attached to the existing GitHub release only after automated and live
acceptance pass.
