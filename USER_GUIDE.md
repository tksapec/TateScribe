# TateScribe User Guide

1. Choose a book folder and import images. Check page order, rotation, inclusion, display profile, page role, and all four crop edges.
2. Run OCR. PaddleOCR words with coordinates, auxiliary Tesseract text, and a non-destructive suggested text are retained separately.
3. Export a proofreading package for roughly ten pages. Choose ZIP or folder and optionally include cropped review images.
4. Attach the package to ChatGPT. Follow `instructions.md`; keep the provenance and `[[PAGE:xxxx]]` markers in the returned text.
5. Import UTF-8 `.txt`, `.md`, or a package ZIP containing `proofread.txt`/`proofread.md`. Review validation and the character-count/diff summary, then explicitly save accepted pages.
6. Export DOCX. TateScribe uses ConfirmedText first, then ManualText, SuggestedText, and the reconstructed PaddleOCR draft. It warns before including unconfirmed pages.

TateScribe does not send images, OCR results, or text to external services. The ChatGPT exchange is performed manually by the user.
