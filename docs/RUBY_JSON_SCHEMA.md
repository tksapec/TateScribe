# Ruby JSON Schema

Every ruby package contains the authoritative machine-readable `output-schema.json` using JSON Schema draft 2020-12.

Top-level properties are fixed to `formatVersion`, `projectId`, `batchId`, `documentTextHash`, `annotations`, and `unresolved`; additional properties are rejected. Annotation and unresolved item properties are likewise fixed.

See [RUBY_JSON_FORMAT.md](RUBY_JSON_FORMAT.md) for the human-readable format and validation rules. The exporter constant in `RubyPackageExporter` is covered by package and validator tests so the shipped schema and accepted JSON remain aligned.
