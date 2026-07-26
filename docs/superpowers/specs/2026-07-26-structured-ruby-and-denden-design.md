# Structured Ruby and Denden Export Design

## Goal

Add a two-stage ChatGPT workflow to TateScribe: text proofreading returns format 2 text, while ruby review returns schema-validated JSON. TateScribe remains the sole deterministic producer of DOCX and Denden Converter input files.

## Constraints

- Do not connect directly to the ChatGPT API.
- Do not ask ChatGPT to edit DOCX, generate EPUB, or freely rewrite Markdown.
- Preserve format 1 import and the existing format 2 proofreading workflow.
- Preserve existing `project.db` contents and migrate transactionally.
- Keep OCR `RubyCandidate`, ruby proposals, and confirmed ruby as separate concepts.
- Only `Confirmed` ruby annotations may reach DOCX or Denden output.
- Do not create a release ZIP during this work.
- Use artificial text and images in tests.

## Architecture

### Prompt source

`IChatGptPromptTemplateProvider` owns both `TextProofreading` and `RubyAnnotation` templates. The prompt window and package exporters receive the same provider so instructions cannot drift.

### Structured document

`StructuredDocument` contains `StructuredParagraph` values. Each paragraph has a persistent GUID, role, plain text, text hash, source spans, and an ordered list of `TextInline` and `RubyInline` values. Reconstructing the plain text from inlines must produce the original paragraph exactly.

Paragraph GUIDs are stored in SQLite. When a new snapshot is built, the repository matches a paragraph to the most recent paragraph with the same logical source anchor (page identity, source ordinal, and role). Text changes update the hash without replacing the logical ID. Split, merged, deleted, or structurally changed paragraphs do not receive blind text-search reassignment.

### Ruby lifecycle

OCR words classified as `RubyCandidate` remain coordinate evidence only. Ruby JSON imports create `Proposed` annotations and unresolved items. The review UI allows per-item edits, confirmation, rejection, and evidence inspection. A paragraph/body change makes affected annotations `Stale`. Exporters read only `Confirmed`.

### Persistence

Schema version 8 adds document snapshots, paragraphs, source spans, ruby batches, annotations, annotation/unresolved evidence pages, unresolved items, and original automatic OCR roles. Migration runs in the existing initialization transaction and uses explicit column/table checks. Old projects open without losing OCR, Manual, Confirmed, or review data.

### Import validation

The validator checks format, project, batch, document hash, paragraph identity/hash, UTF-16 range boundaries, exact `baseText`, reading/source/confidence, duplicate and overlapping ranges, evidence pages, stale proofreading state, and post-export body changes. Errors prevent every DB write. Suggestions, low confidence, non-kana readings, and conflicting readings are warnings.

### Deterministic outputs

The DOCX exporter writes ordered text/ruby inlines and retains existing styles. Denden output writes UTF-8 without BOM and LF newlines with fixed file/property ordering. It escapes markup-sensitive characters, uses inline `{base|reading}` ruby, and emits only explicitly approved global single-reading entries to `ruby.csv`.

### UI

The main left navigation becomes vertically scrollable at the 1080×640 default and 800×480 minimum sizes. It adds ruby package/export/import/review and Denden actions without hiding existing crop, metadata, range, and page controls.

The prompt window adds task selection, dynamic help, reset, copy, and close controls. The ruby review window provides proposed/unresolved/stale lists, editable range/reading, evidence, source image navigation, and source-specific bulk selection. Body text is read-only.

## Verification

- Test prompt identity, JSON validation, DB rollback/migration/history/staleness, structured inlines, DOCX validation, Denden determinism, and package directory contents.
- Mark the single small ZIP-specific proofreading test `SlowZip`; standard test runs exclude it.
- Run Debug and Release builds, standard .NET/Python tests, and `package.ps1 -SkipArchive`.
- Launch the built WPF app and inspect the main window and all new dialogs at default and minimum practical sizes for clipping, inaccessible controls, or truncated labels.
