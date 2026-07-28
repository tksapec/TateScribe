# Test plan

All fixtures use artificial text and artificial byte/image data.

- Prompts: format 2, JSON-only ruby response, body-change prohibition, no DOCX return, shared UI/package provider.
- OCR execution: planner selection for every status, resume ordering and zero-target behavior, Completed/ReviewRequired/excluded skip counts, explicit reprocess-all behavior, cancellation preservation, per-page failure continuation, and persistent failure-history display.
- Ruby JSON: valid import and every identity, hash, paragraph, UTF-16, range, base-text, reading, source, confidence, overlap, duplicate, evidence-page, policy, and stale failure.
- Model: mixed text/ruby, multiple ruby, different readings at different positions, unchanged plain text, stable paragraph IDs.
- Export purity: preparation/cancellation/output failure create no snapshot or ruby batch; successful DOCX/Denden output persists exactly one deduplicated snapshot. Ruby preflight deduplicates repeated imports and unresolved items, uses the latest same-reading state, excludes conflicting readings, and matches DOCX ruby composition.
- Database: old database migration through schema 9, v8 candidate backfill, incompatible migration rollback, proposal/history/status persistence, no duplicate history, body-change staleness.
- DOCX: legacy output, multiple ruby, preserved text/styles/spaces, no duplicate base text, OpenXmlValidator success; Word offset 3pt default and inclusive 0 through 20 validation; provisional raise calculation and re-export after an offset change; compare normalized direct-export XML (`hps=10`, `hpsRaise=16`, `hpsBaseText=21`) and verify Japanese-font, size, and `ja-JP` RunProperties in both RubyContent and RubyBase. Diagnostics do not replace manual Word visual verification; B/C comparison stays pending until Word-saved reference files are supplied.
- Denden: official YAML keys and ranges, inline ruby, different readings, semantic-safe source escaping, roles, RTL/LTR, UTF-8 without BOM, LF, deterministic bytes, root README/upload-only layout, exact 100-file upload boundary, Markdown-referenced illustrations, official illustration-list figure markup, joined-paragraph placement warnings, validated PNG/JPEG/GIF preservation, 90/180/270 rotated-coordinate crops, transformed PNG output and original preservation, corrupt-image rejection, WebP conversion, empty-export rejection, post-transform 3 MiB failure, cleanup, conditional safe `ruby.csv`, no EPUB/ZIP.
- Ruby evidence: reading/base separation, coordinate linking and ambiguity, width/kana normalization, source-span/evidence-page intersection, `ImageCandidateMismatch`, `BaseTextCandidateUnknown`, `LowOcrCandidateConfidence`, `LowLinkConfidence`, `EvidencePageDoesNotMatchSourceSpan`, `WrongSideCandidate`, inclusive 0.70/0.70/0.60 thresholds, TextConfirmed cross-page exception, schema/validator parity, and safe bulk-confirm rules.
- Preflight: common DOCX/Denden summary, unproofread/Other/empty pages, confirmed/proposed/unresolved/stale counts, existing destination, cancellation before filesystem output.
- UI structure: scrollable sidebar, four crop inputs, resumable/reprocess OCR actions, task prompt selector, ruby/unresolved review, save-time warning refresh, separate bulk actions, ruby batch history columns/selection, and Denden metadata controls. Ruby review supports Ctrl/Shift extended selection, Ctrl+Enter confirmation, selected-count summary, and button-only rejection (no Delete-key shortcut). Verify separate image/text bulk dialogs report examined, newly confirmed, already confirmed, wrong-source, excluded, and per-reason exclusion categories.
- Runtime UI: inspect the main, prompt, proofreading import, ruby review, ruby batch history, page evidence, and Denden windows at default and practical minimum sizes. Confirm edited validation errors keep review open and a newly warned Confirmed item requires acknowledgement.
- Ruby history: latest annotated selection when a newer empty export exists, historical status/unresolved counts, document-current/stale marker, and opening a user-selected annotated batch.

Standard verification:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -SkipArchive
```

`scripts/test.ps1` excludes `Category=SlowZip` unless `-IncludeSlowZip` is explicitly supplied. Standard verification must not create a ZIP; `package.ps1 -SkipArchive` is the Release artifact check used by the normal test workflow.

The Word ruby compatibility workflow adds no SQLite schema migration and no Release ZIP.
