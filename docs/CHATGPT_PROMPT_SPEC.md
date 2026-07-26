# ChatGPT prompt specification

`ChatGptPromptTemplateProvider` is the only prompt definition source.

`TextProofreading` requires TateScribe format 2, preserves all markers, limits edits to `TEXT_BEGIN`/`TEXT_END`, uses images as authority, separates reports, forbids ruby, and forbids completed DOCX/EPUB/Markdown files.

`RubyAnnotation` freezes body characters and structure, requires JSON only without code fences, defines allowed evidence sources, requires UTF-16 paragraph ranges and exact base text, and moves unsupported readings to `unresolved`. It requires format version 1; verbatim manifest project/batch/document-hash identity; present `annotations` and `unresolved` arrays even when empty; unchanged schema property names; no unknown properties; paragraph IDs copied from `confirmed-document.json`; and no completed file or body response.

The prompt explicitly defines `readingCandidate` as the OCR reading hypothesis and `baseTextCandidate` as optional parent-text evidence. The confirmed-document range remains authoritative. Ambiguous OCR evidence must be checked against the image or returned as unresolved.

The prompt window resets to the selected provider template and allows a copy-time local edit. Package `instructions.md` always contains the provider template unchanged.
