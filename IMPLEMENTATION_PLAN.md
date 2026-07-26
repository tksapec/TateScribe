# Implementation plan

1. Centralize task-specific ChatGPT prompts.
2. Add structured document, ruby policy/source/status, proposal, unresolved, and validation contracts.
3. Add strict JSON validation and package JSON Schema.
4. Migrate SQLite transactionally to schema version 8.
5. Persist stable paragraph identities, source spans, ruby batches, OCR evidence snapshots, annotations, history, and unresolved items.
6. Add folder-first ruby package export and review/import services.
7. Add read-only body ruby review UI with individual and evidence-source bulk actions.
8. Add schema-valid multi-ruby Open XML export while keeping the legacy interface.
9. Add deterministic Denden Converter output and metadata UI.
10. Make the main sidebar scrollable and verify every window at runtime.
11. Exclude the one `SlowZip` test by default and support `package.ps1 -SkipArchive`.
12. Run build, fast tests, Python tests, publish without archive, UI inspection, review, commit, and push.

The detailed development record is in `docs/superpowers/plans/2026-07-26-structured-ruby-and-denden.md`.

## Denden and ruby evidence hardening

The follow-up plan for official `ddconv.yml`, referenced illustration assets,
safe image conversion, schema-v9 ruby evidence linking, candidate-scoped
validation, and shared export preflight is in
`docs/superpowers/plans/2026-07-26-denden-and-ruby-evidence-hardening.md`.
