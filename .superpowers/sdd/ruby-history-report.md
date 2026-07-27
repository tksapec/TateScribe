# Task 3A review: ruby validation and batch history

Reviewed range: `b9a38ea..483bb6b`
Specification scope: sections 5, 6, 7, 10, 11, and 12

## Verdict

- **Specification verdict: REQUEST CHANGES.** The core SourceSpan validation,
  thresholds, save gate, and history selection are present, but the new
  annotation-ID matching rule breaks initial warning binding for newly
  imported JSON, and section 10's documentation list is incomplete.
- **Quality verdict: REQUEST CHANGES.** Release build and all 247 tests pass,
  but the UI-oriented tests are source-text assertions and do not exercise the
  broken import-to-review warning pipeline.
- **Risk: MEDIUM.** The defect affects review visibility and evidence
  presentation, while save-time revalidation still blocks errors and prompts
  for newly materialized warnings. No schema migration or destructive storage
  change was introduced.

## Critical

None found.

## Important

### 1. Newly imported annotations lose their initial validation-warning binding

`PrepareImportAsync` validates the ID-less JSON first and only afterwards
assigns a new `AnnotationId` to each proposal
(`src/TateScribe.App/Services/RubyWorkflowService.cs:86-97`). The validation
issues produced in the first step therefore have `AnnotationId == null`.

The new ID-first matcher deliberately refuses range fallback when either side
has an ID (`src/TateScribe.Core/Ruby/RubyBulkConfirmationPolicy.cs:28-31`).
When `RubyReviewWindow` builds its rows, each proposal now has an ID but its
initial issues do not, so the filter at
`src/TateScribe.App/RubyReviewWindow.xaml.cs:34-48` matches none of the
candidate-scoped warnings. As a result:

- warnings such as `LowConfidence`, `ImageCandidateMismatch`,
  `LowOcrCandidateConfidence`, `LowLinkConfidence`,
  `EvidencePageDoesNotMatchSourceSpan`, and `WrongSideCandidate` are absent
  from the initial candidate display;
- `initialWarningKeys` is incorrectly empty;
- the import and saved-review paths behave differently even for the same
  proposal.

The save gate invokes `ValidateReviewed` again, and those new issues do carry
the proposal IDs, so errors still block saving and Confirmed warnings still
produce the acknowledgement prompt. This limits the blast radius, but it does
not satisfy the candidate-warning visibility/binding intent in sections 5-6.

Recommended fix: after assigning annotation IDs, rerun validation on the
identified `RubyImportDocument` (or assign IDs before the authoritative
validation) so proposals and issues share the same IDs. Add an integration
test covering `PrepareImportAsync` through the initial `RubyReviewWindow`
candidate issue binding.

### 2. Section 10's required documentation set was not updated

The change updates `README.md`, `USER_GUIDE.md`, and
`docs/RUBY_JSON_FORMAT.md`, but does not update the other explicitly listed
documents: `SPEC.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`, `CHANGELOG.md`,
`docs/DENDEN_EXPORT.md`, or an ADR where the validation/history decision is
architecturally relevant.

Repository-wide searches show no descriptions of the new named trust warnings,
thresholds, save-time acknowledgement, or batch-history behavior in
`SPEC.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`, or `CHANGELOG.md`. At minimum,
the normative specification, architecture/storage description, test plan, and
changelog need to reflect this implementation. `docs/DENDEN_EXPORT.md` may
legitimately need no ruby-specific text, but that should be an explicit
no-change determination rather than silently omitting the section-10 review.

## Minor

### 1. UI regression coverage is structural rather than behavioral

`MainWindowLayoutTests` checks for source-code fragments such as
`RefreshValidationIssues`, `MessageBoxButton.YesNo`, and
`FirstOrDefault(item => item.AnnotationCount > 0)`. These tests confirm that
tokens are present, but not that:

- imported issues remain attached after IDs are assigned;
- a newly warned Confirmed candidate cannot close without acknowledgement;
- selecting a historical row loads that exact batch.

The repository test gives good behavioral coverage to ordering/counts, and the
validator tests cover the individual warning codes, but section 11's
save-before-validation and batch-selection requirements still lack an
integration-level path. This is why Important finding 1 passes the current
suite.

## Confirmed implementation points

- `ImageConfirmed` computes the annotation's half-open range, intersects it
  with `StructuredParagraph.SourceSpans`, and only considers OCR candidates on
  evidence pages in that source-page set.
- The four requested nonfatal trust warnings are present:
  `LowOcrCandidateConfidence`, `LowLinkConfidence`,
  `EvidencePageDoesNotMatchSourceSpan`, and `WrongSideCandidate`.
- The three inclusive thresholds are centralized in
  `RubyBulkConfirmationPolicy` at `0.70`, `0.70`, and `0.60`.
- Any candidate-scoped warning/error prevents bulk confirmation. Individual
  confirmation remains available.
- The source-page restriction is applied only to `ImageConfirmed`;
  `TextConfirmed` and the other source meanings are unchanged.
- Saving revalidates edited annotations, refreshes issue bindings, keeps the
  window open for errors, and asks Yes/No before saving a Confirmed candidate
  with a newly introduced warning.
- History is ordered newest-first, initially selects the latest batch with
  annotations, leaves empty export batches visible but not openable, reports
  the required counts, and marks non-current snapshots stale.
- The repository change adds SELECT queries only. Schema version remains 9;
  no migration, table change, deletion, or rewrite of OCR/body/ruby/history
  data was added in this range.

## Verification

- Focused Release tests: **80 passed, 0 failed**
- Full Release build: **0 warnings, 0 errors**
- Full Release tests: **247 passed, 0 failed, 0 skipped**
- `git diff --check b9a38ea..483bb6b`: **passed**
- No Release ZIP was created.

## Review resolution

Resolved the two Important findings without changing Task 3B.

### Fresh-import warning identity

`PrepareImportAsync` still performs the strict JSON validation first. After it
assigns internal `AnnotationId` values, it now calls
`ValidateReviewed(batch, identified)` instead of only replacing
`preview.Result`. The second validation uses the identified in-memory document,
so every candidate-scoped issue receives the same ID as its proposal.
`RubyBulkConfirmationPolicy.Matches` remains unchanged: ID matching is
mandatory whenever either side has an ID, and paragraph/start/length fallback
is used only when both are ID-less.

TDD evidence:

- RED: the focused run failed because
  `Fresh_ruby_import_revalidates_after_assigning_annotation_ids` could not find
  the required revalidation call in `RubyWorkflowService`.
- GREEN: the two fresh-import regression tests passed. The behavioral test
  verifies that the initial ID-less warning becomes ID-bound after assignment
  and revalidation and that the strict matcher attaches it to the proposal.
- The wider Ruby workflow/MainWindow focused Release run passed 51/51.

### Documentation completion

Updated `SPEC.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`, `CHANGELOG.md`, and
`docs/ADR-0002-denden-assets-and-ruby-evidence.md`. They now describe:

- resumable OCR selection, page-local persistence/failure history, and explicit
  full re-OCR;
- the Denden root README versus upload-only converter assets;
- ImageConfirmed SourceSpan ownership, named thresholds and warning policy;
- fresh-ID and save-time revalidation, acknowledgement, and batch history;
- standard SlowZip exclusion and no-archive Release verification.

`docs/DENDEN_EXPORT.md` required no further edit because its current root
README/upload-only instructions already match the implementation.

### Review-fix verification

- `git diff --check`: passed.
- Focused Release tests: 51 passed, 0 failed.
- Full Release build: 0 warnings, 0 errors.
- Full Release tests: 249 passed, 0 failed, 0 skipped.
- No ZIP or archive was created.
