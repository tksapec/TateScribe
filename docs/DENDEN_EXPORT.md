# Denden Converter export

TateScribe creates converter input deterministically. ChatGPT does not create or edit these files, and TateScribe does not generate EPUB or ZIP.

Standard output keeps instructions at the destination root and places every Denden Converter input inside `upload/`:

```text
DendenExport/
  README.txt
  upload/
    book.md
    ddconv.yml
    default.css
    cover.png
    illustration-001.png
```

Chapter splitting writes `chapter-001.md` etc. and does not create an empty `book.md`. An export with no content blocks is rejected before the destination is created. Illustration output is opt-in and includes only included pages explicitly classified as `PageRole=Illustration`. Every emitted illustration has a Markdown reference such as `![挿絵 1](illustration-001.png)`. When `displayLoiNav` is enabled, illustrations instead use the official `<figure class="illustration">`, `<img>`, and `<figcaption>` markup required for the illustration list. A joined paragraph is never split to insert an image; the image moves after the paragraph and preflight reports `IllustrationPlacementAdjusted`.

Select every file inside `upload` when using Denden Converter; never select `README.txt` itself. Only files inside `upload` count toward the 100-file limit.

Valid PNG, JPEG, and GIF data with full crop and no rotation keep their actual format after decode/structure validation. Transformed illustrations are decoded, rotated, cropped in rotated coordinates, and encoded as PNG. Truncated or signature-spoofed files are rejected. WebP, BMP, TIFF, and other decodable input is encoded as PNG with a stable name. The extension always matches the output bytes. An output image over 3 MiB or an export over 100 files is rejected before the destination folder is created. Ordinary OCR screenshots are never copied.

`ddconv.yml` is UTF-8 without BOM, LF-only, and emitted in this fixed form:

```yaml
ddconvVersion: 1.0
titles:
  - content: "書名"
creators:
  - content: "著者"
    role: aut
language: "ja"
pageDirection: rtl
options:
  skipCover: true
  titlepage: true
  tocInSpine: true
  tocDisplayDepth: 2
  displayLandmarksNav: false
  displayLoiNav: false
  autoTcy: true
  tcyDigit: 2
```

Vertical writing uses `rtl`; horizontal writing uses `ltr`. TOC depth is 1–6 and `tcyDigit` is 2 or greater. YAML strings are double-quoted with backslashes and quotes escaped.

Confirmed ruby is written as `{親文字|読み}`. Source body text escapes literal ampersands, HTML angle brackets, braces, vertical bars, backslashes, list prefixes, code indentation, and Markdown-sensitive characters. Generated headings, scene breaks, image syntax, and ruby syntax remain structural Markdown. Chapter titles become level-one headings, section titles/numbers become level-two headings, and scene breaks become `***`. `ruby.csv` remains an API-level opt-in only for explicitly approved terms with one global reading.

DOCX and Denden use the same preflight page/ruby rules. Proposed, Unresolved, and Stale ruby are reported and excluded.
