# ChatGPT prompt specification

`ChatGptPromptTemplateProvider` is the only prompt definition source.

`TextProofreading` requires TateScribe format 2, preserves all markers, limits edits to `TEXT_BEGIN`/`TEXT_END`, uses images as authority, separates reports, forbids ruby, and forbids completed DOCX/EPUB/Markdown files.

`RubyAnnotation` freezes body characters and structure, requires JSON only without code fences, defines allowed evidence sources, requires UTF-16 paragraph ranges and exact base text, and moves unsupported readings to `unresolved`.

The prompt window resets to the selected provider template and allows a copy-time local edit. Package `instructions.md` always contains the provider template unchanged.
