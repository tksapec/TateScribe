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
11. Review each ruby proposal. The body is read-only here. Confirm/reject proposals, edit parent ranges/readings, inspect evidence pages and OCR coordinates, and review unresolved items.
12. Export the ruby-enabled DOCX. Only confirmed, non-stale ruby is included.
13. Enter metadata and export the Denden Converter folder.
14. Use Denden Converter to create EPUB outside TateScribe.

If the body changes after a ruby package was exported, TateScribe marks the associated ruby stale. Export a new package and review the new positions; TateScribe does not reattach ruby by blind text search.

The main sidebar scrolls independently. If all commands do not fit vertically, use its scrollbar; the page list and page-order controls remain available below it.
