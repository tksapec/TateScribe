# Word Ruby Layout and Review Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate Word-compatible ruby OOXML with a validated 3pt-default offset, and make ruby review selection and bulk-confirmation outcomes explicit and safe.

**Architecture:** Core owns ruby metrics, options, selection validation, and bulk result summaries. Infrastructure consumes immutable DOCX options to emit ruby XML. WPF only validates input, snapshots selected rows, renders details/results, and saves through the existing workflow. The XML comparator is diagnostic-only.

**Tech Stack:** C# / .NET 8, WPF, Open XML SDK, xUnit, PowerShell.

## Global Constraints

- Preserve OCR, proofreading, existing ruby records, JSON import, denden export, and schema-v9 SQLite data.
- Do not create a Release ZIP; use build/test scripts only with `-SkipArchive` when packaging is required.
- Keep Ruby bulk thresholds at annotation 0.70, OCR 0.70, link 0.60.
- Default Word offset is 3; allow only integer values 0 through 20.
- `hpsRaise = rubyFontSizeHalfPoints + wordOffsetPoints * 2` is provisional pending Word-saved reference files; document and test it as such.
- Do not alter body line spacing or non-ruby run formatting.

---

### Task 1: Ruby metrics, options, and XML diagnostic foundation

**Files:**
- Create: `src/TateScribe.Core/Export/DocxRubyOptions.cs`
- Create: `src/TateScribe.Core/Export/WordRubyMetrics.cs`
- Create: `scripts/compare-docx-ruby.ps1`
- Create: `tests/TateScribe.Tests/WordRubyMetricsTests.cs`
- Modify: `tests/TateScribe.Tests/DocxExportTests.cs`

**Interfaces:**
- Produces `DocxRubyOptions.Default`, `DocxRubyOptions.TryCreate(string?, out DocxRubyOptions, out string)`, and `WordRubyMetrics.CalculateRaiseHalfPoints(int, int)`.
- Consumed by the exporter and WPF export handler in Task 2.

- [ ] **Step 1: Write failing metric and input-validation tests**

```csharp
[Theory]
[InlineData(0, 10)]
[InlineData(3, 16)]
[InlineData(20, 50)]
public void Offset_maps_to_provisional_raise(int offset, int expected) =>
    Assert.Equal(expected, WordRubyMetrics.CalculateRaiseHalfPoints(10, offset));

[Theory]
[InlineData("", false)]
[InlineData("-1", false)]
[InlineData("21", false)]
[InlineData("3", true)]
public void Options_validate_word_offset(string value, bool valid) =>
    Assert.Equal(valid, DocxRubyOptions.TryCreate(value, out _, out _));
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Release --filter FullyQualifiedName~WordRubyMetricsTests`

Expected: compilation failure because the metrics/options types do not exist.

- [ ] **Step 3: Implement the immutable core model**

```csharp
public sealed record DocxRubyOptions(int WordOffsetPoints = 3, int RubyFontSizeHalfPoints = 10)
{
    public static DocxRubyOptions Default { get; } = new();
    public static bool TryCreate(string? value, out DocxRubyOptions options, out string error) { /* parse 0..20 */ }
}

public static class WordRubyMetrics
{
    public static int CalculateRaiseHalfPoints(int rubyFontSizeHalfPoints, int wordOffsetPoints) =>
        checked(rubyFontSizeHalfPoints + wordOffsetPoints * 2);
}
```

- [ ] **Step 4: Add a read-only DOCX ruby comparer**

Implement `compare-docx-ruby.ps1` accepting `-Path` with one or more DOCX files; read `word/document.xml` and `word/styles.xml` from `ZipArchive`, select `w:ruby` and ruby-relevant style nodes, canonicalize whitespace/attribute order, and print per-file normalized XML. Return a nonzero exit code for a missing file. Do not modify source DOCX files.

- [ ] **Step 5: Run focused tests and commit**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Release --filter FullyQualifiedName~WordRubyMetricsTests`

Expected: PASS.

```powershell
git add src/TateScribe.Core/Export/DocxRubyOptions.cs src/TateScribe.Core/Export/WordRubyMetrics.cs scripts/compare-docx-ruby.ps1 tests/TateScribe.Tests/WordRubyMetricsTests.cs tests/TateScribe.Tests/DocxExportTests.cs
git commit -m "feat: add Word ruby metrics and diagnostics"
```

### Task 2: DOCX ruby OOXML and export control

**Files:**
- Modify: `src/TateScribe.Infrastructure/Export/OpenXmlDocumentExporter.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Modify: `tests/TateScribe.Tests/DocxExportTests.cs`
- Modify: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes `DocxRubyOptions` and `WordRubyMetrics` from Task 1.
- Produces `OpenXmlDocumentExporter` overloads accepting ruby options while preserving legacy overload defaults.

- [ ] **Step 1: Write failing OOXML and layout tests**

```csharp
[Fact]
public async Task Ruby_uses_default_three_point_offset_and_explicit_japanese_run_properties()
{
    await new OpenXmlDocumentExporter().ExportAsync(document, path, false, "游明朝", DocxRubyOptions.Default, CancellationToken.None);
    Assert.Equal("16", rubyPr.GetFirstChild<PhoneticGuideRaise>()!.Val!.Value);
    Assert.Equal("游明朝", rubyContentRun.RunProperties!.RunFonts!.EastAsia!.Value);
    Assert.Equal("10", rubyContentRun.RunProperties.FontSize!.Val!.Value);
    Assert.Equal("21", rubyBaseRun.RunProperties.FontSize!.Val!.Value);
}
```

Assert the offset input name, default `3`, tooltip wording, and validation call in `MainWindowLayoutTests`.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Release --filter "FullyQualifiedName~DocxExportTests|FullyQualifiedName~MainWindowLayoutTests"`

Expected: missing exporter overload and missing UI control assertions.

- [ ] **Step 3: Implement options-aware ruby generation**

Add a legacy-compatible exporter overload forwarding `DocxRubyOptions.Default`. Pass the effective paragraph half-point size (21 normal, 32/28/24 headings) and Japanese font to `CreateRuby`. Build `RunProperties` for `RubyContent` and `RubyBase` with `RunFonts`, `FontSize`, `FontSizeComplexScript`, and `Languages { EastAsia = "ja-JP" }`; set `hpsBaseText` to that effective size and `hpsRaise` through `WordRubyMetrics`.

- [ ] **Step 4: Implement WPF input validation**

Add an offset TextBox/label in the DOCX export group with initial text `3`, range help, and explanatory tooltip. Before preparation/export, call `DocxRubyOptions.TryCreate`; on failure show the returned Japanese validation message and return without writing a file or snapshot. Retain the valid value in the control for the app session and pass options to the exporter.

- [ ] **Step 5: Run DOCX tests, validator tests, and commit**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Release --filter "FullyQualifiedName~DocxExportTests|FullyQualifiedName~MainWindowLayoutTests"`

Expected: PASS, including reopening the DOCX and `OpenXmlValidator` validation.

```powershell
git add src/TateScribe.Infrastructure/Export/OpenXmlDocumentExporter.cs src/TateScribe.App/MainWindow.xaml src/TateScribe.App/MainWindow.xaml.cs tests/TateScribe.Tests/DocxExportTests.cs tests/TateScribe.Tests/MainWindowLayoutTests.cs
git commit -m "feat: configure Word ruby offset in DOCX export"
```

### Task 3: Atomic review selection and bulk-confirm outcome model

**Files:**
- Create: `src/TateScribe.Core/Ruby/RubyReviewSelectionService.cs`
- Create: `tests/TateScribe.Tests/RubyReviewSelectionServiceTests.cs`
- Modify: `src/TateScribe.App/RubyReviewWindow.xaml`
- Modify: `src/TateScribe.App/RubyReviewWindow.xaml.cs`
- Modify: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`

**Interfaces:**
- Produces `RubyReviewSelectionService.ApplyStatus(IReadOnlyList<RubyAnnotationProposal>, RubyAnnotationStatus, Func<string,string?>)` and `RubyBulkConfirmationSummary`.
- WPF supplies selected snapshots and paragraph text; the service returns either a complete changed set or validation errors without partial mutation.

- [ ] **Step 1: Write failing atomic-selection and bulk-summary tests**

```csharp
[Fact]
public void Invalid_selected_item_leaves_every_selected_status_unchanged()
{
    var result = RubyReviewSelectionService.ApplyStatus([valid, invalid], RubyAnnotationStatus.Confirmed, textFor);
    Assert.False(result.IsSuccess);
    Assert.All(result.Items, item => Assert.Equal(RubyAnnotationStatus.Proposed, item.Status));
}

[Fact]
public void Explicit_selection_can_confirm_warning_candidate() { /* valid warning row -> Confirmed */ }
```

Add tests for noncontiguous selection, rejection, zero selection, unchanged unselected rows, UTF-16 invalid boundaries, empty readings, and summary reasons grouped by warning code.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Release --filter FullyQualifiedName~RubyReviewSelectionServiceTests`

Expected: compilation failure because the service and result types do not exist.

- [ ] **Step 3: Implement core batch action and result records**

Validate all selected proposals before returning replacements. Validate range, base substring, surrogate boundaries, and trimmed reading. Return immutable errors keyed by annotation ID/range. Add a `RubyBulkConfirmationSummary` which counts examined, newly confirmed, already confirmed, wrong source, excluded, warning codes, and validation errors without relaxing `RubyBulkConfirmationPolicy`.

- [ ] **Step 4: Wire WPF extended selection and explicit outcomes**

Set `SelectionMode="Extended"` and `SelectionUnit="FullRow"`. Commit grid edits, copy `SelectedItems.OfType<RubyAnnotationView>().ToArray()`, call the service, and replace only after a successful result. Rename buttons to 選択項目を確定 / 選択項目を却下. Keep the focused selected row for details/evidence. Rebuild summary after every selection, action, validation refresh, and bulk action. Display a MessageBox after every image/text bulk attempt, including invalid validation and zero candidates. Handle `Ctrl+Enter`; omit a reject shortcut to avoid DataGrid Delete conflicts.

- [ ] **Step 5: Run focused tests and commit**

Run: `dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Release --filter "FullyQualifiedName~RubyReviewSelectionServiceTests|FullyQualifiedName~RubyWorkflowTests|FullyQualifiedName~MainWindowLayoutTests"`

Expected: PASS with existing bulk threshold tests unchanged.

```powershell
git add src/TateScribe.Core/Ruby/RubyReviewSelectionService.cs tests/TateScribe.Tests/RubyReviewSelectionServiceTests.cs src/TateScribe.App/RubyReviewWindow.xaml src/TateScribe.App/RubyReviewWindow.xaml.cs tests/TateScribe.Tests/MainWindowLayoutTests.cs
git commit -m "feat: support atomic multi-select ruby review"
```

### Task 4: Documentation, compatibility verification, and release hygiene

**Files:**
- Modify: `README.md`
- Modify: `USER_GUIDE.md`
- Modify: `TEST_PLAN.md`
- Modify: `CHANGELOG.md`
- Modify: `SPEC.md`
- Modify: `ARCHITECTURE.md`
- Modify: `docs/ADR-0001-structured-ruby-boundary.md`

- [ ] **Step 1: Write documentation assertions or expand existing source-layout tests**

Add assertions for the offset default/range, explicit confirmation versus bulk confirmation, selected-count summary, and no schema migration. Ensure tests name the manual Word verification boundary.

- [ ] **Step 2: Update user and architecture documentation**

Document 3pt default, provisional calculation, re-export requirement, Word visual verification, Ctrl/Shift selection, Ctrl+Enter, button-only rejection, and all bulk result categories. State no schema migration and no Release ZIP.

- [ ] **Step 3: Exercise the diagnostic against the available direct-export DOCX**

Run: `./scripts/compare-docx-ruby.ps1 -Path 'C:/Users/e1399/Desktop/成瀬は天下を取りにいく/成瀬は天下を取りにいく.docx'`

Expected: normalized ruby XML reports `hps=10`, `hpsRaise=10`, `hpsBaseText=21`, and absent nested ruby run properties. Record that B/C comparison and Word visual confirmation remain pending because no Word-saved reference files are available.

- [ ] **Step 4: Run full verification without ZIP**

```powershell
dotnet restore
dotnet build TateScribe.sln -c Release
dotnet test TateScribe.sln -c Release
./scripts/test.ps1
git diff --check
```

Expected: 0 build warnings/errors, all .NET and Python tests pass, and no release ZIP appears in `artifacts`.

- [ ] **Step 5: Review, commit, and push**

```powershell
git add README.md USER_GUIDE.md TEST_PLAN.md CHANGELOG.md SPEC.md ARCHITECTURE.md docs/ADR-0001-structured-ruby-boundary.md
git commit -m "docs: explain Word ruby and review workflows"
git push origin main
git rev-list --left-right --count HEAD...origin/main
```

Expected: remote count `0 0`.

## Self-review

- DOCX offset, explicit properties, dynamic base size, comparison tool, and Word manual boundary are covered by Tasks 1, 2, and 4.
- Multi-select, atomic validation, details, summary, shortcuts, and bulk outcomes are covered by Task 3.
- Existing data, JSON/denden compatibility, no schema change, no ZIP, documentation, build, tests, review, and push are covered globally and in Task 4.
- No placeholder markers or unresolved type names remain; Task 1 defines options/metrics, Task 3 defines selection/bulk result interfaces.
