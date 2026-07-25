namespace TateScribe.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void Crop_controls_use_a_two_row_grid_with_all_four_named_inputs()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));

        Assert.Contains("<Grid HorizontalAlignment=\"Center\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropLeftPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropTopPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropBottomPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CropRightPercent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
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
    public void Main_window_exposes_an_editable_copyable_final_proofreading_prompt()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "TateScribe.App", "MainWindow.xaml"));
        var promptPath = Path.Combine(root, "src", "TateScribe.App", "ChatGptProofreadingPromptWindow.xaml");

        Assert.Contains("Click=\"ShowChatGptProofreadingPrompt\"", mainXaml, StringComparison.Ordinal);
        Assert.True(File.Exists(promptPath), $"Prompt window XAML was not found: {promptPath}");
        var promptXaml = File.ReadAllText(promptPath);
        Assert.Contains("x:Name=\"PromptEditor\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CopyStatus\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CopyPrompt\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CloseWindow\"", promptXaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("TateScribe.sln");
    }
}
