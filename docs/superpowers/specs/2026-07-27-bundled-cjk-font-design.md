# Bundled CJK Font Design

## Goal

Render arbitrary Simplified Chinese prompt, response, status, and preview text
inside the packaged Juno mod without depending on operating-system fonts.

## Root Cause

Juno's player build enumerates Windows font families, but Unity cannot create a
usable TextMesh Pro face from those operating-system fonts. The player log shows
every attempted family failing with `Unable to load font face`, followed by the
VizzyGPT no-font warning. The existing system-font provider therefore cannot be
made reliable for community packages.

## Font And License

Bundle the regular Simplified Chinese face from Noto Sans CJK under the SIL Open
Font License 1.1. Include the upstream license and attribution alongside the font
in the Unity project and in the built mod. Do not use or redistribute a Windows
system font.

## Runtime Architecture

Store the source font under a VizzyGPT `Resources` path with font data included.
At startup, load that packaged `UnityEngine.Font` and create one dynamic,
multi-atlas `TMP_FontAsset`. The dynamic atlas adds glyphs on demand, so model
responses are not limited to a predefined Chinese character subset.

Reuse the existing `CjkTextFontApplicator` and keep its target scope unchanged:

- prompt input text and placeholder;
- transcript and status text;
- preview summary, added, changed, removed, and warning text.

Static English titles, labels, toggles, and buttons continue using the game's
font. The created TMP asset is cached for the behavior lifetime and released on
destroy. The packaged source font is owned by Unity's resource system and is not
destroyed by VizzyGPT.

## Failure Handling

If the bundled font resource is missing or TMP asset creation fails, the panel
still opens with its original font and writes one non-secret warning. Reopening
the panel does not repeat loading or warning.

## Testing And Acceptance

Automated EditMode tests cover successful bundled-font resolution, cached reuse,
missing-resource fallback, disposal, dynamic-text application, and preservation
of static UI fonts. The release package must include the font and OFL text.

Live acceptance in Juno requires:

1. Chinese text is visible while typing in the prompt field.
2. A Chinese API response renders without missing-glyph boxes.
3. Chinese preview summary and change descriptions render correctly.
4. Closing and reopening the panel does not duplicate warnings.
5. `Player.log` contains no bundled-font load failure.

## Distribution Impact

The mod package becomes materially larger because the full source font is
included. This is accepted in exchange for deterministic Chinese rendering on
community installations.
