using System.Xml.Linq;

namespace TateScribe.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void Sidebar_is_scrollable_and_crop_controls_keep_all_four_labeled_inputs_visible()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"SidebarScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"画像の除外範囲（%）\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropLeftPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropTopPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropBottomPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropRightPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"64\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_structure_controls_include_examples_in_tooltips()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));

        Assert.Contains("ChapterTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("ReflowVertical", xaml, StringComparison.Ordinal);
        Assert.Contains("FixedPageVertical", xaml, StringComparison.Ordinal);
        Assert.Contains("Illustration", xaml, StringComparison.Ordinal);
        Assert.Contains("ページ種別・表示形式を保存", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_exposes_task_specific_editable_copyable_chatgpt_prompts()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));
        var promptPath = Path.Combine(root, "src", "TateScribe.App", "ChatGptProofreadingPromptWindow.xaml");

        Assert.Contains("Click=\"ShowChatGptProofreadingPrompt\"", mainXaml, StringComparison.Ordinal);
        Assert.True(File.Exists(promptPath), $"Prompt window XAML was not found: {promptPath}");
        var promptXaml = File.ReadAllText(promptPath);
        Assert.Contains("x:Name=\"PromptEditor\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TaskTypeSelector\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("RubyAnnotation", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ResetPrompt\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CopyStatus\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CopyPrompt\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CloseWindow\"", promptXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_exposes_ruby_package_review_and_denden_workflows()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));
        var review = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "RubyReviewWindow.xaml"));
        var denden = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "DendenExportWindow.xaml"));

        Assert.Contains("x:Name=\"RubyPolicySelector\"", main, StringComparison.Ordinal);
        Assert.Contains("Click=\"ExportRubyPackage\"", main, StringComparison.Ordinal);
        Assert.Contains("Click=\"ImportRubyJson\"", main, StringComparison.Ordinal);
        Assert.Contains("Click=\"ReviewSavedRuby\"", main, StringComparison.Ordinal);
        Assert.Contains("Click=\"ExportDenden\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AnnotationGrid\"", review, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", review, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnresolvedGrid\"", review, StringComparison.Ordinal);
        Assert.Contains("画像根拠だけ一括確定", review, StringComparison.Ordinal);
        Assert.Contains("本文根拠だけ一括確定", review, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DirectionSelector\"", denden, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TocDepthEditor\"", denden, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IncludeIllustrationsCheck\"", denden, StringComparison.Ordinal);
        Assert.Contains("通常のOCR画面は含めません", denden, StringComparison.Ordinal);
    }

    [Fact]
    public void Proofreading_import_window_exposes_before_after_diff_and_bulk_selection()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "TateScribe.App", "ProofreadingImportWindow.xaml");
        var xaml = File.ReadAllText(path);
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var rootGrid = Assert.Single(document.Root!.Elements(presentation + "Grid"));
        var rows = rootGrid.Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition")
            .Select(row => row.Attribute("Height")?.Value)
            .ToArray();
        var summaryScrollViewer = rootGrid.Elements(presentation + "ScrollViewer")
            .Single(element => element.Attribute(x + "Name")?.Value == "SummaryScrollViewer");
        var summary = Assert.Single(summaryScrollViewer.Elements(presentation + "TextBlock"));
        var candidateGrid = rootGrid.Elements(presentation + "DataGrid")
            .Single(element => element.Attribute(x + "Name")?.Value == "CandidateGrid");
        var footer = rootGrid.Elements(presentation + "StackPanel")
            .Single(element => element.Attribute("Grid.Row")?.Value == "3");

        Assert.Contains("x:Name=\"SelectAllButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearAllButton\"", xaml, StringComparison.Ordinal);
        Assert.Collection(
            rows,
            row => Assert.Equal("Auto", row),
            row => Assert.Equal("Auto", row),
            row => Assert.Equal("*", row),
            row => Assert.Equal("Auto", row));
        Assert.Equal("120", summaryScrollViewer.Attribute("MaxHeight")?.Value);
        Assert.Equal("Auto", summaryScrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("True", summaryScrollViewer.Attribute("Focusable")?.Value);
        Assert.Equal("校正結果の検証詳細", summaryScrollViewer.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Summary", summary.Attribute(x + "Name")?.Value);
        Assert.Equal("2", candidateGrid.Attribute("Grid.Row")?.Value);
        Assert.Contains(
            footer.Elements(presentation + "Button"),
            element => element.Attribute(x + "Name")?.Value == "CancelImportButton");
        Assert.Contains(
            footer.Elements(presentation + "Button"),
            element => element.Attribute(x + "Name")?.Value == "AcceptImportButton");
        Assert.Contains("x:Name=\"WarningsOnly\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding BeforeText, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding AfterText, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding DiffSummary}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding InlineSpans}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Background}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_exposes_text_history_and_ruby_candidate_review()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));
        var review = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "PageReviewWindow.xaml"));

        Assert.Contains("x:Name=\"PageReviewButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoryGrid\"", review, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RubyGrid\"", review, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RubyOverlay\"", review, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_routes_workflows_through_focused_services()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml.cs"));

        foreach (var service in new[]
                 {
                     "OcrOrchestrationService", "ProofreadingPackageService",
                     "ProofreadingImportService", "DocumentExportService", "PageValidationService",
                     "RubyWorkflowService", "DendenExportService"
                 })
            Assert.Contains(service, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ruby_review_revalidates_saved_and_live_edited_candidates()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "TateScribe.App", "Services", "RubyWorkflowService.cs"));
        var review = File.ReadAllText(Path.Combine(
            root, "src", "TateScribe.App", "RubyReviewWindow.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(
            root, "src", "TateScribe.App", "MainWindow.xaml.cs"));

        Assert.Contains(
            "return new RubyImportResult(batch, ValidateReviewed(batch, import));",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("validateReviewed?.Invoke(current)", review, StringComparison.Ordinal);
        Assert.Equal(
            2,
            Count(main, "reviewed => service.ValidateReviewed(result.Batch, reviewed)"));
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        for (var index = 0;
             (index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0;
             index += fragment.Length)
            count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("TateScribe.sln");
    }
}
