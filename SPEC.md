# TateScribe specification

TateScribe is an offline Windows application that turns Japanese vertical-book screenshots into reviewed text and deterministic publication artifacts.

## ChatGPT-assisted work

- `TextProofreading` compares OCR text with package images and returns TateScribe format 2 structured text. It never adds ruby or returns DOCX/EPUB/Markdown.
- `RubyAnnotation` examines a frozen confirmed document and returns JSON conforming to `output-schema.json`. It never changes or reproduces the body.
- The UI and both package types obtain instructions from `IChatGptPromptTemplateProvider`.
- No ChatGPT API integration is included.

## OCR execution

`ResumeIncomplete` processes only included pages whose persisted OCR status is `NotProcessed`, `Failed`, or leftover `Processing`, in page order. It skips `Completed`, `ReviewRequired`, and excluded pages. Successful page results are committed as each page finishes; cancellation does not discard earlier successes. A page-local failure records its stage and diagnostic details, marks that page Failed, and does not stop later pages. Failure rows remain diagnostic history after a later successful retry.

`ReprocessAll` is a separate explicit operation that targets every page and warns that machine-owned OCR results will be refreshed. Re-OCR never deletes ManualText or ConfirmedText, but an OCR update can make reviewed state stale.

## Ruby

`RubyPolicy` is `PreserveOriginalOnly` by default. OCR `RubyCandidate` regions are evidence; imported proposals remain separate until the user confirms them. Only `Confirmed` annotations reach DOCX or Denden output. Body changes make the associated batch and annotations stale.

`RubyOcrCandidate.ReadingCandidate` is the OCR text from the ruby region. `BaseTextCandidate` is a separate, optional parent-text hypothesis linked from vertically overlapping nearby Body regions. Reading comparison normalizes width, katakana/hiragana, and whitespace without dictionary guessing. An ambiguous coordinate link remains null and cannot be bulk-confirmed.

`ImageConfirmed` evidence is restricted to page markers whose paragraph source spans intersect the annotation's UTF-16 range. The reading and linked parent text must match a candidate that remains on the ruby side. Bulk confirmation requires annotation confidence at least 0.70, OCR candidate confidence at least 0.70, link confidence at least 0.60, non-empty evidence, and no candidate warning. `TextConfirmed` can cite reading evidence on another page and is not source-page restricted. Individual visual confirmation remains available.

The review window revalidates edited ranges, readings, and statuses before saving. Errors keep it open; a new warning on a Confirmed item requires explicit acknowledgement. Saved-batch history initially selects the newest batch with annotations, permits opening older annotated batches, and displays status counts plus whether each batch belongs to the current document.

The authoritative model is a `StructuredDocument` with stable persisted paragraph IDs, roles, ordered text/ruby inlines, text hashes, and source spans. Applying ruby must not change the plain body text.

## Output

TateScribe generates schema-valid multi-ruby DOCX and deterministic Denden Converter folders. Denden text is UTF-8 without BOM with LF line endings and fixed ordering. EPUB generation is outside this phase.

`ddconv.yml` follows version 1.0 with structured title/creator entries and official option names. A Denden destination contains an instruction-only `README.txt` plus an `upload/` folder; users select every file in `upload/`, never the README. Illustration output is opt-in, ordered by page, and referenced from Markdown. Full-crop, unrotated PNG/JPEG/GIF are preserved; transformed illustrations and other decodable formats become PNG. Images over 3 MiB and more than 100 files inside `upload/` are rejected before output creation. DOCX and Denden use the same preflight counts and never render Proposed, Unresolved, or Stale ruby.

Standard verification excludes `SlowZip` and uses the no-archive package path. A Release ZIP is created only by an explicit archive operation, never by normal build/test verification.

Compatibility with existing project databases, proofreading format 1/2, legacy `ExportDocument`, page roles, boundary joins, and existing OCR/manual/confirmed layers is required.
