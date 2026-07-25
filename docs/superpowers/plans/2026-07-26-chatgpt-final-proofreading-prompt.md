# ChatGPT Final Proofreading Prompt Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an editable, copyable instruction that asks ChatGPT to return a corrected DOCX after final OCR proofreading.

**Architecture:** Store the standard instruction in the Core project so its content is testable without WPF. A dedicated App window edits and copies that text, while `MainWindow` only owns the button and dialog launch.

**Tech Stack:** .NET 8, WPF XAML, xUnit.

## Global Constraints

- Preserve the existing OCR, proofreading-package, import, and DOCX-export behavior.
- Do not persist edits made in the prompt window.
- Keep all current uncommitted crop-layout and tooltip changes intact.
- If clipboard copying fails, keep the prompt visible and report the error.

---

### Task 1: Define and test the standard instruction

**Files:**
- Create: `src/TateScribe.Core/Proofreading/FinalProofreadingPrompt.cs`
- Create: `tests/TateScribe.Tests/FinalProofreadingPromptTests.cs`

**Interfaces:**
- Produces: `FinalProofreadingPrompt.Text`, a non-empty Japanese `string`.
- Consumes: no application or WPF state.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Text_requests_correction_as_a_docx_without_guessing_uncertain_passages()
{
    var text = FinalProofreadingPrompt.Text;
    Assert.Contains("OCR", text, StringComparison.Ordinal);
    Assert.Contains("誤字・脱字・文字化け", text, StringComparison.Ordinal);
    Assert.Contains("推測で確定せず", text, StringComparison.Ordinal);
    Assert.Contains("ユーザーに確認", text, StringComparison.Ordinal);
    Assert.Contains("修正を反映したDOCXファイル", text, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Verify the test fails**

Run: `dotnet test .\tests\TateScribe.Tests\TateScribe.Tests.csproj --no-restore --filter FullyQualifiedName~FinalProofreadingPromptTests`

Expected: compilation fails because `FinalProofreadingPrompt` does not exist.

- [ ] **Step 3: Add the minimal prompt model**

```csharp
namespace TateScribe.Core.Proofreading;

public static class FinalProofreadingPrompt
{
    public const string Text = """
        添付したDOCXファイルは、書籍の縦書き画面をOCRで読み取って作成したものです。
        誤字・脱字・文字化け・句読点の誤り・文脈上明らかな欠落がある可能性があるため、全文を校正し、適切に修正してください。
        原文の意味・文体・固有名詞を不用意に変更しないでください。
        原文画像がなく判断できない箇所や、複数の解釈が可能な箇所は推測で確定せず、ユーザーに確認してください。
        校正結果は、修正を反映したDOCXファイルとして返してください。
        """;
}
```

- [ ] **Step 4: Verify the focused test passes**

Run: `dotnet test .\tests\TateScribe.Tests\TateScribe.Tests.csproj --no-restore --filter FullyQualifiedName~FinalProofreadingPromptTests`

Expected: one passing test.

---

### Task 2: Add the prompt window and main-window entry point

**Files:**
- Create: `src/TateScribe.App/ChatGptProofreadingPromptWindow.xaml`
- Create: `src/TateScribe.App/ChatGptProofreadingPromptWindow.xaml.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml`
- Modify: `src/TateScribe.App/MainWindow.xaml.cs`
- Modify: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes: `FinalProofreadingPrompt.Text`.
- Produces: modal `ChatGptProofreadingPromptWindow` with `PromptEditor`, `CopyStatus`, copy, and close controls.

- [ ] **Step 1: Add the failing source-shape test**

```csharp
[Fact]
public void Main_window_exposes_an_editable_copyable_final_proofreading_prompt()
{
    var root = FindRepositoryRoot();
    var mainXaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));
    var promptXaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "ChatGptProofreadingPromptWindow.xaml"));
    Assert.Contains("Click=\"ShowChatGptProofreadingPrompt\"", mainXaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"PromptEditor\"", promptXaml, StringComparison.Ordinal);
    Assert.Contains("Click=\"CopyPrompt\"", promptXaml, StringComparison.Ordinal);
    Assert.Contains("Click=\"CloseWindow\"", promptXaml, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Verify the source-shape test fails**

Run: `dotnet test .\tests\TateScribe.Tests\TateScribe.Tests.csproj --no-restore --filter FullyQualifiedName~MainWindowLayoutTests`

Expected: failure because the prompt-window XAML is absent.

- [ ] **Step 3: Implement the WPF window**

Create a modal window with an editable multiline `TextBox`, a copy status
`TextBlock`, and copy/close buttons. Initialize the editor from
`FinalProofreadingPrompt.Text`. `CopyPrompt` calls
`Clipboard.SetText(PromptEditor.Text)` and catches exceptions to show an error
without closing the window.

- [ ] **Step 4: Connect the main-window button**

Add `ChatGPT最終校正用の指示` next to the DOCX export button and implement:

```csharp
private void ShowChatGptProofreadingPrompt(object sender, RoutedEventArgs e)
{
    new ChatGptProofreadingPromptWindow { Owner = this }.ShowDialog();
}
```

- [ ] **Step 5: Verify tests and build**

Run: `.\scripts\test.ps1`

Expected: all .NET and Python tests pass.

Run: `dotnet build .\src\TateScribe.App\TateScribe.App.csproj -c Release --no-restore`

Expected: zero warnings and zero errors.
