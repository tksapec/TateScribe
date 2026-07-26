# Denden Converter export

TateScribe creates the converter input deterministically. ChatGPT does not create or edit these files.

Standard output:

```text
DendenExport/
  book.md
  ddconv.yml
  default.css
  README.txt
```

An explicitly selected cover is copied as `cover.jpg`. Chapter splitting creates fixed names such as `chapter-001.md`. `ruby.csv` is an API-level opt-in for terms explicitly approved as having one global reading; conflicting readings are rejected. The normal UI uses inline ruby and does not create `ruby.csv`.

Confirmed ruby is written as `{親文字|読み}`. Literal braces, vertical bars, backslashes, and common Markdown-sensitive characters in body text are escaped. Chapter titles become level-one headings, section titles/numbers become level-two headings, and scene breaks become `***`.

All text files use UTF-8 without BOM and LF line endings. File and YAML property ordering is fixed, and no time or random identifier is written. Vertical writing uses `pageDirection: rtl`. TateScribe does not generate EPUB or ZIP in this phase.
