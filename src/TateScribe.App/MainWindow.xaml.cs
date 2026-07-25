using Microsoft.Win32;
using System.IO;
using System.Windows;
using TateScribe.Core.Projects;
using TateScribe.Core.Export;
using TateScribe.Infrastructure.Export;
using TateScribe.Infrastructure.Import;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App;

public partial class MainWindow : Window
{
    private string? _projectDirectory;
    private List<ProjectPage> _pages = [];

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void CreateProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "TateScribe プロジェクト用フォルダーを選択" };
        if (dialog.ShowDialog(this) == true)
        {
            _projectDirectory = dialog.FolderName;
            Title = $"TateScribe — {_projectDirectory}";
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
            if (_pages.Count == 0)
            {
                var images = Directory.EnumerateFiles(_projectDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                if (images.Length > 0)
                {
                    _pages = (await new ImageImporter().ImportAsync(images, CancellationToken.None)).ToList();
                    await repository.SavePagesAsync(_pages, CancellationToken.None);
                }
            }
            RefreshPages();
        }
    }

    private async void ImportImages(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_projectDirectory))
        {
            MessageBox.Show(this, "先にプロジェクト用フォルダーを選択してください。", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.webp"
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                IsEnabled = false;
                var pages = await new ImageImporter().ImportAsync(dialog.FileNames, CancellationToken.None);
                await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
                await repository.SavePagesAsync(pages, CancellationToken.None);
                _pages = pages.ToList();
                RefreshPages();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "画像の追加に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }

    private async void MovePageUp(object sender, RoutedEventArgs e) => await MoveSelectedPageAsync(-1);

    private async void MovePageDown(object sender, RoutedEventArgs e) => await MoveSelectedPageAsync(1);

    private async Task MoveSelectedPageAsync(int offset)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        _pages = PageOrderEditor.Move(_pages, selected.Id, offset).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private void RefreshPages() => PageList.ItemsSource = _pages.OrderBy(page => page.SortOrder).ToArray();

    private async void PageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        var textState = await repository.LoadPageTextStateAsync(selected.Id, CancellationToken.None);
        TextEditor.Text = textState.ManualText ?? string.Concat(textState.MachineWords.Select(word => word.Text));
    }

    private async void SaveManualText(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SaveManualTextAsync(selected.Id, TextEditor.Text, CancellationToken.None);
    }

    private async void ExportDocx(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || _pages.Count == 0) return;
        try
        {
            IsEnabled = false;
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            var blocks = new List<ExportParagraph>();
            foreach (var page in _pages.Where(page => page.IsIncluded).OrderBy(page => page.SortOrder))
            {
                var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);
                var text = state.ManualText ?? string.Concat(state.MachineWords.Select(word => word.Text));
                if (!string.IsNullOrWhiteSpace(text)) blocks.Add(new ExportParagraph(ExportStyle.Normal, text));
            }
            var outputPath = BookFolderPaths.GetDocumentPath(_projectDirectory);
            await new OpenXmlDocumentExporter().ExportAsync(new ExportDocument(blocks), outputPath, CancellationToken.None);
            MessageBox.Show(this, $"DOCXを出力しました。{Environment.NewLine}{outputPath}", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "DOCX出力に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }
}
