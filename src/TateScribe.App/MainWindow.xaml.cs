using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using TateScribe.Core.Projects;
using TateScribe.Core.Export;
using TateScribe.Infrastructure.Export;
using TateScribe.Infrastructure.Ocr;
using TateScribe.Core.Ocr;
using TateScribe.Core.Layout;
using TateScribe.Core.Images;
using TateScribe.Infrastructure.Images;
using TateScribe.Infrastructure.Import;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App;

public partial class MainWindow : Window
{
    private string? _projectDirectory;
    private List<ProjectPage> _pages = [];
    private CancellationTokenSource? _ocrCancellation;

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
                var mergedPages = PageImportMerger.AppendNew(_pages, pages);
                await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
                await repository.SavePagesAsync(mergedPages, CancellationToken.None);
                _pages = mergedPages.ToList();
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

    private async void RotatePageLeft(object sender, RoutedEventArgs e) => await RotateSelectedPageAsync(-90);

    private async void RotatePageRight(object sender, RoutedEventArgs e) => await RotateSelectedPageAsync(90);

    private async void TogglePageUsage(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        _pages = _pages.Select(page => page.Id == selected.Id ? PageUsageEditor.Toggle(page) : page).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private async Task MoveSelectedPageAsync(int offset)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        _pages = PageOrderEditor.Move(_pages, selected.Id, offset).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private async Task RotateSelectedPageAsync(int degrees)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        _pages = _pages.Select(page => page.Id == selected.Id ? PageRotationEditor.Rotate(page, degrees) : page).ToList();
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
        var reconstruction = VerticalTextReconstruction.Reconstruct(textState.MachineWords, 20, 0.75);
        TextEditor.Text = textState.ManualText ?? reconstruction.Text;
        var source = new BitmapImage(new Uri(selected.SourcePath, UriKind.Absolute));
        PagePreview.Source = selected.RotationDegrees == 0
            ? source
            : new TransformedBitmap(source, new System.Windows.Media.RotateTransform(selected.RotationDegrees));
        ReviewStatus.Text = reconstruction.ReviewItems.Count == 0
            ? "要確認の低信頼度文字はありません。"
            : $"要確認: 低信頼度文字 {reconstruction.ReviewItems.Count} 件";
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
            var pageTexts = new List<string>();
            foreach (var page in _pages.Where(page => page.IsIncluded).OrderBy(page => page.SortOrder))
            {
                var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);
                var text = state.ManualText ?? VerticalTextReconstruction.Reconstruct(state.MachineWords, 20, 0.75).Text;
                if (!string.IsNullOrWhiteSpace(text)) pageTexts.Add(text);
            }
            var outputPath = BookFolderPaths.GetDocumentPath(_projectDirectory);
            await new OpenXmlDocumentExporter().ExportAsync(BookDocumentAssembler.Assemble(pageTexts), outputPath, CancellationToken.None);
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

    private async void RunOcr(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        await RunOcrAsync([selected]);
    }

    private async void RunAllOcr(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || _pages.Count == 0) return;
        await RunOcrAsync(_pages.OrderBy(page => page.SortOrder).ToArray());
    }

    private async Task RunOcrAsync(IReadOnlyList<ProjectPage> pages)
    {
        var projectDirectory = _projectDirectory;
        if (projectDirectory is null) return;
        if (_ocrCancellation is not null) return;
        var python = ResolveRuntimePath("ocr-runtime", "Scripts", "python.exe");
        var workerScript = ResolveRuntimePath("ocr-worker", "worker.py");
        if (!File.Exists(python) || !File.Exists(workerScript))
        {
            MessageBox.Show(this, "ローカルOCRランタイムが見つかりません。scripts/setup-ocr.ps1 を実行してください。", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            _ocrCancellation = new CancellationTokenSource();
            RunOcrButton.IsEnabled = false;
            RunAllOcrButton.IsEnabled = false;
            CancelOcrButton.IsEnabled = true;
            await using var worker = new JsonLinesOcrWorker(python, workerScript);
            await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, CancellationToken.None);
            var preprocessor = new ScreenshotPreprocessor();
            var cacheDirectory = Path.Combine(projectDirectory, ".tatescribe-cache");
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                ReviewStatus.Text = $"OCR実行中: {index + 1}/{pages.Count} {page.FileName}";
                var prepared = await preprocessor.PrepareAsync(page.SourcePath, cacheDirectory, NormalizedCrop.Full, page.RotationDegrees, _ocrCancellation.Token);
                var result = await worker.RecognizeAsync(new OcrRequest(Guid.NewGuid().ToString("N"), "paddle", prepared.CachePath), _ocrCancellation.Token);
                await repository.ReplaceOcrWordsAsync(page.Id, result.Engine, result.ModelVersion, result.Words, _ocrCancellation.Token);
                if (PageList.SelectedItem is ProjectPage selected && selected.Id == page.Id)
                {
                    TextEditor.Text = VerticalTextReconstruction.Reconstruct(result.Words, 20, 0.75).Text;
                }
            }
            ReviewStatus.Text = $"OCR完了: {pages.Count} ページ";
        }
        catch (OcrWorkerException exception)
        {
            MessageBox.Show(this, exception.Message, "OCRを実行できません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (OperationCanceledException)
        {
            ReviewStatus.Text = "OCRを中止しました。完了済みのOCR結果は保持されています。";
        }
        finally
        {
            _ocrCancellation?.Dispose();
            _ocrCancellation = null;
            RunOcrButton.IsEnabled = true;
            RunAllOcrButton.IsEnabled = true;
            CancelOcrButton.IsEnabled = false;
        }
    }

    private void CancelOcr(object sender, RoutedEventArgs e) => _ocrCancellation?.Cancel();

    private static string ResolveRuntimePath(params string[] parts)
    {
        var packagedPath = Path.Combine([AppContext.BaseDirectory, .. parts]);
        return File.Exists(packagedPath) ? packagedPath : Path.Combine([Directory.GetCurrentDirectory(), .. parts]);
    }
}
