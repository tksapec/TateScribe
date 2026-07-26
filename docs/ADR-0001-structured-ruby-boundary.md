# ADR 0001: Keep ChatGPT outside publication-file generation

Status: accepted

## Decision

ChatGPT returns only TateScribe format 2 proofreading text or version 1 ruby JSON. TateScribe owns the authoritative structured document, user confirmation, DOCX, and Denden Converter output.

OCR ruby candidates, ChatGPT proposals, and user-confirmed ruby are separate concepts and tables. Only confirmed, non-stale annotations are composed into output inlines.

## Consequences

- Publication output is repeatable and testable.
- ChatGPT cannot silently rewrite body text while adding ruby.
- Package/batch/document/paragraph hashes and UTF-16 ranges must be validated.
- Body edits invalidate affected ruby instead of using ambiguous text search.
- Direct EPUB generation remains a separate future decision.
