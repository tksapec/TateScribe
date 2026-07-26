# Proofreading Workflow Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve non-destructive OCR data while making proofreading round-trips, OCR state, validation, and DOCX export safe for production use.

**Architecture:** Add pure Core models/services first, then transactional SQLite persistence and package/worker/export infrastructure, and finally thin WPF orchestration and documentation. Every behavior change starts with a focused failing test and preserves format 1 and old database compatibility.

**Tech Stack:** .NET 8, WPF, Microsoft.Data.Sqlite, DocumentFormat.OpenXml, Python 3 unittest, PaddleOCR, Tesseract.

## Global Constraints

- Do not add direct ChatGPT API integration.
- Preserve Paddle coordinates and separate Paddle, Tesseract, Suggested, Manual, and Confirmed data.
- Preserve existing project databases and formatVersion 1 import.
- Do not commit local book images or OCR text.
- Do not insert illustration images into DOCX.
- Do not create screenshot-boundary page breaks.

---

### Task 1: Package text selection and format 2

**Files:**
- Modify: `src/TateScribe.Core/Projects/PageTextState.cs`
- Modify: `src/TateScribe.Core/Proofreading/ProofreadingContracts.cs`
- Modify: `src/TateScribe.Core/Proofreading/ProofreadingImportParser.cs`
- Modify: `src/TateScribe.Infrastructure/Proofreading/ProofreadingPackageExporter.cs`
- Test: `tests/TateScribe.Tests/ProofreadingPackageTests.cs`

**Interfaces:**
- Produces: `PageTextSelection`, `BoundaryJoinType`, strict format 2 parsing, format 1 fallback.

- [x] Add failing tests for Confirmed/Manual/Suggested/RawPaddle priority and manifest `textSource`.
- [x] Run `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj --filter ProofreadingPackageTests` and verify the new tests fail for the expected old behavior.
- [x] Add failing parser tests for report exclusion, missing/duplicate/nested markers, outside text, code fences, whitespace preservation, joins, and format 1 compatibility.
- [x] Implement format 2 exporter/parser and raw Paddle fallback without trimming body content.
- [x] Re-run focused tests and refactor only after they pass.

### Task 2: Snapshot staleness, versions, and status separation

**Files:**
- Modify: `src/TateScribe.Core/Projects/ProjectPage.cs`
- Create: `src/TateScribe.Core/Proofreading/ProofreadingDiff.cs`
- Modify: `src/TateScribe.Infrastructure/Storage/SqliteProjectRepository.cs`
- Test: `tests/TateScribe.Tests/ProjectRepositoryTests.cs`
- Test: `tests/TateScribe.Tests/ProofreadingPackageTests.cs`

**Interfaces:**
- Produces: `OcrStatus`, `ProofreadingStatus.Stale`, page text version reads/restores, export snapshot comparison, diff metrics.

- [x] Add failing repository tests for Manual/Confirmed history deduplication and old-version retrieval.
- [x] Add failing tests proving re-OCR preserves Manual/Confirmed and marks a confirmed baseline stale.
- [x] Add failing tests for unchanged snapshot, Manual/OCR warning, source hash error, excluded/deleted error, and order/role/crop warnings.
- [x] Add failing pure tests for insertion/deletion/replacement/paragraph diff counts.
- [x] Implement schema-versioned transactional migrations and new repository APIs.
- [x] Run the repository/package test groups and confirm all new and existing tests pass.

### Task 3: OCR worker caching and failure records

**Files:**
- Modify: `ocr-worker/worker.py`
- Modify: `ocr-worker/tests/test_worker.py`
- Modify: `src/TateScribe.Core/Ocr/OcrContracts.cs`
- Create: `src/TateScribe.Core/Ocr/OcrFailure.cs`
- Create: `src/TateScribe.App/Services/OcrOrchestrationService.cs`
- Modify: `src/TateScribe.Infrastructure/Storage/SqliteProjectRepository.cs`
- Test: `tests/TateScribe.Tests/JsonLinesOcrWorkerTests.cs`
- Test: `tests/TateScribe.Tests/ProjectRepositoryTests.cs`

**Interfaces:**
- Produces: cached `get_paddle_engine`, structured worker errors, persisted `OcrFailure`, per-page continuation.

- [x] Add Python tests proving one initialization per model configuration and no Paddle initialization for Tesseract.
- [x] Add .NET tests for failure stages, cancellation distinction, persistence, and partial-batch continuation.
- [x] Implement cache/error protocol and orchestration service.
- [x] Run Python worker tests and focused .NET OCR tests.

### Task 4: Image metadata and page validation

**Files:**
- Modify: `src/TateScribe.Infrastructure/Import/ImageImporter.cs`
- Create: `src/TateScribe.Core/Projects/PageValidationService.cs`
- Modify: `src/TateScribe.Infrastructure/Storage/SqliteProjectRepository.cs`
- Test: `tests/TateScribe.Tests/PageOrderingTests.cs`
- Create: `tests/TateScribe.Tests/PageValidationServiceTests.cs`

**Interfaces:**
- Produces: safe EXIF timestamp extraction and persistent validation ReviewItems.

- [x] Add artificial JPEG metadata tests for filename-over-EXIF, DateTimeOriginal, DateTimeDigitized, fallback, and broken metadata.
- [x] Add validator tests for duplicate, reversal, gap, nonnumeric, and FixedPageVertical scope.
- [x] Implement metadata fallback and validator persistence.
- [x] Run ordering, importer, validator, and repository tests.

### Task 5: Ruby candidate review

**Files:**
- Modify: `src/TateScribe.Core/Layout/VerticalTextReconstruction.cs`
- Modify: `src/TateScribe.Infrastructure/Storage/SqliteProjectRepository.cs`
- Create: `src/TateScribe.App/RubyCandidateWindow.xaml`
- Create: `src/TateScribe.App/RubyCandidateWindow.xaml.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/VerticalTextReconstructionTests.cs`
- Test: `tests/TateScribe.Tests/ProjectRepositoryTests.cs`

**Interfaces:**
- Produces: persisted role/included overrides and combined package ReviewItems.

- [x] Add failing tests for RubyCandidate-to-Body, included toggle, redisplay persistence, and re-OCR preservation.
- [x] Implement repository APIs, reconstruction consumption, candidate UI, and merged review-items export.
- [x] Run reconstruction/repository/package tests.

### Task 6: Document assembly and explicit styles

**Files:**
- Modify: `src/TateScribe.Core/Export/ExportDocument.cs`
- Modify: `src/TateScribe.Core/Export/BookDocumentAssembler.cs`
- Modify: `src/TateScribe.Infrastructure/Export/OpenXmlDocumentExporter.cs`
- Create: `src/TateScribe.App/Services/DocumentExportService.cs`
- Test: `tests/TateScribe.Tests/BookDocumentAssemblerTests.cs`
- Test: `tests/TateScribe.Tests/DocxExportTests.cs`

**Interfaces:**
- Produces: boundary-aware page assembly, chapter-number/title structure, Other inclusion warnings, explicit Open XML styles.

- [x] Add failing tests for all join types, leading spaces, blank-line preservation, multi-line chapter headings, Other inclusion, and no image-page blanks.
- [x] Add failing tests for StylesPart, six styles, indentation/alignment/page-break settings, and OpenXmlValidator.
- [x] Implement assembler, export selection service, and style definitions.
- [x] Run assembler, DOCX, and end-to-end tests.

### Task 7: Import diff UI and thin services

**Files:**
- Create: `src/TateScribe.App/Services/ProofreadingPackageService.cs`
- Create: `src/TateScribe.App/Services/ProofreadingImportService.cs`
- Modify: `src/TateScribe.App/ProofreadingImportWindow.xaml`
- Modify: `src/TateScribe.App/ProofreadingImportWindow.xaml.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Test: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`

**Interfaces:**
- Produces: before/after and inline diff display, per-page accept/hold, all/none, warning filter, accepted-count reporting, navigation.

- [x] Add failing UI contract tests for controls, accepted marker count, error-page rejection, and warning filter.
- [x] Implement services and bind candidates without changing DB until dialog acceptance.
- [x] Route validation and OCR/export/import/document handlers through the five requested services.
- [x] Build the WPF app and run layout tests.

### Task 8: Documentation and complete verification

**Files:**
- Modify: `README.md`
- Modify: `SPEC.md`
- Modify: `ARCHITECTURE.md`
- Modify: `IMPLEMENTATION_PLAN.md`
- Modify: `TEST_PLAN.md`
- Modify: `USER_GUIDE.md`
- Modify: `CHANGELOG.md`

- [x] Document source priority, format 2 markers, format 1 compatibility, snapshots, statuses, history, diff UI, validation, Other handling, and DOCX styles.
- [x] Run `git diff --check` and scan staged paths for local OCR/book content.
- [x] Run `.\scripts\build.ps1`.
- [x] Run `.\scripts\test.ps1`.
- [x] Run `.\scripts\package.ps1` because this task specification explicitly requests package verification.
- [x] Inspect package contents and perform supported automated DB/package/import/DOCX validation.
- [x] Review every numbered requirement against code/tests/docs and record any manual-only verification.
- [ ] Commit as `fix: harden proofreading workflow and document export`.
- [ ] Push `main` and verify `HEAD...origin/main` is `0 0`.
