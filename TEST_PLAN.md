# TateScribe Test Plan

## Automated tests

- Natural ordering: timestamp embedded in filename takes precedence, then EXIF, creation time, modification time, and natural filename comparison.
- Geometry: normalized crop coordinates round-trip correctly; 90-degree rotation maps points correctly.
- Reading order: columns sort right-to-left, glyphs sort top-to-bottom, column/page boundaries default to `DirectJoin`, and punctuation alone does not create paragraphs.
- Evidence safety: illustration/caption classifications are excluded only when confirmed; user edits survive re-OCR; duplicate candidates remain review items.
- Storage: create/save/load retains page order, source hash, crop, state, OCR evidence, and revisions.
- Worker protocol: valid JSON Lines is parsed, cancellation terminates the request, malformed output and process exit become retryable errors.
- DOCX: headings map to Word styles, body uses Normal, paragraph indent is paragraph formatting, ruby XML is emitted, and page markers/images/captions are absent. Screenshot boundaries must not create Word page breaks.

## Manual acceptance

Use the eight supplied screenshots when available. Verify thumbnail import/order, crop preview, no UI freeze while the worker runs, review visibility for uncertain text/order, manual correction retention after re-OCR, output opening in Word without Word being required to generate it, and package launch on a clean Windows x64 machine.

## Commands

`scripts/test.ps1` runs C# unit tests and Python protocol tests. `scripts/build.ps1` builds the solution; `scripts/package.ps1` runs tests then publishes self-contained win-x64 output.
