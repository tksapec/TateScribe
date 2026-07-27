# Word Ruby Layout and Review Selection Design

## Scope

Improve TateScribe's DOCX ruby output and ruby-review interaction without
changing OCR, proofreading text, denden export, or the persisted project
schema. Release ZIP creation remains opt-in and is excluded from normal
verification.

## Evidence and compatibility boundary

The available reference DOCX is a TateScribe direct export. Its ruby elements
use `hps=10`, `hpsRaise=10`, and `hpsBaseText=21`, but do not specify run
properties inside `rubyContent` or `rubyBase`. Word-saved 3pt and reset-to-0pt
reference files are not available. Therefore the implementation will add a
repeatable XML inspector and use the following explicitly provisional mapping:

`hpsRaise = rubyFontSizeHalfPoints + wordOffsetPoints * 2`

The mapping is isolated in `WordRubyMetrics`; it must not be interpreted as a
claim that `hpsRaise` itself is Word's displayed offset. A Microsoft Word
manual check of generated 0pt, 3pt, and 5pt files is a required acceptance
step before declaring visual compatibility proven.

## DOCX ruby model

`DocxRubyOptions` owns a validated Word-offset value (integer 0 through 20,
default 3), ruby size (default 10 half-points / 5pt), and existing alignment.
The exporter receives the model rather than exposing placement arithmetic at
the call site. Existing exporter APIs retain their current behavior by using
the default model.

Each ruby writes explicit `RunProperties` for both `RubyContent` and
`RubyBase`: the selected Japanese font in ascii/highAnsi/eastAsia, matching
font size and complex-script size, and `ja-JP` language metadata. Ruby content
uses the configured ruby size; ruby base and `hpsBaseText` use the effective
paragraph text size. Normal body text remains 21 half-points; heading ruby
uses the heading's actual style size. Non-ruby runs remain untouched.

The DOCX export UI accepts the offset before export. Invalid, empty, or
out-of-range input cancels export with an explanation. The current process
retains a valid value; no database migration is introduced.

`scripts/compare-docx-ruby.ps1` extracts and normalizes ruby-related XML from
one or more DOCX files for future A/B/C comparisons. Automated tests validate
the generated OOXML and reopen it with `OpenXmlValidator`; they do not claim
to prove Word rendering.

## Ruby review selection model

The candidate grid uses full-row extended selection. A UI-independent batch
action validates an immutable selected snapshot before changing any state.
Every selected item must have a nonempty reading, a valid UTF-16-safe range,
and a base-text match. Any fatal item leaves every selected item unchanged and
returns item-specific reasons. Explicit user confirmation may set warning
candidates to Confirmed; bulk policies remain unchanged.

The focused/last-selected row remains the detail and evidence-page target when
multiple rows are selected. Summary text includes per-status counts, unresolved
count, and selected-row count. `Ctrl+Enter` confirms the selected rows;
rejecting is intentionally button-only to avoid collision with DataGrid text
editing. `Ctrl+A` uses the grid's standard select-all behavior.

Bulk image/text confirmation reports examined count, newly confirmed count,
already-confirmed count, source mismatch count, excluded count by warning code,
and validation errors. It always reports an outcome, including zero matches,
and keeps the 0.70/0.70/0.60 thresholds intact.

## Tests and documentation

Tests cover ruby metrics validation, OOXML properties and paragraph-size
tracking, XML inspection, batch-action atomicity, multiple selection, and bulk
outcome summaries. Existing DOCX, denden, and ruby JSON regression tests must
remain green. README, USER_GUIDE, TEST_PLAN, CHANGELOG, SPEC, and ARCHITECTURE
describe the user-facing operation and the provisional Word compatibility
boundary.

## Non-goals

- No alteration of body line spacing or ruby alignment modes.
- No OCR/proofreading/ruby data deletion or project-schema migration.
- No claim that headless Open XML tests replace Microsoft Word visual review.
