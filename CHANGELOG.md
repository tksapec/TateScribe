# Changelog

## Unreleased

- Added proofreading formatVersion 2 with strict text/report ranges, safe Markdown-fence handling, boundary joins, and formatVersion 1 compatibility.
- Added Confirmed/Manual/Suggested/RawPaddle export priority and manifest provenance.
- Added export snapshot staleness checks, independent OCR status, Stale proofreading state, Manual/Confirmed history, and page diff review.
- Post-confirmation Manual edits now become active without deleting Confirmed history; strict joins, bounded inline diff, blank paragraphs, cancellation state, and Ruby review status are covered.
- Reused one Python worker and one PaddleOCR engine per model configuration; persisted structured OCR failures.
- Added EXIF ordering, persistent printed-page ReviewItems, editable RubyCandidate roles, history/diagnostic UI, and requested service boundaries.
- Included text-bearing Other pages after confirmation and added explicit validated DOCX styles.

- Added non-destructive PaddleOCR/Tesseract persistence, merge proposals, OCR run history, confirmed text, and proofreading states.
- Added versioned offline proofreading ZIP/folder export and validated ChatGPT result import.
- Added four-edge crop controls, display profiles, page roles, printed-page metadata, structured DOCX output, and optional chapter page breaks.
