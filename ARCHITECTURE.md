# TateScribe Architecture

## Runtime layout

`TateScribe.App` is a WPF presentation shell. It depends on `TateScribe.Core` for domain rules and ports, and on `TateScribe.Infrastructure` for SQLite, OpenCV, Open XML, filesystem, and worker-process adapters. `TateScribe.Tests` tests Core and Infrastructure without WPF automation. `ocr-worker` is a Python command-line process shipped beneath `ocr-runtime` in release builds.

## Data flow

1. Import computes immutable source metadata and creates `Page` records in `project.db`.
2. Preprocessing writes a cache key derived from source hash plus crop/rotation/profile. It produces normalized regions only; the source remains untouched.
3. `JsonLinesOcrWorker` starts one child process, sends a request containing image path, cached image path, engine id and settings, and receives typed word/region records. Stderr is captured as diagnostic data, never parsed as protocol.
4. Layout services create columns and ordered text segments. A reviewable `JoinKind` links columns and pages; `DirectJoin` is the default.
5. PaddleOCR words, Tesseract auxiliary text, OCR runs, merge proposals, manual edits, and confirmed text are separate SQLite layers. Re-OCR refreshes only machine-owned layers.
6. `ProofreadingPackageExporter` writes instructions, manifest, marker-based OCR draft, review items, and stable image names. `ProofreadingImportParser` verifies the package provenance before the repository writes confirmed page text.
7. `DocumentExportService` consumes the resolved active text view in Confirmed → Manual → Suggested → Paddle order and produces Open XML elements, adding ruby only when parent/ruby alignment is confirmed.

## Main interfaces

```csharp
public interface IOcrWorker : IAsyncDisposable
{
    Task<OcrPageResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);
}

public interface IProjectRepository
{
    Task<Project> CreateAsync(string projectPath, CancellationToken cancellationToken);
    Task<IReadOnlyList<Page>> LoadPagesAsync(CancellationToken cancellationToken);
    Task SavePagesAsync(IReadOnlyList<Page> pages, CancellationToken cancellationToken);
}

public interface IDocumentExporter
{
    Task ExportAsync(ExportDocument document, string destinationPath, CancellationToken cancellationToken);
}
```

## Safety boundaries

## Proofreading and review services

- `ProofreadingPackageService` selects provenance-aware text, combines stored ReviewItems, prepares optional crop images, exports format 2, and records the DB snapshot.
- `ProofreadingImportService` reads text/ZIP input, invokes the strict versioned parser, calculates page diffs, and saves only accepted non-error pages.
- `OcrOrchestrationService` owns the persistent JSON Lines worker for a batch, records stage-specific failures, and continues after page-local failures.
- `DocumentExportService` applies PageRole policy and boundary joins before Open XML generation.
- `PageValidationService` is a pure Core validator; SQLite stores its results in `review_items`.

SQLite schema version 8 includes the version 6 OCR/proofreading state plus structured document snapshots, stable paragraphs, source spans, ruby batches, evidence snapshots, annotations, unresolved items, unresolved evidence pages, and each OCR word's original automatic role. Migrations inspect columns with `PRAGMA table_info`, run in one transaction, and only advance `schema_version` after success.

The Python worker remains offline and process-local. PaddleOCR is lazily cached by detection/recognition model directories; Tesseract does not initialize Paddle. Restarting or cancelling the worker discards the process cache safely.
- UI commands are asynchronous and cancelable; only ViewModels touch UI dispatching.
- OCR JSON uses a versioned envelope with one request and one terminal response per line. Unknown fields are ignored; malformed or unexpected responses fail the request with a retry action.
- The database owns user changes. Cache and worker files are disposable and excluded from source control.
- Detection makes recommendations only. No content, page order, crop, structural break, or duplicate decision is applied silently.
## Structured document and ruby

`TateScribe.Core.Ruby` defines the authoritative ruby-aware document model. A `StructuredDocument` contains stable, persisted paragraph IDs. Each paragraph contains ordered `TextInline` and `RubyInline` elements plus a text hash and source-page spans. Concatenating the base text of those inlines always reproduces the original paragraph text.

OCR `RubyCandidate` regions remain OCR evidence and are never treated as final ruby. ChatGPT imports become `RubyAnnotationProposal` records. Only user-confirmed annotations are composed into structured paragraphs. Manual or imported body changes mark affected batches and annotations stale.

SQLite schema version 8 adds document snapshots, paragraphs, provenance spans, ruby batches and evidence snapshots, annotations, annotation/unresolved evidence pages, and the automatic OCR role used to distinguish a true RubyCandidate-to-Body return. Migrations run in the existing initialization transaction. Paragraph logical keys persist independently from displayed text so ordinary text corrections can retain the same paragraph ID.

## ChatGPT boundary

TateScribe does not call the ChatGPT API. `ChatGptPromptTemplateProvider` is the single source for both the prompt window and package `instructions.md`. Text proofreading returns TateScribe format 2 structured text. Ruby annotation returns schema-constrained JSON and cannot change body text. TateScribe alone generates DOCX and Denden Converter input.

## Deterministic exporters

`OpenXmlDocumentExporter` supports the legacy export contract and the structured multi-ruby contract. Structured ruby is written as schema-valid WordprocessingML, while the existing paragraph styles and chapter page-break option remain intact.

`DendenExportService` writes UTF-8 without BOM, LF line endings, fixed file/property ordering, inline ruby, and escaped markup. It does not generate EPUB or ZIP.
