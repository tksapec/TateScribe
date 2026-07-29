# Microsoft Word ruby placement validation

TateScribe's default is the **3pt** offset users recognize in Word. The provisional conversion remains `hpsRaise = rubyFontSizeHalfPoints + wordOffsetPoints * 2`; with 5pt ruby (`rubyFontSizeHalfPoints=10`), it produces `hpsRaise=16`. XML `w:hpsRaise` and Word's displayed offset are not necessarily identical, so 3pt must not be changed to `w:hpsRaise=6` without Word-saved evidence.

## Comparison procedure

1. A: Export a DOCX from TateScribe with the 3pt default.
2. B: In Microsoft Word set its ruby offset to 3pt and save as a new file.
3. C: Set B back to 0pt and save as a new file.
4. Run the non-mutating `scripts/compare-docx-ruby.ps1 A.docx B.docx C.docx`.
5. Compare `word/document.xml`, `word/styles.xml`, `w:ruby`, `w:rubyPr`, `w:rubyAlign`, `w:hps`, `w:hpsRaise`, `w:hpsBaseText`, `w:lid`, RubyContent/RubyBase `w:rPr`, `w:rFonts`, `w:sz`, `w:szCs`, `w:lang`, and other ruby-related elements Word adds.

Automated tests and OpenXmlValidator cannot guarantee placement in Microsoft Word. Visual inspection in Word is the final acceptance condition. B/C comparison remains incomplete until Word-saved reference files exist. An environment without Word must not report “verified in Word”.
