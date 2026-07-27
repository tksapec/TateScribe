# Ruby evidence validation and batch history report

Date: 2026-07-27

## Scope

Implemented Task 3A from the ruby evidence/history hardening request:

- source-span-bound `ImageConfirmed` validation;
- centralized bulk-confirmation thresholds and warning exclusions;
- save-time review validation with refreshed candidate warning bindings;
- explicit acknowledgement when an edited Confirmed candidate gains a warning;
- read-only ruby batch history and latest-annotated-batch selection;
- focused documentation and regression tests.

Task 3B was intentionally excluded. No export preflight mutation, ruby-count
deduplication, or export-side database write behavior was added or changed.

## Design and behavior

### Image evidence validation

`RubyImportValidator` now intersects each ImageConfirmed annotation's
`paragraphId`/`start`/`length` range with its `StructuredParagraph.SourceSpans`.
Only OCR candidates on evidence pages that also own that source range may
support the image confirmation. This prevents a same-reading/same-base
occurrence on another page from being accepted accidentally.

The following candidate-scoped warnings were added:

- `EvidencePageDoesNotMatchSourceSpan`;
- `LowOcrCandidateConfidence`;
- `LowLinkConfidence`;
- `WrongSideCandidate`.

Existing `ImageCandidateMismatch` and `BaseTextCandidateUnknown` distinctions
remain. Reading comparison continues to use the established safe reading
normalizer; parent text remains an exact ordinal match. The candidate must be
on the ruby side (`ReturnedToBody == false`) and meet both confidence floors.
`TextConfirmed` deliberately does not use the source-page restriction.
`UserConfirmed`, `DictionarySuggested`, and `ContextSuggested` were unchanged.

### Bulk confirmation policy

`RubyBulkConfirmationPolicy` is the single policy/constant location:

- `MinBulkConfirmAnnotationConfidence = 0.70`;
- `MinBulkConfirmOcrConfidence = 0.70`;
- `MinBulkConfirmLinkConfidence = 0.60`.

Bulk confirmation still requires the requested source, non-empty evidence and
evidence pages, an eligible status, and no matching error or warning.
Candidate issue matching now uses `AnnotationId` whenever either side has one;
paragraph/range fallback is used only when both sides have no ID.

### Review save gate

`RubyWorkflowService.ValidateReviewed` validates the in-memory reviewed model,
preserving persisted annotation IDs. `RubyReviewWindow` reruns it after grid
edits and before `DialogResult` can close the window. It refreshes every row's
issues using annotation-ID-first matching. Errors are shown and keep the
window open. If a Confirmed row has a warning that was not present when the
window opened, the warning text is presented in a Yes/No acknowledgement.
Choosing No returns to review; no automatic status downgrade occurs.

### Batch history

The repository adds read-only aggregate queries; schema version 9 and all
stored data remain unchanged.

The history query returns:

- exported UTC;
- batch ID;
- document text hash;
- ruby policy;
- total annotation count;
- Confirmed, Proposed, and Stale counts;
- unresolved count;
- Current/Stale document marker.

All export batches remain visible for context. The history window initially
selects the newest batch with annotations and disables Open for empty batches,
so a newer package that was never imported cannot hide an older saved review.
The user can select any annotated historical batch and open it.

## Tests and TDD evidence

RED was observed first:

- the focused test project failed to compile because the three named
  thresholds and the two repository history methods did not exist;
- after the initial implementation, trust-warning tests failed because the
  test passed a pre-serialization ID rather than the validator's actual
  ID-less imported proposal. The test was corrected to exercise the real
  imported proposal while retaining the annotation-ID precedence test.

GREEN verification:

- Focused Release:
  `dotnet test tests\TateScribe.Tests\TateScribe.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MainWindowLayoutTests|FullyQualifiedName~RubyWorkflowTests|FullyQualifiedName~ProjectRepositoryTests"`
  - Passed: 80
  - Failed: 0
- Full Release build:
  `dotnet build TateScribe.sln -c Release --no-restore`
  - Warnings: 0
  - Errors: 0
- Full Release test:
  `dotnet test TateScribe.sln -c Release --no-build --no-restore`
  - Passed: 247
  - Failed: 0
  - Skipped: 0
- `git diff --check`
  - Passed

No Release ZIP or archive was created.

## Compatibility notes

- No database migration or destructive SQL was needed; history is computed
  from existing schema-v9 tables.
- The external ruby JSON format remains version 1 and does not expose internal
  annotation IDs.
- Individual manual/visual confirmation remains possible even when a warning
  blocks bulk confirmation.
- Export and preflight logic were not modified.
