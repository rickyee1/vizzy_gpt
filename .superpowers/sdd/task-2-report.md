# Task 2 Review-Fix Report

## Changes

- `CanonicalXml` now removes whitespace-only text only when an element has child elements and no significant mixed text. Whitespace is preserved between child elements when the containing element also has non-whitespace text.
- `FindByPath` now accepts only absolute indexed paths with exactly one leading slash, no empty or trailing segments, canonical non-negative indices, and an indexed root segment.
- Added regression coverage for mixed-content whitespace, relative/doubled/trailing paths, malformed indexes, sibling index selection, out-of-range indexes, and a known lowercase UTF-8 SHA-256 value.

## Fixture Integrity

`nested.xml` was recreated from `UserData/FlightPrograms/unsdeady FC 2modes.xml` using a byte-level replacement of only `name="unsdeady FC 2modes"` with `name="NestedFixture"`.

- XML parser validation: source and target roots are non-namespaced `Program`; source name is `unsdeady FC 2modes`; target name is `NestedFixture`.
- The old root-name byte sequence occurred exactly once at offset `52`; the replacement byte sequence occurs exactly once at the same offset.
- Source length: `4054` bytes. Target length: `4049` bytes, matching the five-byte name-length difference.
- Prefix and suffix byte comparisons passed after accounting for that length difference.
- Source SHA-256: `10f1e84228ef6e971bea48c42af9b8742c1525ca2408b154f0238930b34ae8ff`.
- Target SHA-256: `bad12470f17b1cec7695f8fa6deaa4dd7c1e038786b4e6681119ab95d43e5ebe`.

The original user fixture was read only and was not modified.

## Static Checks

- `git diff --check`: completed with no whitespace errors.
- Static review: changes remain C# 9-compatible, preserve ordinal XML/path comparisons, and are limited to the Task 2 review findings.

## Deferred Verification

Per explicit user instruction, no executable tests, build, or non-sandbox escalation was attempted. Therefore there is no RED/GREEN evidence for these regressions and compile/NUnit results remain unknown.

## Concern

Run the focused `CanonicalXmlTests` and the full `powershell -ExecutionPolicy Bypass -File tools/Test-Core.ps1` after the platform quota permits the approved non-sandbox execution.
