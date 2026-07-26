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

SQLite schema version 6 adds independent OCR status, last/baseline OCR run identifiers, export snapshots, text-version baselines, OCR failures, persistent ReviewItems, OCR word overrides, and boundary join type. Migrations inspect columns with `PRAGMA table_info`, run in one transaction, and only advance `schema_version` after success.

The Python worker remains offline and process-local. PaddleOCR is lazily cached by detection/recognition model directories; Tesseract does not initialize Paddle. Restarting or cancelling the worker discards the process cache safely.
- UI commands are asynchronous and cancelable; only ViewModels touch UI dispatching.
- OCR JSON uses a versioned envelope with one request and one terminal response per line. Unknown fields are ignored; malformed or unexpected responses fail the request with a retry action.
- The database owns user changes. Cache and worker files are disposable and excluded from source control.
- Detection makes recommendations only. No content, page order, crop, structural break, or duplicate decision is applied silently.
