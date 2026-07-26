# TateScribe Implementation Plan

**Goal:** Deliver an offline Windows x64 vertical Japanese e-book screenshot OCR workflow that maintains source evidence and produces editable DOCX.

**Architecture:** WPF/MVVM calls focused Core services and Infrastructure adapters. SQLite persists project state; a bundled Python worker provides OCR through JSON Lines, isolated from UI and storage.

**Global constraints:** .NET 8, nullable enabled, async cancellation, no network during OCR, source images immutable, no telemetry, fixed dependencies/models, TDD for functional code.

## Phase 1 — Foundation and project workflow

1. Create the solution, central fixed package versions, CI-friendly scripts, Git metadata, and documentation; build and test from a clean checkout.
2. Implement project creation/opening, SQLite migrations, immutable page metadata, SHA-256 import, natural ordering, inclusion/rotation/crop state, and thumbnail workspace.
3. Implement OpenCV cache preprocessing and manual top/bottom exclusion profiles; test geometry and cache-key invariants.

## Phase 2 — OCR evidence and vertical layout

1. Define the versioned JSON Lines worker protocol and supervised process adapter with cancellation/retry diagnostics; add the Python worker with a deterministic mock engine.
2. Add primary PaddleOCR and optional Tesseract adapters, both using local configured models only; record engine/model/version in every result.
3. Persist words/polygons/confidence and implement reviewable vertical columns, ruby candidates, headers/footers, titles, images/captions, and direct joins.

## Phase 3 — Reconstruction and review

1. Implement page-to-page overlap, duplicate/missing/order candidates, paragraph/scene-break recommendations, and issue list without automatic destructive resolution.
2. Add image, region, OCR, text, confirmation, and export workspace panes. Maintain manual revisions independently from OCR evidence.

## Phase 4 — Export and quality gates

1. Generate horizontal DOCX with styles, paragraph indentation, ruby XML, bookmarks/custom evidence linkage, plus text/JSON/CSV optional exports.
2. Add quality checks for empty output, unreviewed uncertain text, broken ruby attachment, duplicate candidates, and export summary.

## Phase 5 — Packaging and acceptance

1. Bundle Python runtime, wheels and downloaded model files into the win-x64 release without runtime downloads; record checksums/licenses.
2. Run unit/protocol tests, manual sample-image acceptance, publish self-contained win-x64, inspect the archive, and tag each completed milestone in Git.

## Phase 6 — Manual proofreading exchange

## Phase 7 — Hardened proofreading and document export

- [x] Select Confirmed/Manual/Suggested/RawPaddle text and emit formatVersion 2.
- [x] Strictly isolate page text and report blocks while retaining version 1 import.
- [x] Persist export snapshots, stale warnings, independent OCR state, and text history.
- [x] Add page-level before/after diff and accepted-page counting.
- [x] Cache PaddleOCR in a persistent worker and retain structured page failures.
- [x] Read EXIF timestamps and persist printed-page validation ReviewItems.
- [x] Allow RubyCandidate role/inclusion overrides and show coordinate boxes.
- [x] Include text-bearing Other pages and define/validate DOCX styles.
- [x] Extract OCR, package, import, document, and page-validation services.
1. Persist non-destructive Paddle/Tesseract runs, merge proposals, user edits, confirmed text, profile/role metadata, and status with idempotent SQLite migration.
2. Export versioned proofreading ZIP/folder batches with stable page markers, source hashes, original/cropped images, review items, and offline instructions; import only matching batches after validation and explicit confirmation.
3. Resolve confirmed text into structured DOCX paragraphs without screenshot-boundary breaks; preserve chapter/section markers and allow an optional page break before chapters.
