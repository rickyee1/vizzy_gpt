# VizzyGPT System CJK Font Fallback Design

## Goal

Allow Chinese text to render in VizzyGPT's prompt input and model-generated
content without bundling a font or translating the static interface.

## Scope

The fallback applies to:

- the prompt input text and its placeholder;
- the conversation transcript;
- dynamic status and diagnostic text;
- preview summary, change lines, and warnings.

Static labels and buttons continue to use the game's existing font and English
copy. This change does not alter input handling, API payloads, or saved data.

## Font Resolution

A single runtime font service discovers an installed CJK-capable system font.
It tries a deterministic list of common Windows font family names, with
Microsoft YaHei first, followed by other Chinese families available on standard
Windows installations. It creates one dynamic TextMesh Pro font asset and
reuses it for the lifetime of the mod.

The service verifies that the selected font can supply representative Simplified
Chinese characters before exposing the asset. A font name existing on the
system is not sufficient by itself.

The implementation remains isolated from UI controllers. Controllers receive or
resolve the shared font service and apply the returned font asset only to their
dynamic text components. For a TMP input field, both the visible text component
and placeholder are updated.

## Failure Behavior

If no supported system font is installed, VizzyGPT keeps the game's original
font instead of failing to open the panel. It writes one non-secret diagnostic
to the Unity log explaining that a CJK system font was not found. Repeated panel
opens do not repeat font discovery or flood the log.

Because no font is bundled, community users without a compatible installed
Chinese font are not guaranteed Chinese rendering. This is an accepted tradeoff
of the selected system-font approach.

## Testing

EditMode tests cover:

- deterministic selection of the first installed font that contains the probe
  characters;
- rejection of installed fonts without the required glyphs;
- cached reuse of the resolved TMP font asset;
- safe no-font behavior;
- application to the prompt input, transcript, status, and preview text
  components without modifying static labels.

The release check includes a live Windows test that types Chinese into the
prompt, receives a Chinese response, and opens a preview containing Chinese
summary text.
