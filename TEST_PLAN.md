# TateScribe Test Plan

## Automated tests

- Natural ordering: timestamp embedded in filename takes precedence, then EXIF, creation time, modification time, and natural filename comparison.
- Geometry: normalized crop coordinates round-trip correctly; 90-degree rotation maps points correctly.
- Reading order: columns sort right-to-left, glyphs sort top-to-bottom, column/page boundaries default to `DirectJoin`, and punctuation alone does not create paragraphs.
- Evidence safety: illustration/caption classifications are excluded only when confirmed; user edits survive re-OCR; duplicate candidates remain review items.
- Storage: create/save/load retains page order, source hash, crop, state, OCR evidence, and revisions.
- Worker protocol: valid JSON Lines is parsed, cancellation terminates the request, malformed output and process exit become retryable errors.
- DOCX: headings map to Word styles, body uses Normal, paragraph indent is paragraph formatting, ruby XML is emitted, and page markers/images/captions are absent. Screenshot boundaries must not create Word page breaks.
- Proofreading exchange: raw Paddle coordinates survive Tesseract supplementation; packages include manifest/instructions/review list/stable images; mismatched, missing, duplicate, reordered, malformed, and extreme imports cannot silently overwrite text; confirmed text wins in DOCX output.

## Manual acceptance

Use local reflow and fixed-page screenshot samples; do not commit them. Verify 4-edge crop preview, display-profile/page-role settings, OCR evidence retention, 10-page package alignment, original/cropped image mapping, import validation, confirmed-text persistence, excluded headers/page numbers, and Word output without screenshot-boundary breaks.

## Commands

## Hardened workflow coverage

Automated coverage verifies source priority and manifest provenance; post-confirmation Manual activation; format 2 marker validation including mandatory joins/report isolation/whitespace/round-trip; format 1 compatibility; export staleness; Manual/Confirmed history; re-OCR and cancellation state retention; bounded diff counts and partial acceptance; one-time Paddle initialization; structured OCR failures; EXIF ordering/fallback; printed-number validation; RubyCandidate status/overrides; Other page inclusion; chapter number/title handling; boundary joins and intentional blank paragraphs; StylesPart contents; and OpenXmlValidator output.

Artificial byte arrays are used for EXIF images. No local book images or OCR body text are test fixtures.

Manual acceptance additionally checks the fixed-size WPF layout, candidate rectangles over a real imported page, page navigation from the diff window, cancellation after at least one completed OCR page, opening an existing project database, and generated DOCX appearance in Word or a compatible renderer.
`scripts/test.ps1` runs C# unit tests and Python protocol tests. `scripts/build.ps1` builds the solution; `scripts/package.ps1` runs tests then publishes self-contained win-x64 output.
