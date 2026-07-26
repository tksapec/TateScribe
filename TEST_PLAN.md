# Test plan

All fixtures use artificial text and artificial byte/image data.

- Prompts: format 2, JSON-only ruby response, body-change prohibition, no DOCX return, shared UI/package provider.
- Ruby JSON: valid import and every identity, hash, paragraph, UTF-16, range, base-text, reading, source, confidence, overlap, duplicate, evidence-page, policy, and stale failure.
- Model: mixed text/ruby, multiple ruby, different readings at different positions, unchanged plain text, stable paragraph IDs.
- Database: old database migration through schema 9, v8 candidate backfill, incompatible migration rollback, proposal/history/status persistence, no duplicate history, body-change staleness.
- DOCX: legacy output, multiple ruby, preserved text/styles/spaces, no duplicate base text, OpenXmlValidator success.
- Denden: official YAML keys and ranges, inline ruby, different readings, escaping, roles, RTL/LTR, UTF-8 without BOM, LF, deterministic bytes, flat Markdown-referenced illustrations, official illustration-list figure markup, joined-paragraph placement warnings, validated PNG/JPEG/GIF preservation, corrupt-image rejection, WebP conversion, empty-export rejection, 3 MiB and 100-file failures, cleanup, conditional safe `ruby.csv`, no EPUB/ZIP.
- Ruby evidence: reading/base separation, coordinate linking and ambiguity, width/kana normalization, candidate-scoped warnings, schema/validator parity, safe bulk-confirm rules.
- Preflight: common DOCX/Denden summary, unproofread/Other/empty pages, confirmed/proposed/unresolved/stale counts, existing destination, cancellation before filesystem output.
- UI structure: scrollable sidebar, four crop inputs, task prompt selector, ruby/unresolved review, separate bulk actions, Denden metadata controls.
- Runtime UI: inspect the main, prompt, proofreading import, ruby review, page evidence, and Denden windows at default and practical minimum sizes.

Standard verification:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -SkipArchive
```

`scripts/test.ps1` excludes `Category=SlowZip` unless `-IncludeSlowZip` is explicitly supplied. The release ZIP is not created during normal verification.
