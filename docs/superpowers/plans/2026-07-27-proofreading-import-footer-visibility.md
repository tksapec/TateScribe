# Proofreading Import Footer Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the proofreading import window's save and cancel buttons visible when validation details contain many lines.

**Architecture:** Preserve the existing four-row window layout and data flow. Bound only the summary/details area with an internal vertical scrollbar so the candidate grid absorbs the remaining height and the footer remains in its `Auto` row.

**Tech Stack:** .NET 8, WPF XAML, xUnit source-layout regression tests.

**Status:** Implemented and verified on 2026-07-27.

## Global Constraints

- Do not change proofreading validation, candidate selection, or save behavior.
- Do not make the complete window scrollable.
- Keep the candidate grid in the `*` row and the action footer in the last `Auto` row.
- Do not create or replace the release ZIP.
- Verify, commit, and push the completed change to `origin/main`.

---

### Task 1: Bound the details area and protect the footer

**Files:**
- Modify: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`
- Modify: `src/TateScribe.App/ProofreadingImportWindow.xaml`

**Interfaces:**
- Consumes: existing `Summary`, `CandidateGrid`, `Cancel`, and `Accept` event handlers.
- Produces: named `SummaryScrollViewer`, `CancelImportButton`, and `AcceptImportButton` XAML elements.

- [ ] **Step 1: Write the failing layout regression test**

Add these assertions to `Proofreading_import_window_exposes_before_after_diff_and_bulk_selection`:

```csharp
Assert.Contains("x:Name=\"SummaryScrollViewer\"", xaml, StringComparison.Ordinal);
Assert.Contains("MaxHeight=\"120\"", xaml, StringComparison.Ordinal);
Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
Assert.Contains("x:Name=\"CancelImportButton\"", xaml, StringComparison.Ordinal);
Assert.Contains("x:Name=\"AcceptImportButton\"", xaml, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test tests/TateScribe.Tests/TateScribe.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~Proofreading_import_window_exposes_before_after_diff_and_bulk_selection"
```

Expected: FAIL because `SummaryScrollViewer` is absent.

- [ ] **Step 3: Implement the minimal XAML change**

Replace the standalone summary text with:

```xml
<ScrollViewer x:Name="SummaryScrollViewer"
              MaxHeight="120"
              VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Disabled"
              Margin="0,0,0,12">
  <TextBlock x:Name="Summary" TextWrapping="Wrap" />
</ScrollViewer>
```

Name the footer buttons without changing their handlers:

```xml
<Button x:Name="CancelImportButton"
        Padding="12,6" Margin="0,0,8,0"
        Click="Cancel">キャンセル（保存しない）</Button>
<Button x:Name="AcceptImportButton"
        Padding="12,6" IsDefault="True"
        Click="Accept">選択ページを保存</Button>
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command.

Expected: PASS.

- [ ] **Step 5: Run complete verification**

Run:

```powershell
dotnet build TateScribe.sln -c Debug --no-restore
.\scripts\build.ps1
.\scripts\test.ps1
git diff --check
```

Expected: Debug and Release builds succeed with zero errors, all .NET and Python tests pass, and `git diff --check` reports no errors.

- [ ] **Step 6: Verify the fixed-size window**

Launch the Debug app with a proofreading import containing enough validation details to exceed 120 device-independent pixels. Confirm that the summary has its own scrollbar and both footer buttons remain visible at `MinHeight="420"`.

- [ ] **Step 7: Commit and push**

```powershell
git add docs/superpowers/plans/2026-07-27-proofreading-import-footer-visibility.md `
        src/TateScribe.App/ProofreadingImportWindow.xaml `
        tests/TateScribe.Tests/MainWindowLayoutTests.cs
git commit -m "fix: keep proofreading import actions visible"
git push origin main
git rev-list --left-right --count HEAD...origin/main
```

Expected: push succeeds and divergence is `0 0`.
