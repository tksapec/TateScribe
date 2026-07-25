# TateScribe Specification

## Purpose

TateScribe is an offline Windows application that converts a user-selected sequence of smartphone screenshots of Japanese vertical e-books into an editable, horizontal DOCX. It preserves OCR evidence and never silently invents, removes, or rewrites source text.

## Product constraints

- Windows 10/11 x64; .NET 8 WPF with MVVM and nullable references enabled.
- All images, OCR results, text, and telemetry stay local. No network request is made while OCR runs.
- A bundled Python 3.11 OCR worker communicates with the app only through UTF-8 JSON Lines on standard input/output. PaddleOCR is the primary adapter; Tesseract Japanese vertical is an optional adapter.
- Original images are immutable. Per-project SQLite stores metadata, normalized crop regions, OCR evidence, revisions, and export settings; derived images are cache entries.
- The default document is horizontal: body uses Word Normal, chapter/section/subsection use Heading 1/2/3, paragraph indentation is paragraph formatting, and ruby uses Word ruby XML. It does not reproduce the screenshot layout and never creates page breaks from screenshot boundaries.
- Proofreading is a manual package exchange: TateScribe writes a versioned ZIP/folder for ChatGPT attachment and imports a marker-preserving result only after local validation. It never calls an AI API or transmits project data.

## Functional behaviour

1. Import PNG, JPEG, WebP, clipboard images, folders, and drag-dropped files. Record source metadata and SHA-256, then order pages by embedded filename timestamp, EXIF time, created time, modified time, and natural filename order.
2. Allow manual order, inclusion, rotation, crop, and reusable top/bottom exclusion profiles. Flag, but never auto-resolve, duplicate, missing, and contradictory page-order candidates.
3. Preprocess images locally with OpenCV; classify page/region candidates; keep possible body text unless the user explicitly excludes it.
4. Store OCR words with polygons, confidence, engine/model/version, source image and crop. Read vertical text as columns right-to-left and characters top-to-bottom. Preserve column and screenshot boundaries as direct joins by default.
5. Keep low-confidence, ruby, title, illustration/caption, duplicate, paragraph, and scene-break decisions reviewable. Re-OCR must not overwrite manual text or structural edits.
6. Provide a project workspace with thumbnail list, image/crop review, OCR/evidence review, text/structure editing, issue list, and export status.
7. Export DOCX without Word installed, plus optional plain text, OCR JSON, and issue CSV. Do not export screenshot-boundary markers, screenshots, captions, or image-contained text as body text.
8. Retain Raw Paddle words and coordinates, Raw Tesseract text, merge proposals, manual text, and confirmed text independently. Confirmed text wins over manual, proposed, and reconstructed drafts.
9. Require matching project and batch identifiers for proofreading imports; validate page markers, range, order, structure, and unusually large text deltas before any confirmed text is saved.

## Acceptance criteria

- A project can be created, saved, reopened, populated by image files, reordered, and exported without modifying sources.
- OCR worker failures are visible and retryable; cancellation leaves persisted completed work intact.
- Unit tests cover ordering, normalized coordinate conversion, vertical ordering, direct joining, paragraph heuristics, document styles/ruby XML, retained manual edits, persistence, and worker failure handling.
- A self-contained win-x64 package and setup/build/test/package scripts are produced. Dependency and model versions are recorded in `THIRD_PARTY.md`.
