# Ruby JSON format version 1

ChatGPT returns one JSON object with these fixed top-level properties:

```json
{
  "formatVersion": 1,
  "projectId": "GUID",
  "batchId": "GUID",
  "documentTextHash": "SHA256",
  "annotations": [
    {
      "paragraphId": "GUID",
      "start": 0,
      "length": 2,
      "baseText": "八角",
      "reading": "やすみ",
      "source": "ImageConfirmed",
      "confidence": 1.0,
      "evidencePageMarkers": ["0012"],
      "evidence": "原画像上のルビ"
    }
  ],
  "unresolved": [
    {
      "paragraphId": "GUID",
      "start": 0,
      "length": 2,
      "baseText": "万二",
      "evidencePageMarkers": ["0007"],
      "reason": "画像にも本文にも読みの根拠がない"
    }
  ]
}
```

`start` and `length` are UTF-16 code units. The selected substring must exactly equal `baseText`; surrogate pairs cannot be split. Allowed sources are `ImageConfirmed`, `TextConfirmed`, `UserConfirmed`, `DictionarySuggested`, and `ContextSuggested`.

Validation covers format/project/batch/document identity, current paragraph hashes, range and UTF-16 boundaries, exact base text, reading, source, confidence, duplicates, overlaps, evidence pages, ruby policy, and post-export staleness. Dictionary/context suggestions, low confidence, image-candidate mismatch, conflicting readings, and non-kana readings are warnings. Unknown JSON properties are rejected.

`ruby-candidates.json` uses `readingCandidate` for OCR text recognized in the ruby region and optional `baseTextCandidate` for a coordinate-linked Body region. `readingCandidate` is never treated as the parent text. Comparison applies Unicode width normalization, katakana-to-hiragana conversion, and whitespace removal while preserving long vowels and voiced marks; it never converts kanji or guesses from a dictionary. A null parent candidate produces `BaseTextCandidateUnknown`, not a false mismatch.

`ImageConfirmed` and `TextConfirmed` require at least one unique evidence page marker. `evidence`, `reading`, `baseText`, and unresolved `reason` cannot be empty. Validation issues carry paragraph/range/annotation keys so the review UI can show all warnings for the selected candidate.

The exact machine-readable schema is written to each ruby package as `output-schema.json`.
