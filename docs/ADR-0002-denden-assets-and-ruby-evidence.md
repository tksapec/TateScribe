# ADR-0002: Flat Denden assets and separate ruby evidence

## Decision

Denden export uses a dedicated ordered block model over the unchanged `StructuredDocument`. Explicit illustration pages become stable assets in `upload/` referenced by Markdown; joined paragraphs are not split. The root `README.txt` is instructions only and is never an upload asset. A deterministic image boundary preserves full-crop/unrotated PNG/JPEG/GIF, converts transformed and other decodable formats to PNG, and rejects images over 3 MiB or more than 100 upload files.

Ruby OCR evidence stores the recognized reading separately from an optional coordinate-linked parent-text candidate. Schema version 9 adds these fields and link confidence while retaining the legacy columns and backfilling version-8 rows.

ImageConfirmed is accepted as bulk-confirmable evidence only when the annotation range intersects a paragraph source span for the cited page and the same-page OCR candidate matches normalized reading, exact parent text, ruby-side role, OCR confidence at least 0.70, and link confidence at least 0.60. Annotation confidence must be at least 0.70. TextConfirmed is not source-page restricted. Validation issues bind by AnnotationId whenever available, with paragraph/range fallback only for ID-less imports.

Saved ruby history is a read-only aggregate over existing schema-v9 data. The UI selects the latest batch that contains annotations rather than assuming the latest exported package was imported.

## Rationale

`StructuredDocument` remains the shared, text-authoritative boundary for DOCX and Denden. Export-only page assets need page order but must not contaminate body text. OCR text from a ruby region is normally a reading, so comparing it directly with parent text creates false warnings and unsafe confirmation. Source-span ownership prevents evidence from a same-looking occurrence on another page, while explicit thresholds allow conservative bulk action without removing individual visual confirmation.

## Consequences

Only explicitly selected illustrations are emitted, and every copied image is referenced. Ambiguous parent links, low confidence, source-page mismatch, and candidates returned to Body remain warnings that block bulk confirmation. Freshly assigned annotation IDs require immediate revalidation so the review UI does not lose initial warnings. Batch history adds no migration or mutation. DOCX and Denden share preflight state counts. TateScribe continues to generate final DOCX and converter inputs deterministically and does not ask ChatGPT to create DOCX/EPUB or create EPUB/ZIP itself.
