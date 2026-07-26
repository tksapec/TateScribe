# ChatGPT workflow

TateScribe separates ChatGPT work into two tasks. Do not combine them in normal operation.

## TextProofreading

1. Export a text proofreading package.
2. Open `ChatGPTへの指示`, select `本文校正`, and copy the prompt.
3. Give ChatGPT the package and prompt.
4. Receive TateScribe format 2 structured text. ChatGPT must not create DOCX, EPUB, Markdown, or ruby.
5. Import the returned text and review page-level differences.

The package `instructions.md` and the prompt window use the same provider. The format, project, batch, page markers, text blocks, join markers, and report block are validated before saving.

## RubyAnnotation

1. Finish text proofreading. TateScribe warns if unproofread pages remain.
2. Select a ruby policy. `PreserveOriginalOnly` is the default.
3. Export the ruby review directory. ZIP is not the default.
4. Open `ChatGPTへの指示`, select `ルビ確認`, and copy the prompt.
5. Give ChatGPT the directory and prompt.
6. Receive JSON only. ChatGPT must not reproduce or change the body.
7. Import the JSON. Any validation error prevents all DB writes.
8. Review the read-only paragraph, parent range, reading, source, confidence, evidence pages, OCR coordinates, conflicts, and warnings.
9. Confirm or reject each proposal. Bulk confirmation is limited to `ImageConfirmed` and `TextConfirmed`.
10. Export DOCX or Denden Converter input. Only `Confirmed` ruby is used.

Changing the body after package export makes the batch stale. Confirmed ruby attached to changed source pages becomes `Stale` and is excluded until reviewed again.
