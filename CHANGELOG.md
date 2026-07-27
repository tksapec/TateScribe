# Changelog

## Unreleased

- Added separate ChatGPT text-proofreading and ruby-annotation prompts from one provider.
- Added folder-first ruby review packages and strict version 1 JSON validation.
- Added SQLite schema version 8 for structured document snapshots, provenance spans, stable paragraphs, ruby batches/evidence, proposals, history, unresolved evidence, and original automatic OCR roles.
- Added individual ruby review, separate image/text evidence bulk confirmation, unresolved review, source-image/coordinate inspection, and stale display.
- Added schema-valid multi-ruby DOCX output and deterministic Denden Converter folders.
- Aligned `ddconv.yml` with official version 1.0 keys and validation.
- Added opt-in, flat, Markdown-referenced illustrations with deterministic PNG conversion, 3 MiB image limits, and a 100-file limit.
- Added supported-image integrity checks, official illustration-list figure markup, and fatal Denden validation in the shared export preflight.
- Added SQLite schema version 9 with separate ruby reading/parent candidates, coordinate link confidence, and v8 backfill.
- Added kana-safe ruby evidence matching, candidate-scoped warnings, schema/prompt parity, and warning-aware bulk confirmation.
- Added a shared DOCX/Denden preflight summary; non-confirmed and stale ruby remain excluded.
- Hardened Denden uploads with a root instruction README and upload-only assets, semantic-safe Markdown escaping, an exact 100-file upload boundary, and rotated/cropped illustration PNG preparation.
- Made the main sidebar scrollable and widened/labeled all four crop-percentage inputs.
- Excluded the minimal `SlowZip` test from standard test runs and added `package.ps1 -SkipArchive`.
- Added resumable OCR planning that skips Completed/ReviewRequired pages, retries persisted incomplete states, preserves per-page successes across cancellation/failure, and retains diagnostic failure history; full re-OCR remains an explicit operation.
- Hardened ImageConfirmed against paragraph source spans and OCR reading/parent/side evidence with centralized 0.70 annotation, 0.70 OCR, and 0.60 link thresholds.
- Revalidated edited and freshly identified ruby imports so candidate warnings bind by AnnotationId, block save on errors, and require acknowledgement for newly warned Confirmed items.
- Added read-only ruby batch history with latest-annotated initial selection, status/unresolved counts, historical batch opening, and current-document staleness.
- Kept DOCX and Denden preparation read-only until confirmation and successful artifact output; snapshot deduplication now occurs only after output succeeds.
- Made ruby preflight and DOCX composition use one effective annotation per paragraph/range, deduplicate repeated unresolved items, and surface conflicting readings instead of silently choosing one.
