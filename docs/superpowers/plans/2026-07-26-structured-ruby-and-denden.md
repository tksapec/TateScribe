# Structured Ruby and Denden Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add schema-validated ChatGPT ruby review, persistent confirmed ruby, multi-ruby DOCX, deterministic Denden output, and verified non-clipping WPF UI.

**Architecture:** Add pure Core contracts/validation first, schema-versioned SQLite persistence second, deterministic package/export infrastructure third, and thin WPF orchestration last. Keep existing proofreading contracts and DOCX compatibility while new structured document inlines become the authoritative ruby-aware export model.

**Tech Stack:** .NET 8, WPF, Microsoft.Data.Sqlite, System.Text.Json, DocumentFormat.OpenXml, Python unittest.

## Global Constraints

- No ChatGPT API, direct EPUB generation, or ChatGPT-authored DOCX/Markdown.
- Only confirmed ruby reaches final output.
- Preserve current proofreading behavior and old project databases.
- Standard tests and this implementation run must not create `TateScribe-win-x64.zip`.
- Tests use artificial text/images only.

---

### Task 1: Central prompt templates and task types

**Files:**
- Create: `src/TateScribe.Core/ChatGpt/ChatGptPromptTemplates.cs`
- Modify: `src/TateScribe.Infrastructure/Proofreading/ProofreadingPackageExporter.cs`
- Modify: `src/TateScribe.App/ChatGptProofreadingPromptWindow.xaml`
- Modify: `src/TateScribe.App/ChatGptProofreadingPromptWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/ChatGptPromptTemplateTests.cs`

**Interfaces:**
- Produces: `ChatGptTaskType`, `IChatGptPromptTemplateProvider`, `ChatGptPromptTemplateProvider`.

- [ ] Add failing tests proving proofreading requests format 2 but not DOCX, ruby requests JSON only and forbids body changes, and package/UI use provider output.
- [ ] Run the focused tests and confirm failure because the provider does not exist.
- [ ] Implement the provider and inject it into package and prompt UI paths.
- [ ] Re-run focused tests and build the WPF project.

### Task 2: Structured document and ruby contracts

**Files:**
- Create: `src/TateScribe.Core/Documents/StructuredDocument.cs`
- Create: `src/TateScribe.Core/Ruby/RubyContracts.cs`
- Create: `src/TateScribe.Core/Ruby/RubyDocumentComposer.cs`
- Test: `tests/TateScribe.Tests/StructuredDocumentTests.cs`

**Interfaces:**
- Produces: `StructuredDocument`, `StructuredParagraph`, `SourceSpan`, `TextInline`, `RubyInline`, `RubyPolicy`, `RubySource`, `RubyAnnotationStatus`.

- [ ] Add failing tests for multiple ruby, mixed text/ruby, different readings of identical text, exact plain-text preservation, and confirmed-only composition.
- [ ] Verify the focused tests fail for missing contracts.
- [ ] Implement immutable contracts and a composer that rejects overlaps and never duplicates parent text.
- [ ] Re-run focused tests.

### Task 3: Ruby JSON parser and validator

**Files:**
- Create: `src/TateScribe.Core/Ruby/RubyImportValidator.cs`
- Test: `tests/TateScribe.Tests/RubyImportValidatorTests.cs`

**Interfaces:**
- Consumes: Task 2 structured paragraphs and enums.
- Produces: `RubyImportDocument`, `RubyImportPreview`, `RubyImportIssue`, `RubyImportValidator.Validate`.

- [ ] Add failing tests for valid JSON, every identity/hash/range/base/source/confidence failure, duplicates/overlaps, unresolved items, warnings, and UTF-16 surrogate boundaries.
- [ ] Verify failures are caused by the absent validator.
- [ ] Implement strict `System.Text.Json` parsing and non-mutating validation.
- [ ] Re-run focused tests.

### Task 4: Schema version 8 and document/ruby persistence

**Files:**
- Modify: `src/TateScribe.Infrastructure/Storage/SqliteProjectRepository.cs`
- Test: `tests/TateScribe.Tests/ProjectRepositoryTests.cs`
- Test: `tests/TateScribe.Tests/RubyRepositoryTests.cs`

**Interfaces:**
- Produces repository methods to create/load snapshots, create ruby batches, preview/save proposals, confirm/reject/edit annotations, load unresolved items, and mark annotations stale.

- [ ] Add failing old-schema migration, stable paragraph ID, rollback, deduplication, history, and stale tests.
- [ ] Run repository tests and confirm expected failures at schema 6/missing APIs.
- [x] Add transactional version 8 migration and repository methods.
- [ ] Re-run repository and legacy compatibility tests.

### Task 5: Structured snapshot and ruby package services

**Files:**
- Create: `src/TateScribe.App/Services/StructuredDocumentService.cs`
- Create: `src/TateScribe.Infrastructure/Ruby/RubyPackageExporter.cs`
- Create: `src/TateScribe.App/Services/RubyPackageService.cs`
- Test: `tests/TateScribe.Tests/RubyPackageTests.cs`

**Interfaces:**
- Produces directory package files `instructions.md`, `manifest.json`, `confirmed-document.json`, `ruby-candidates.json`, `output-schema.json`, and optional images.

- [ ] Add failing directory-package tests for confirmed-only paragraphs, stable IDs/hashes, policy, OCR candidate evidence, shared prompt, JSON Schema, and unproofread warning data.
- [ ] Verify the tests fail for missing services.
- [ ] Implement snapshot construction and deterministic package serialization.
- [ ] Re-run focused tests.

### Task 6: Ruby import persistence and review UI

**Files:**
- Create: `src/TateScribe.App/Services/RubyImportService.cs`
- Create: `src/TateScribe.App/RubyReviewWindow.xaml`
- Create: `src/TateScribe.App/RubyReviewWindow.xaml.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`
- Test: `tests/TateScribe.Tests/RubyImportServiceTests.cs`

**Interfaces:**
- Consumes Tasks 3–5; produces per-item confirm/reject/edit and unresolved/stale display.

- [ ] Add failing UI contract and DB-nonmutation tests.
- [ ] Verify expected failures.
- [ ] Implement import preview/save and the review window with read-only body text, evidence, image/coordinate navigation, and source-specific bulk selection.
- [ ] Re-run focused tests and WPF build.

### Task 7: Multi-ruby DOCX

**Files:**
- Modify: `src/TateScribe.Core/Export/ExportDocument.cs`
- Modify: `src/TateScribe.Infrastructure/Export/OpenXmlDocumentExporter.cs`
- Modify: `src/TateScribe.App/Services/DocumentExportService.cs`
- Test: `tests/TateScribe.Tests/DocxExportTests.cs`

**Interfaces:**
- Adds a structured export overload while retaining `ExportDocument` compatibility.

- [ ] Add failing tests for multiple ruby, mixed runs, repeated words with different readings, text identity, styles, and OpenXmlValidator.
- [ ] Verify failures against the single-ruby implementation.
- [ ] Implement ordered inline emission and configurable ruby size.
- [ ] Re-run DOCX and legacy export tests.

### Task 8: Deterministic Denden output

**Files:**
- Create: `src/TateScribe.Core/Export/DendenExportOptions.cs`
- Create: `src/TateScribe.Infrastructure/Export/DendenExportService.cs`
- Test: `tests/TateScribe.Tests/DendenExportTests.cs`

**Interfaces:**
- Produces `book.md`, `ddconv.yml`, `default.css`, `README.txt`, and optional safe `ruby.csv`.

- [ ] Add failing tests for ruby syntax, escaping, roles, YAML fields/order, rtl, BOM/LF, deterministic repetition, confirmed-only filtering, and conflicting-reading exclusion from `ruby.csv`.
- [ ] Verify failures because the exporter is absent.
- [ ] Implement fixed-order UTF-8 no-BOM writers and deterministic filenames/content.
- [ ] Re-run focused tests.

### Task 9: Main UI layout and export controls

**Files:**
- Modify: `src/TateScribe.App/MainWindow.xaml`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Create: `src/TateScribe.App/DendenExportWindow.xaml`
- Create: `src/TateScribe.App/DendenExportWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`

**Interfaces:**
- Exposes ruby package/import/review, structured DOCX, Denden metadata/export, and a scrollable sidebar.

- [ ] Add failing source-layout tests requiring a sidebar `ScrollViewer`, named ruby/Denden controls, wrapping labels, and usable dialog minimum sizes.
- [ ] Verify the tests fail against current clipped layout.
- [ ] Implement scrollable grouped controls and event handlers without removing existing actions.
- [ ] Re-run layout tests and build.

### Task 10: Fast standard test/package scripts

**Files:**
- Modify: `tests/TateScribe.Tests/ProofreadingPackageTests.cs`
- Modify: `scripts/test.ps1`
- Modify: `scripts/package.ps1`

- [ ] Convert normal package tests to directories and mark one minimal ZIP test `Category=SlowZip`.
- [ ] Add `test.ps1 -IncludeSlowZip` filtering and `package.ps1 -SkipArchive`.
- [ ] Run standard tests and prove no release ZIP is created.

### Task 11: Documentation and complete verification

**Files:**
- Modify: `README.md`, `SPEC.md`, `ARCHITECTURE.md`, `IMPLEMENTATION_PLAN.md`, `TEST_PLAN.md`, `USER_GUIDE.md`, `CHANGELOG.md`
- Create: `docs/CHATGPT_PROMPTS.md`, `docs/RUBY_JSON_SCHEMA.md`, `docs/DENDEN_EXPORT.md`

- [x] Document the 14-step workflow, schema 8, prompts, JSON, staleness, DOCX, and Denden rules.
- [ ] Run `git diff --check`, Debug/Release builds, `scripts/test.ps1`, and `scripts/package.ps1 -SkipArchive`.
- [ ] Inspect package publish output and prove the release ZIP was not created or replaced.
- [ ] Launch the actual WPF app and inspect the main window, prompt window, ruby review window, proofreading import window, and Denden window at default and minimum practical sizes.
- [ ] Fix every observed clipping/inaccessible-control defect with a failing layout test first, then repeat actual UI inspection.
- [ ] Request read-only code review, address all Critical/Important issues, commit as `feat: add structured ruby and deterministic ebook exports`, push `main`, and verify `HEAD...origin/main` is `0 0`.
