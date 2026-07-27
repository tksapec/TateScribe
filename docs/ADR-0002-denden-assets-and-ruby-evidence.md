# ADR-0002: Flat Denden assets and separate ruby evidence

## Decision

Denden export uses a dedicated ordered block model over the unchanged `StructuredDocument`. Explicit illustration pages become stable assets in `upload/` referenced by Markdown; joined paragraphs are not split. The root `README.txt` is instructions only and is never an upload asset. A deterministic image boundary preserves full-crop/unrotated PNG/JPEG/GIF, converts transformed and other decodable formats to PNG, and rejects images over 3 MiB or more than 100 upload files.

Ruby OCR evidence stores the recognized reading separately from an optional coordinate-linked parent-text candidate. Schema version 9 adds these fields and link confidence while retaining the legacy columns and backfilling version-8 rows.

## Rationale

`StructuredDocument` remains the shared, text-authoritative boundary for DOCX and Denden. Export-only page assets need page order but must not contaminate body text. OCR text from a ruby region is normally a reading, so comparing it directly with parent text creates false warnings and unsafe confirmation.

## Consequences

Only explicitly selected illustrations are emitted, and every copied image is referenced. Ambiguous parent links remain null and require review. DOCX and Denden share preflight state counts. TateScribe continues to generate final DOCX and converter inputs deterministically and does not ask ChatGPT to create DOCX/EPUB or create EPUB/ZIP itself.
