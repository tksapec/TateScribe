# Safe OCR Supplement Merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore reliably aligned punctuation, quotes, small kana, long vowels, and newlines without replacing OCR body text.

**Architecture:** `PunctuationMerger` remains the only integration point. It derives supplementary characters from short Tesseract gaps bounded by existing LCS anchors, then inserts them into the Paddle text while discarding all non-supplementary auxiliary characters.

**Tech Stack:** .NET 8, C#, xUnit.

## Global Constraints

- Do not replace a normal body character from the Tesseract result.
- Require existing reliable LCS context and a bounded gap before inserting a supplementary character.
- Keep `testdata/` out of Git.

---

### Task 1: Specify safe mixed-gap recovery

**Files:**
- Modify: `tests/TateScribe.Tests/PunctuationMergerTests.cs`

**Interfaces:**
- Consumes: `PunctuationMerger.Merge(string primary, string auxiliary, int lookAhead)`.
- Produces: regression coverage for trusted supplementary recovery.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Merge_recovers_a_long_vowel_when_auxiliary_has_an_adjacent_body_error()
{
    var result = PunctuationMerger.Merge("会議テブル", "会識テナーブル", 16);

    Assert.Equal("会議テーブル", result);
}

[Fact]
public void Merge_recovers_leading_quotes_without_copying_auxiliary_body_errors()
{
    var result = PunctuationMerger.Merge("原島はいったいつものことさ", "原島はいった。「ハつものことさ」9", 16);

    Assert.Equal("原島はいった。「いつものことさ」", result);
}

[Fact]
public void Merge_does_not_move_a_supplementary_character_past_an_auxiliary_body_error()
{
    var result = PunctuationMerger.Merge("漢字本文", "漢子ゃ本文", 16);

    Assert.Equal("漢字本文", result);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj --filter "FullyQualifiedName~PunctuationMergerTests"`

Expected: the two recovery tests fail while the non-movement test protects against an unsafe implementation.

### Task 2: Insert only trusted supplementary characters

**Files:**
- Modify: `src/TateScribe.Core/Layout/PunctuationMerger.cs`
- Test: `tests/TateScribe.Tests/PunctuationMergerTests.cs`

**Interfaces:**
- Consumes: `primaryGap`, `auxiliaryGap`, and existing `hasReliableContext` from `AddSupplementaryGap`.
- Produces: `TryAddTrustedSupplementaryGap(...)`, which appends only characters accepted by `IsSupplementaryCharacter`.

- [ ] **Step 1: Implement the smallest helper**

```csharp
private static bool TryAddTrustedSupplementaryGap(
    ReadOnlySpan<char> primaryGap,
    ReadOnlySpan<char> auxiliaryGap,
    Dictionary<int, StringBuilder> insertions,
    int primaryStart)
{
    if (primaryGap.Length == 0)
    {
        var recovered = string.Concat(auxiliaryGap.ToArray().Where(IsSupplementaryCharacter));
        if (recovered.Length == 0) return false;
        AddInsertion(insertions, primaryStart, recovered.AsSpan());
        return true;
    }

    var count = LeadingSupplementaryCharacterCount(auxiliaryGap);
    if (count == 0) return false;
    AddInsertion(insertions, primaryStart, auxiliaryGap[..count]);
    return true;
}
```

Call it from `AddSupplementaryGap` only after `hasReliableContext` is true and both gaps are at most 12 characters. Retain existing specialized rules after the helper so they handle their current cases.

- [ ] **Step 2: Run focused tests and verify they pass**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj --filter "FullyQualifiedName~PunctuationMergerTests"`

Expected: all `PunctuationMergerTests` pass.

### Task 3: Verify integration and publish

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-safe-ocr-supplement-design.md`
- Modify: `docs/superpowers/plans/2026-07-25-safe-ocr-supplement-merge.md`

- [ ] **Step 1: Run the complete test suite and Release build**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj && dotnet build TateScribe.sln -c Release`

Expected: zero failed tests and zero build errors.

- [ ] **Step 2: Publish Windows executable**

Run: `dotnet publish src/TateScribe.App/TateScribe.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/TateScribe-win-x64`

Expected: `artifacts/TateScribe-win-x64/TateScribe.App.exe` is created with a new timestamp.

- [ ] **Step 3: Commit the implementation**

```powershell
git add docs/superpowers src/TateScribe.Core/Layout/PunctuationMerger.cs tests/TateScribe.Tests/PunctuationMergerTests.cs
git commit -m "fix: recover trusted OCR supplementary characters"
git push origin main
```
