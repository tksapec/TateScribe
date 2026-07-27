# OCR resume implementation report

## Scope

Implemented Task 1 only: resumable OCR selection and its WPF controls. No Denden, Ruby, Markdown/export, worker/runtime, database-schema, or release-archive changes were made.

## Design

- Added the pure Core model `OcrRunMode` (`Selected`, `ResumeIncomplete`, `ReprocessAll`), `OcrRunPlan`, `OcrPageSelectionPolicy`, and `OcrRunPlanner`.
- `ResumeIncomplete` includes only `IsIncluded` pages with `NotProcessed`, `Failed`, or `Processing` status, ordered by `SortOrder`.
- `Selected` preserves the existing selected-page behavior, including an excluded or completed selected page.
- `ReprocessAll` includes every included page ordered by `SortOrder`; excluded pages are not targets.
- The UI plans before looking up the runtime or creating the cancellation token. An empty plan reports that there are no target pages and never constructs the OCR worker.
- Resume displays target/status/skipped counts before starting. Reprocess-all uses an explicit warning that OCR is overwritten and confirmed proofreading becomes stale.
- Existing `OcrOrchestrationService` remains the execution boundary, so its per-page status saves, failure history, cancellation behavior, and single sequential worker remain unchanged. Progress and result counts use `plan.Targets.Count`.
- Project load reports remaining `Processing` pages from the already loaded page list; it does not change their OCR status.

## TDD evidence

1. RED: added `OcrRunPlannerTests` before production code. The focused test command failed with `CS0103` for missing `OcrRunPlanner` and `OcrRunMode` in all four planner tests.
2. GREEN: added the Core planner/policy and WPF actions. The same focused test command passed: 5/5.

Focused command:

```powershell
dotnet test tests\TateScribe.Tests\TateScribe.Tests.csproj --no-restore --filter "FullyQualifiedName~OcrRunPlannerTests|FullyQualifiedName~MainWindowLayoutTests.Main_window_exposes_resumable_ocr_actions"
```

## Tests added

- `OcrRunPlannerTests`: all OCR statuses, included/excluded behavior, SortOrder, selected-page semantics, reprocess-all semantics, and zero-target resume plan.
- `MainWindowLayoutTests`: exact required Japanese OCR action labels and the three event handlers.
- Existing repository tests continue to cover persisted OCR failure details, cancelled re-OCR preserving an existing completed status, and non-destructive re-OCR state behavior without introducing an external worker dependency.

## Verification

- Focused tests: 5 passed.
- `dotnet build -c Release --no-restore`: 0 warnings, 0 errors.
- `dotnet test -c Release --no-restore`: 216 passed, 0 failed.
- `git diff --check`: clean.

## Self-review

- Confirmed no worker is created for a zero-target plan because the early return precedes runtime lookup and cancellation-token construction.
- Confirmed the pre-existing `_ocrCancellation` guard still prevents concurrent runs, and all three start controls are disabled while OCR runs.
- Confirmed only planned targets reach the orchestration service; no persistence/cancellation code path was changed.
- Confirmed no release ZIP was created.

## Review-fix TDD evidence

### RED

Updated the reprocess-all expectation to include an excluded page, added skipped-total assertions, and added source-layout coverage for completion counts and project-open processing reporting. The focused command failed as expected because `OcrRunPlan` did not contain `SkippedCount`:

```text
error CS1061: 'OcrRunPlan' does not contain a definition for 'SkippedCount'
```

Command:

```powershell
dotnet test tests\TateScribe.Tests\TateScribe.Tests.csproj --no-restore --filter "FullyQualifiedName~OcrRunPlannerTests|FullyQualifiedName~MainWindowLayoutTests.Main_window_reports_complete_ocr_counts_and_project_open_processing_status"
```

### GREEN

- Reprocess-all now targets every page, preserving the previous all-page behavior; only resume treats excluded pages as skipped.
- `OcrRunPlan.SkippedCount` totals the three explicit skipped categories.
- Completion status always reports success, failure (including zero), and skipped counts; failure details remain appended when failures exist.
- Project open calls a read-only status formatter unconditionally, so missing sources and leftover `Processing` pages are reported together when both exist.

The same focused command passed: 5 passed, 0 failed.

Full verification after the review fix:

```powershell
dotnet test -c Release --no-restore
```

Result: 217 passed, 0 failed, 0 skipped.
