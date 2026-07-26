# Denden and Ruby Evidence Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align Denden Converter output with the official format, make illustration export safe and referenced, and correct ruby evidence validation without regressing deterministic DOCX/Denden output.

**Architecture:** Preserve `StructuredDocument` as the shared confirmed-text model. Add a Denden-only ordered block model and image preparation boundary, a shared export preflight result, and schema-v9 ruby candidate evidence fields. Keep legacy SQLite columns and migrate their values forward transactionally.

**Tech Stack:** .NET 8, WPF, C# records, SQLite, OpenCvSharp, xUnit, Open XML.

**Status:** Implemented and verified on 2026-07-26. The task checkboxes below are the original execution checklist and are retained as a development record.

## Global Constraints

- `ddconv.yml` uses `ddconvVersion: 1.0` and official key spelling in a fixed order.
- Denden assets are UTF-8 without BOM, use LF, and are deterministic for identical inputs.
- Only PNG, JPEG, and GIF are emitted; unsupported images are decoded and encoded as PNG.
- A single output image larger than 3 MiB is a fatal preflight error.
- More than 100 output files is a fatal preflight error.
- Illustration output is opt-in, includes only `PageRole.Illustration`, and every copied image is referenced by Markdown.
- `StructuredDocument`, formatVersion 1 ruby JSON, confirmed ruby history, and existing project databases remain compatible.
- SQLite migration advances version 8 to 9 without dropping legacy columns.
- `Proposed`, `Unresolved`, and `Stale` ruby entries are never rendered into final output.
- SlowZip and release ZIP generation remain excluded; package verification uses `scripts/package.ps1 -SkipArchive`.

---

### Task 1: Official Denden metadata and option validation

**Files:**
- Modify: `src/TateScribe.Core/Denden/DendenExportContracts.cs`
- Modify: `src/TateScribe.Infrastructure/Denden/DendenExportService.cs`
- Modify: `src/TateScribe.App/DendenExportWindow.xaml`
- Modify: `src/TateScribe.App/DendenExportWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/RubyPackageAndExportTests.cs`

**Interfaces:**
- Produces: `DendenExportOptions.Validate()` and official `BuildYaml` output.

- [ ] Add tests asserting `ddconvVersion`, `titles[].content`, `creators[].content/role`, official options, range validation, BOM-free LF output, and deterministic bytes.
- [ ] Run the focused tests and confirm failures mention missing official keys or accepted `TcyDigitCount = 1`.
- [ ] Extend `DendenExportOptions` with `SkipCover`, `DisplayLandmarksNav`, and `DisplayIllustrationList`, normalize empty language to `ja`, and reject invalid title, creator, TOC depth, or TCY count.
- [ ] Emit the official keys in a fixed order and update the WPF validation/filter.
- [ ] Run the focused tests until green.

### Task 2: Referenced illustration blocks and safe image preparation

**Files:**
- Modify: `src/TateScribe.Core/Denden/DendenExportContracts.cs`
- Create: `src/TateScribe.Infrastructure/Denden/DendenImageProcessor.cs`
- Modify: `src/TateScribe.Infrastructure/Denden/DendenExportService.cs`
- Modify: `src/TateScribe.App/Services/DocumentExportService.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/RubyPackageAndExportTests.cs`

**Interfaces:**
- Produces: `DendenIllustration`, `DendenContentBlock`, `DendenExportDocument`, `DendenImageProcessor.Prepare`.
- Consumes: ordered `ProjectPage.SortOrder`, paragraph `SourceSpans`, and `BoundaryJoinType`.

- [ ] Add tests for opt-out, referenced root-level images, split chapters, DirectJoin placement adjustment, stable names, format preservation/conversion, size/file-count failures, and cleanup.
- [ ] Run the focused tests and confirm failures reflect missing block/image behavior.
- [ ] Build ordered paragraph/illustration blocks without splitting a joined paragraph; record `IllustrationPlacementAdjusted`.
- [ ] Preserve valid PNG/JPEG/GIF bytes, deterministically convert other decodable formats to PNG, and reject unsupported or oversized output before destination creation.
- [ ] Write Markdown references and images together, omit empty `book.md` in chapter mode, and update README instructions.
- [ ] Run the focused tests until green.

### Task 3: Ruby evidence linking and schema-v9 migration

**Files:**
- Modify: `src/TateScribe.Core/Ruby/RubyPackageContracts.cs`
- Create: `src/TateScribe.Core/Ruby/RubyCandidateLinker.cs`
- Modify: `src/TateScribe.Core/Ruby/RubyOcrCandidateSelector.cs`
- Modify: `src/TateScribe.App/Services/RubyWorkflowService.cs`
- Modify: `src/TateScribe.Infrastructure/Storage/SqliteProjectRepository.cs`
- Test: `tests/TateScribe.Tests/RubyPackageAndExportTests.cs`
- Test: `tests/TateScribe.Tests/ProjectRepositoryTests.cs`

**Interfaces:**
- Produces: `ReadingCandidate`, `BaseTextCandidate`, `LinkConfidence`, and kana-safe `RubyTextNormalizer.NormalizeReading`.

- [ ] Add tests for reading/base separation, vertical coordinate linking, ambiguous null links, returned candidates, and version-8 migration.
- [ ] Run focused tests and confirm failures show old `OcrText`/page-wide body semantics.
- [ ] Link each ruby region only to vertically overlapping nearby body word regions; return null when the best match is ambiguous.
- [ ] Add v9 columns while retaining `ocr_text` and `adjacent_body_text`; backfill old rows with reading candidate and null base/link.
- [ ] Persist/load new fields and run focused tests until green.

### Task 4: Candidate-scoped validation, schema, prompt, and review UI

**Files:**
- Modify: `src/TateScribe.Core/Ruby/RubyContracts.cs`
- Modify: `src/TateScribe.Core/Ruby/RubyImportValidator.cs`
- Modify: `src/TateScribe.Core/ChatGpt/ChatGptPromptTemplates.cs`
- Modify: `src/TateScribe.Infrastructure/Ruby/RubyPackageExporter.cs`
- Modify: `src/TateScribe.App/RubyReviewWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/RubyWorkflowTests.cs`
- Test: `tests/TateScribe.Tests/ChatGptPromptTemplateTests.cs`

**Interfaces:**
- Produces: candidate keys on `RubyValidationIssue` and consistent schema/application requirements.

- [ ] Add tests for kana normalization, reading mismatch, unknown base candidate, evidence marker requirements, unique markers, non-empty evidence, prompt identity instructions, and bulk-confirm exclusions.
- [ ] Run focused tests and confirm expected failures.
- [ ] Attach paragraph/range/annotation identity to every candidate warning; compare normalized reading to reading evidence and base only when linked.
- [ ] Require evidence markers for image/text confirmations in validator and draft-2020-12 schema; keep unknown properties disallowed.
- [ ] Show all matching coordinates and selected-candidate issues; only bulk-confirm warning-free, sufficiently confident image/text candidates.
- [ ] Run focused tests until green.

### Task 5: Shared export preflight

**Files:**
- Create: `src/TateScribe.Core/Export/ExportPreflight.cs`
- Modify: `src/TateScribe.App/Services/DocumentExportService.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/DocxExportTests.cs`
- Test: `tests/TateScribe.Tests/RubyPackageAndExportTests.cs`

**Interfaces:**
- Produces: `ExportPreflightResult` used by both DOCX and Denden handlers.

- [ ] Add tests for unproofread, Other, empty, ruby state counts, fatal Denden image/file issues, and cancellation-before-directory-creation.
- [ ] Run focused tests and confirm the missing shared result causes failures.
- [ ] Inspect included pages and persisted ruby state once, excluding non-confirmed/stale ruby from the prepared structured document.
- [ ] Render a single confirmation summary for each export path and stop before writing when declined or fatal.
- [ ] Run focused tests until green.

### Task 6: Documentation and full verification

**Files:**
- Modify: `README.md`
- Modify: `SPEC.md`
- Modify: `ARCHITECTURE.md`
- Modify: `IMPLEMENTATION_PLAN.md`
- Modify: `TEST_PLAN.md`
- Modify: `USER_GUIDE.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/DENDEN_EXPORT.md`
- Modify: `docs/RUBY_JSON_FORMAT.md`
- Modify: `docs/RUBY_JSON_SCHEMA.md`
- Modify: `docs/CHATGPT_PROMPT_SPEC.md`
- Create: `docs/ADR-0002-denden-assets-and-ruby-evidence.md`

- [ ] Document the official YAML, flat referenced assets, image/file limits, ruby evidence semantics/linking, shared preflight, and no direct EPUB/ZIP generation.
- [ ] Run `git diff --check`.
- [ ] Run `.\scripts\build.ps1`.
- [ ] Run `.\scripts\test.ps1` and confirm SlowZip is excluded.
- [ ] Run `.\scripts\package.ps1 -SkipArchive` and confirm no release ZIP was created or modified.
- [ ] Inspect the final diff and database migration compatibility.
- [ ] Commit with `fix: align denden export and ruby evidence matching`.
- [ ] Push `main` and verify `HEAD` equals `origin/main`.
