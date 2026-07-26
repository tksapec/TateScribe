using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
using TateScribe.Core.Proofreading;
using TateScribe.Infrastructure.Proofreading;
using TateScribe.App.Services;

namespace TateScribe.App;

public partial class MainWindow : Window
{
    private string? _projectDirectory;
    private List<ProjectPage> _pages = [];
    private CancellationTokenSource? _ocrCancellation;
    private double _previewZoom = 1;

    public MainWindow()
    {
        InitializeComponent();
        DisplayProfileSelector.ItemsSource = Enum.GetValues<DisplayProfile>();
        PageRoleSelector.ItemsSource = Enum.GetValues<PageRole>();
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
            await ValidatePagesAsync(repository);
            _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
            RefreshPages();
            var missingSourceCount = _pages.Count(page => !File.Exists(page.SourcePath));
            if (missingSourceCount > 0) ReviewStatus.Text = $"要確認: 元画像が見つからないページが {missingSourceCount} 件あります。";
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
                await ValidatePagesAsync(repository);
                _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
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
        await ValidatePagesAsync(repository);
        _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private async Task MoveSelectedPageAsync(int offset)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        _pages = PageOrderEditor.Move(_pages, selected.Id, offset).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        await ValidatePagesAsync(repository);
        _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private async Task RotateSelectedPageAsync(int degrees)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        _pages = _pages.Select(page => page.Id == selected.Id ? PageRotationEditor.Rotate(page, degrees) : page).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        await ValidatePagesAsync(repository);
        _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private async void ApplyCrop(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        if (!TryReadCrop(out var crop))
        {
            MessageBox.Show(this, "4辺の除外率は、各方向の合計が100%未満になる数値で指定してください。", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _pages = _pages.Select(page => page.Id == selected.Id ? page with { Crop = crop } : page).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        await ValidatePagesAsync(repository);
        _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private async void ApplyCropToAll(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || !TryReadCrop(out var crop)) return;
        _pages = _pages.Select(page => page with { Crop = crop }).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        RefreshPages();
        UpdateCropOverlay(crop);
    }

    private bool TryReadCrop(out NormalizedCrop crop)
    {
        crop = NormalizedCrop.Full;
        if (!double.TryParse(CropLeftPercent.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var leftPercent) ||
            !double.TryParse(CropTopPercent.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var topPercent) ||
            !double.TryParse(CropBottomPercent.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var bottomPercent) ||
            !double.TryParse(CropRightPercent.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rightPercent) ||
            leftPercent < 0 || rightPercent < 0 || topPercent < 0 || bottomPercent < 0 || leftPercent + rightPercent >= 100 || topPercent + bottomPercent >= 100) return false;
        crop = new NormalizedCrop(leftPercent / 100, topPercent / 100, 1 - rightPercent / 100, 1 - bottomPercent / 100);
        return true;
    }

    private void RefreshPages() => PageList.ItemsSource = _pages.OrderBy(page => page.SortOrder).ToArray();

    private async void PageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        if (!File.Exists(selected.SourcePath))
        {
            PagePreview.Source = null;
            TextEditor.Text = string.Empty;
            ReviewStatus.Text = $"要確認: 元画像が見つかりません。{selected.SourcePath}";
            return;
        }
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        var textState = await repository.LoadPageTextStateAsync(selected.Id, CancellationToken.None);
        var crop = selected.Crop ?? NormalizedCrop.Full;
        CropLeftPercent.Text = (crop.Left * 100).ToString("0.##", CultureInfo.CurrentCulture);
        CropTopPercent.Text = (crop.Top * 100).ToString("0.##", CultureInfo.CurrentCulture);
        CropBottomPercent.Text = ((1 - crop.Bottom) * 100).ToString("0.##", CultureInfo.CurrentCulture);
        CropRightPercent.Text = ((1 - crop.Right) * 100).ToString("0.##", CultureInfo.CurrentCulture);
        DisplayProfileSelector.SelectedItem = selected.DisplayProfile;
        PageRoleSelector.SelectedItem = selected.PageRole;
        PrintedPageNumberEditor.Text = selected.PrintedPageNumber ?? string.Empty;
        var reconstruction = VerticalTextReconstruction.Reconstruct(textState.MachineWords, 20, 0.75);
        TextEditor.Text = textState.ConfirmedText ?? textState.ManualText ?? textState.SuggestedText ?? reconstruction.Text;
        TextSourceStatus.Text = textState.LegacyMergedText is not null ? "表示中: 旧統合OCRの補正候補（元の座標は不明です）" :
            textState.ConfirmedText is not null ? "表示中: ChatGPT取込み済み確定本文" :
            textState.ManualText is not null ? "表示中: 手動修正文" :
            textState.SuggestedText is not null ? "表示中: 補正候補（OCR原本は保持されています）" : "表示中: PaddleOCR原本から復元した下書き";
        if (selected.ProofreadingStatus == ProofreadingStatus.Stale)
            TextSourceStatus.Text += "／校正済みですが、その後OCRが更新されています。本文を確認してください。";
        var source = new BitmapImage(new Uri(selected.SourcePath, UriKind.Absolute));
        PagePreview.Source = selected.RotationDegrees == 0
            ? source
            : new TransformedBitmap(source, new System.Windows.Media.RotateTransform(selected.RotationDegrees));
        _previewZoom = 1;
        ApplyPreviewZoom();
        UpdateCropOverlay(crop);
        var storedReviewCount = (await repository.LoadReviewItemsAsync(selected.Id, CancellationToken.None)).Count;
        var rubyCount = (await repository.LoadLatestOcrWordStatesAsync(selected.Id, CancellationToken.None)).Count(word => word.Role == "RubyCandidate");
        var failures = await repository.LoadOcrFailuresAsync(selected.Id, CancellationToken.None);
        ReviewStatus.Text = $"要確認: 低信頼度 {reconstruction.ReviewItems.Count}／検証 {storedReviewCount}／ルビ候補 {rubyCount}"
            + (failures.Count == 0 ? string.Empty : $"／OCR失敗 {failures.Count}（最新: {failures[0].Stage} {failures[0].Message}）");
    }

    private async void SaveManualText(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SaveManualTextAsync(selected.Id, TextEditor.Text, CancellationToken.None);
        _pages = _pages.Select(page => page.Id == selected.Id ? page with { ProofreadingStatus = ProofreadingStatus.ManuallyEdited } : page).ToList();
        RefreshPages();
    }

    private void ShowPageReview(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        new PageReviewWindow(_projectDirectory, selected) { Owner = this }.ShowDialog();
    }

    private async void SavePageMetadata(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected ||
            DisplayProfileSelector.SelectedItem is not DisplayProfile profile || PageRoleSelector.SelectedItem is not PageRole role) return;
        _pages = _pages.Select(page => page.Id == selected.Id
            ? page with { DisplayProfile = profile, PageRole = role, PrintedPageNumber = string.IsNullOrWhiteSpace(PrintedPageNumberEditor.Text) ? null : PrintedPageNumberEditor.Text.Trim() }
            : page).ToList();
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SavePagesAsync(_pages, CancellationToken.None);
        await ValidatePagesAsync(repository);
        _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
        RefreshPages();
        PageList.SelectedItem = _pages.Single(page => page.Id == selected.Id);
    }

    private void ZoomIn(object sender, RoutedEventArgs e) { _previewZoom = Math.Min(4, _previewZoom * 1.25); ApplyPreviewZoom(); }
    private void ZoomOut(object sender, RoutedEventArgs e) { _previewZoom = Math.Max(.25, _previewZoom / 1.25); ApplyPreviewZoom(); }
    private void ZoomActual(object sender, RoutedEventArgs e) { _previewZoom = 1; ApplyPreviewZoom(); }
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) => UpdateCropOverlay(PageList.SelectedItem is ProjectPage page ? page.Crop ?? NormalizedCrop.Full : NormalizedCrop.Full);

    private void ApplyPreviewZoom()
    {
        if (PagePreview.Source is not BitmapSource source) return;
        PagePreview.Width = source.PixelWidth * _previewZoom;
        PagePreview.Height = source.PixelHeight * _previewZoom;
    }

    private void UpdateCropOverlay(NormalizedCrop crop)
    {
        CropOverlay.Width = PagePreview.ActualWidth;
        CropOverlay.Height = PagePreview.ActualHeight;
        TopCropOverlay.Width = CropOverlay.Width;
        BottomCropOverlay.Width = CropOverlay.Width;
        TopCropOverlay.Height = CropOverlay.Height * crop.Top;
        BottomCropOverlay.Height = CropOverlay.Height * (1 - crop.Bottom);
        LeftCropOverlay.Width = CropOverlay.Width * crop.Left;
        LeftCropOverlay.Height = CropOverlay.Height;
        RightCropOverlay.Width = CropOverlay.Width * (1 - crop.Right);
        RightCropOverlay.Height = CropOverlay.Height;
        TopCropLabel.Text = crop.Top > 0 ? $"上部除外 {crop.Top:P0}" : string.Empty;
        BottomCropLabel.Text = crop.Bottom < 1 ? $"下部除外 {1 - crop.Bottom:P0}" : string.Empty;
        LeftCropLabel.Text = crop.Left > 0 ? $"左除外 {crop.Left:P0}" : string.Empty;
        RightCropLabel.Text = crop.Right < 1 ? $"右除外 {1 - crop.Right:P0}" : string.Empty;
    }

    private async void ExportProofreadingPackage(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || _pages.Count == 0) return;
        if (!int.TryParse(ProofreadingStartPage.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var startPage) ||
            !int.TryParse(ProofreadingPageCount.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var pageCount) || startPage < 1 || pageCount < 1)
        {
            MessageBox.Show(this, "校正範囲の開始ページと件数は1以上の整数で指定してください。", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var selectedPages = _pages.Where(page => page.IsIncluded).OrderBy(page => page.SortOrder).Skip(startPage - 1).Take(pageCount).ToArray();
        if (selectedPages.Length == 0)
        {
            MessageBox.Show(this, "指定された範囲に出力対象ページがありません。", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var formatChoice = MessageBox.Show(this, "ZIP形式で出力しますか？\n［はい］: ZIP ／ ［いいえ］: フォルダー", "校正用パッケージ形式", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (formatChoice == MessageBoxResult.Cancel) return;
        var format = formatChoice == MessageBoxResult.Yes ? ProofreadingPackageFormat.Zip : ProofreadingPackageFormat.Directory;
        string? destination;
        if (format == ProofreadingPackageFormat.Zip)
        {
            var dialog = new SaveFileDialog { Filter = "ZIPファイル|*.zip", FileName = $"TateScribe-Proofreading-{startPage:000}.zip" };
            if (dialog.ShowDialog(this) != true) return;
            destination = dialog.FileName;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = "校正用パッケージの出力先フォルダーを選択" };
            if (dialog.ShowDialog(this) != true) return;
            destination = Path.Combine(dialog.FolderName, $"TateScribe-Proofreading-{startPage:000}");
        }
        try
        {
            IsEnabled = false;
            await new ProofreadingPackageService().ExportAsync(
                _projectDirectory, selectedPages, destination, format,
                IncludeCroppedImages.IsChecked == true, CancellationToken.None);
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
            RefreshPages();
            MessageBox.Show(this, $"校正用パッケージを出力しました。{Environment.NewLine}{destination}", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "校正用パッケージの出力に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void ImportProofreadText(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null) return;
        var dialog = new OpenFileDialog { Filter = "校正済みテキストまたはZIP|*.txt;*.md;*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var service = new ProofreadingImportService();
            var preview = await service.PrepareAsync(_projectDirectory, dialog.FileName, CancellationToken.None);
            var details = ProofreadingImportService.BuildDetails(preview);
            if (preview.Issues.Any(issue => issue.IsError && issue.PageMarker is null))
            {
                MessageBox.Show(this, $"検証エラーのため本文は保存しません。{Environment.NewLine}{details}", "校正結果の検証", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selection = new ProofreadingImportWindow(preview, details, pageId =>
            {
                PageList.SelectedItem = _pages.SingleOrDefault(page => page.Id == pageId);
                PageList.ScrollIntoView(PageList.SelectedItem);
            }) { Owner = this };
            if (selection.ShowDialog() != true) return;
            await service.SaveAsync(_projectDirectory, preview, selection.AcceptedMarkers, CancellationToken.None);
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
            RefreshPages();
            ReviewStatus.Text = $"校正済み本文を {selection.AcceptedMarkers.Count} ページ保存しました。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "校正済みテキストを取り込めません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportDocx(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || _pages.Count == 0) return;
        try
        {
            IsEnabled = false;
            var preparation = await new DocumentExportService().PrepareAsync(
                _projectDirectory, _pages, PageBreakBeforeChapters.IsChecked == true, CancellationToken.None);
            if (preparation.OtherPagesWithText.Count > 0
                && MessageBox.Show(this,
                    $"PageRole=Other の本文ページ {preparation.OtherPagesWithText.Count} 件を含めます。続行しますか？",
                    "Otherページの確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (preparation.UnproofreadPageCount > 0 && MessageBox.Show(this, $"未校正ページ {preparation.UnproofreadPageCount} 件を含めてDOCXを出力します。続行しますか？", "未校正本文を含む出力", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var outputPath = BookFolderPaths.GetDocumentPath(_projectDirectory);
            await new OpenXmlDocumentExporter().ExportAsync(preparation.Document, outputPath, CancellationToken.None);
            var summary = preparation.EmptyPageCount == 0
                ? $"{preparation.IncludedPageCount} ページを出力しました。"
                : $"{preparation.IncludedPageCount} ページを出力し、本文のない {preparation.EmptyPageCount} ページを除外しました。";
            MessageBox.Show(this, $"DOCXを出力しました。{Environment.NewLine}{summary}{Environment.NewLine}{outputPath}", "TateScribe", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void ShowChatGptProofreadingPrompt(object sender, RoutedEventArgs e)
    {
        new ChatGptProofreadingPromptWindow { Owner = this }.ShowDialog();
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
            var progress = new Progress<(int Current, int Total, string FileName)>(value =>
                ReviewStatus.Text = $"OCR実行中: {value.Current}/{value.Total} {value.FileName}");
            var result = await new OcrOrchestrationService().RunAsync(
                projectDirectory, pages, python, workerScript, progress, _ocrCancellation.Token);
            if (PageList.SelectedItem is ProjectPage selected)
            {
                var outcome = result.Pages.SingleOrDefault(page => page.PageId == selected.Id);
                if (outcome?.SuggestedText is not null)
                {
                    TextEditor.Text = outcome.SuggestedText;
                    TextSourceStatus.Text = "表示中: 補正候補（PaddleOCR原本と座標は保持されています）";
                }
            }
            ReviewStatus.Text = result.Failures.Count == 0
                ? $"OCR完了: {pages.Count} ページ"
                : $"OCR完了: {result.SucceededCount}/{pages.Count} ページ（失敗: {string.Join(", ", result.Failures.Select(failure => $"{failure.FileName}: {failure.Stage} {failure.Message}"))}）";
            await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, CancellationToken.None);
            _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
            RefreshPages();
        }
        catch (OcrWorkerException exception)
        {
            MessageBox.Show(this, exception.Message, "OCRを実行できません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (OperationCanceledException)
        {
            ReviewStatus.Text = "OCRを中止しました。完了済みのOCR結果は保持されています。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "OCRを実行できません", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private async Task ValidatePagesAsync(SqliteProjectRepository repository)
    {
        var issues = PageValidationService.Validate(_pages);
        await repository.ReplacePageValidationIssuesAsync(issues, CancellationToken.None);
        ReviewStatus.Text = issues.Count == 0
            ? "固定ページの印刷ページ番号に矛盾はありません。"
            : $"要確認: 印刷ページ番号 {issues.Count} 件（一覧は校正パッケージにも含まれます）";
    }

    private static string ResolveRuntimePath(params string[] parts)
    {
        var packagedPath = Path.Combine([AppContext.BaseDirectory, .. parts]);
        return File.Exists(packagedPath) ? packagedPath : Path.Combine([Directory.GetCurrentDirectory(), .. parts]);
    }
}
