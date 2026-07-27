# Chapter Title Boundary Design

## Problem

`DocumentExportService` converts a page whose `PageRole` is `ChapterTitle` into
an internal `[[CHAPTER:...]]` structure marker. `BookDocumentAssembler` then
joins adjacent page text according to the preceding page's
`BoundaryJoinType`.

When the page immediately before a chapter title uses `DirectJoin` or
`Uncertain`, no newline is inserted. The marker is therefore appended to the
preceding body text:

```text
Previous body text[[CHAPTER:Chapter title]]
```

The parser recognizes structure markers only when they occupy a complete line.
The concatenated marker becomes body text, so the marker is visible in DOCX and
neither the heading style nor `PageBreakBefore` is applied.

## Selected Design

Treat the beginning of a structural page as an unconditional paragraph
boundary during page assembly.

Before applying the preceding page's normal join behavior, both the legacy and
source-aware assemblers inspect the next page. If its first line is a complete
supported structure marker, the assembler inserts one newline unless a line
break already exists.

This rule applies to the existing supported markers:

- `[[CHAPTER:...]]`
- `[[TITLE:...]]`
- `[[SECTION_TITLE:...]]`
- `[[SECTION:...]]`

The page's stored `BoundaryJoinType`, proofreading text, and project database
remain unchanged. Non-structural pages retain the existing direct, space,
paragraph, scene-break, and uncertain join semantics.

## Source Provenance

The source-aware assembler attributes the inserted separator to the preceding
page. The separator has no paragraph content and therefore does not change the
chapter title's source span; the title remains owned by the chapter page.

## Verification

Regression coverage must prove that:

1. A directly joined body page followed by a chapter marker produces separate
   body and `ChapterTitle` paragraphs.
2. Source-aware assembly produces the same paragraph structure and attributes
   the chapter title to the chapter page.
3. DOCX export contains no `[[CHAPTER:` text and applies `Heading1` plus
   `pageBreakBefore` when chapter page breaks are enabled.
4. Existing page-boundary behavior and the full test suite remain green.

Do not create a release ZIP. Package verification uses
`scripts/package.ps1 -SkipArchive`.
