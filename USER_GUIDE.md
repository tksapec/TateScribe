# User guide

1. Import book screenshots and run OCR.
2. Export a text proofreading package.
3. Open `ChatGPTへの指示`, select `本文校正`, and copy the prompt.
4. Give ChatGPT the package and prompt.
5. Import the returned TateScribe format 2 text.
6. Review page differences and confirm the body.
7. Select a ruby policy and export a ruby review package.
8. Open `ChatGPTへの指示`, select `ルビ確認`, and copy the prompt.
9. Give ChatGPT the ruby package and prompt.
10. Import the returned JSON. A validation error leaves the database unchanged.
11. Review each ruby proposal. The body is read-only here. Confirm/reject proposals, edit parent ranges/readings, inspect every matching OCR coordinate and candidate warning, and review unresolved items. `ImageConfirmed` means that the reading and linked parent text match an OCR candidate on a page owning the annotation's source range. Bulk confirmation requires annotation confidence 0.70, OCR confidence 0.70, link confidence 0.60, non-empty evidence, and no candidate warning. Individual visual confirmation remains available. Saving revalidates edited ranges and readings; errors keep the window open, and a newly warned Confirmed item requires explicit acknowledgement.
12. Export the ruby-enabled DOCX. The common preflight shows unproofread, Other, empty pages, and ruby states. Only confirmed, non-stale ruby is included.
13. Enter metadata and export the Denden Converter folder. Enable `挿絵ページを含める` only when `Illustration` pages should be included. Open its `upload` folder and select every file inside it together when uploading; never select the root `README.txt`.
14. Use Denden Converter to create EPUB outside TateScribe.

## Word ruby offset and review selection

The Word ruby offset has a 3pt default and accepts whole numbers from 0 through 20. TateScribe calculates the raise provisionally from the ruby font size and this offset; change the offset, then re-export the DOCX. Inspect the re-exported document in Word. XML diagnostics do not replace manual Word visual verification, and B/C comparison remains pending until Word-saved reference files are available.

In the ruby grid, use Ctrl/Shift for extended selection. Ctrl+Enter confirms the selected rows after pending edits are committed. The Confirm and Reject buttons operate only on those selected rows; rejection is button-only and has no Delete-key shortcut. Image-based and text-based bulk confirmation are deliberately separate. Each bulk result reports examined, newly confirmed, already confirmed, wrong-source, and excluded counts, with an exclusion-reason breakdown. The summary includes the selected count.

This workflow requires no SQLite schema migration and never creates a Release ZIP.

If the body changes after a ruby package was exported, TateScribe marks the associated ruby stale. Export a new package and review the new positions; TateScribe does not reattach ruby by blind text search.

`保存済みルビ候補を確認` opens the ruby batch history. It initially selects the newest batch that actually contains annotations, even when a newer package export has not been imported. Select an older annotated batch to review it. The table shows exported UTC, batch ID, document hash, policy, annotation/status counts, unresolved count, and whether the batch belongs to the current document.

The main sidebar scrolls independently. If all commands do not fit vertically, use its scrollbar; the page list and page-order controls remain available below it.

Denden images may be PNG, JPEG, GIF, WebP, BMP, or TIFF input. Full-crop unrotated PNG/JPEG/GIF remain in that format; transformed illustrations become PNG after rotation and crop. Output stops before creating the folder if an image exceeds 3 MiB after preparation or files inside `upload` exceed 100. TateScribe itself never creates EPUB or a release ZIP from this command.
