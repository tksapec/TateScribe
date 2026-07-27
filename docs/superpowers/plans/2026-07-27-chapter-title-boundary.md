# Chapter Title Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure a `ChapterTitle` page remains a real DOCX heading with an optional page break even when the preceding page boundary is `DirectJoin`.

**Architecture:** Keep chapter metadata encoded as the existing structure marker. At the shared page-assembly boundary, force a single line break before a next page whose first line is a supported complete structure marker; mirror the behavior in source-aware assembly.

**Tech Stack:** .NET 8, C#, xUnit, Open XML SDK.

**Status:** Implemented and verified on 2026-07-27.

## Global Constraints

- Do not modify stored proofreading text or page-boundary settings.
- Preserve all join behavior between ordinary body pages.
- Keep legacy and source-aware assembly results identical.
- Do not create or replace the release ZIP.
- Verify, commit, and push the completed change to `origin/main`.

---

### Task 1: Protect structural page boundaries

**Files:**
- Modify: `tests/TateScribe.Tests/BookDocumentAssemblerTests.cs`
- Modify: `tests/TateScribe.Tests/DocxExportTests.cs`
- Modify: `src/TateScribe.Core/Export/BookDocumentAssembler.cs`

**Interfaces:**
- Consumes: `BookDocumentAssembler.Assemble`, `AssembleWithSourceSpans`,
  `ExportPageText`, and `ExportSourcePageText`.
- Produces: private structure-marker boundary detection shared by both assembly
  paths; no public API changes.

- [ ] **Step 1: Add failing assembler regression tests**

Add tests equivalent to:

```csharp
[Fact]
public void Assemble_separates_a_chapter_page_after_a_direct_join_body_page()
{
    var chapter = BookDocumentAssembler.CreateChapterPageText("Chapter title");
    var document = BookDocumentAssembler.Assemble([
        new ExportPageText("Previous body", BoundaryJoinType.DirectJoin),
        new ExportPageText(chapter, BoundaryJoinType.DirectJoin),
    ]);

    Assert.Collection(document.Paragraphs,
        paragraph => Assert.Equal("Previous body", paragraph.Text),
        paragraph => Assert.Equal(
            (DocumentElementRole.ChapterTitle, "Chapter title"),
            (paragraph.Role, paragraph.Text)));
}
```

Add a source-aware counterpart and assert that the chapter paragraph's single
source span belongs to the chapter page.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~BookDocumentAssemblerTests"
```

Expected: the new direct-join chapter tests fail because the old assembler
returns one body paragraph containing `[[CHAPTER:`.

- [ ] **Step 3: Implement the minimal boundary rule**

In both join loops, after the existing last-page/line-break checks and before
the `BoundaryJoinType` switch, detect whether the next page starts with a
complete supported structure-marker line. Append `"\n"` and continue when it
does.

Extract structure-line recognition so the boundary check and
`IsStandaloneStructureMarker` use the same supported prefixes:

```csharp
private static bool StartsWithStructureMarker(string text)
{
    var lineEnd = text.IndexOfAny(['\r', '\n']);
    var firstLine = lineEnd < 0 ? text : text[..lineEnd];
    return IsStructureMarkerLine(firstLine.Trim());
}

private static bool IsStructureMarkerLine(string line) =>
    line.EndsWith("]]", StringComparison.Ordinal)
    && (line.StartsWith("[[CHAPTER:", StringComparison.Ordinal)
        || line.StartsWith("[[TITLE:", StringComparison.Ordinal)
        || line.StartsWith("[[SECTION_TITLE:", StringComparison.Ordinal)
        || line.StartsWith("[[SECTION:", StringComparison.Ordinal));
```

- [ ] **Step 4: Verify GREEN**

Run the command from Step 2.

Expected: all `BookDocumentAssemblerTests` pass.

- [ ] **Step 5: Add and run the DOCX regression**

Assemble a direct-join body page followed by a chapter page, set
`PageBreakBeforeChapters` to `true`, export it, and assert:

```csharp
Assert.DoesNotContain("[[CHAPTER:", xml, StringComparison.Ordinal);
Assert.Contains("Heading1", xml, StringComparison.Ordinal);
Assert.Contains("pageBreakBefore", xml, StringComparison.OrdinalIgnoreCase);
```

Run:

```powershell
dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DocxExportTests"
```

Expected: all `DocxExportTests` pass.

- [ ] **Step 6: Run complete verification**

Run:

```powershell
dotnet build TateScribe.sln -c Debug --no-restore
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -SkipArchive
git diff --check
```

Confirm the release ZIP was neither created nor modified.

- [ ] **Step 7: Commit and push**

```powershell
git add docs/superpowers/specs/2026-07-27-chapter-title-boundary-design.md `
        docs/superpowers/plans/2026-07-27-chapter-title-boundary.md `
        src/TateScribe.Core/Export/BookDocumentAssembler.cs `
        tests/TateScribe.Tests/BookDocumentAssemblerTests.cs `
        tests/TateScribe.Tests/DocxExportTests.cs
git commit -m "fix: preserve chapter title boundaries"
git push origin main
git rev-list --left-right --count HEAD...origin/main
```

Expected: push succeeds and divergence is `0 0`.
