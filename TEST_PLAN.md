# Test plan

All fixtures use artificial text and artificial byte/image data.

- Prompts: format 2, JSON-only ruby response, body-change prohibition, no DOCX return, shared UI/package provider.
- Ruby JSON: valid import and every identity, hash, paragraph, UTF-16, range, base-text, reading, source, confidence, overlap, duplicate, evidence-page, policy, and stale failure.
- Model: mixed text/ruby, multiple ruby, different readings at different positions, unchanged plain text, stable paragraph IDs.
- Database: old database migration, incompatible migration rollback, proposal/history/status persistence, no duplicate history, body-change staleness.
- DOCX: legacy output, multiple ruby, preserved text/styles/spaces, no duplicate base text, OpenXmlValidator success.
- Denden: inline ruby, different readings, escaping, roles, YAML, RTL, UTF-8 without BOM, LF, deterministic bytes, conditional safe `ruby.csv`, no EPUB/ZIP.
- UI structure: scrollable sidebar, four crop inputs, task prompt selector, ruby/unresolved review, separate bulk actions, Denden metadata controls.
- Runtime UI: inspect the main, prompt, proofreading import, ruby review, page evidence, and Denden windows at default and practical minimum sizes.

Standard verification:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -SkipArchive
```

`scripts/test.ps1` excludes `Category=SlowZip` unless `-IncludeSlowZip` is explicitly supplied. The release ZIP is not created during normal verification.
