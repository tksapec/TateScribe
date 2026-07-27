# ADR 0001: Keep ChatGPT outside publication-file generation

Status: accepted

## Decision

ChatGPT returns only TateScribe format 2 proofreading text or version 1 ruby JSON. TateScribe owns the authoritative structured document, user confirmation, DOCX, and Denden Converter output.

OCR ruby candidates, ChatGPT proposals, and user-confirmed ruby are separate concepts and tables. Only confirmed, non-stale annotations are composed into output inlines.

## Consequences

- Publication output is repeatable and testable.
- Preparing DOCX/Denden is read-only; the snapshot is persisted only after confirmation and successful artifact output.
- Ruby output and preflight select one effective annotation per paragraph/range. Different readings are a review conflict, never an implicit latest-wins choice.
- ChatGPT cannot silently rewrite body text while adding ruby.
- Package/batch/document/paragraph hashes and UTF-16 ranges must be validated.
- Body edits invalidate affected ruby instead of using ambiguous text search.
- Word ruby offset defaults to 3pt within the inclusive 0 through 20 range. Its calculated raise is provisional, so an offset change requires re-export. XML diagnostics cannot replace manual Word visual verification or B/C comparison against Word-saved references.
- Review confirmation is explicit: Ctrl/Shift selects rows, Ctrl+Enter confirms selected rows, and rejection is button-only. Image/text bulk confirmation stays separate and reports examined, newly confirmed, already confirmed, wrong-source, excluded, and per-reason exclusion categories; the review summary includes selected count.
- This decision uses existing persisted state: no SQLite schema migration and no Release ZIP.
- Direct EPUB generation remains a separate future decision.
