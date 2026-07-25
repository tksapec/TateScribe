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
        var printedNumbers = _pages.Where(page => page.DisplayProfile == DisplayProfile.FixedPageVertical)
            .OrderBy(page => page.SortOrder)
            .Select(page => int.TryParse(page.PrintedPageNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : (int?)null)
            .Where(number => number.HasValue).Select(number => number!.Value).ToArray();
        if (printedNumbers.Zip(printedNumbers.Skip(1)).Any(pair => pair.Second <= pair.First || pair.Second > pair.First + 1))
            ReviewStatus.Text = "要確認: 固定ページの印刷ページ番号に順序矛盾または欠落候補があります。";
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
        var source = new BitmapImage(new Uri(selected.SourcePath, UriKind.Absolute));
        PagePreview.Source = selected.RotationDegrees == 0
            ? source
            : new TransformedBitmap(source, new System.Windows.Media.RotateTransform(selected.RotationDegrees));
        _previewZoom = 1;
        ApplyPreviewZoom();
        UpdateCropOverlay(crop);
        ReviewStatus.Text = reconstruction.ReviewItems.Count == 0
            ? "要確認の低信頼度文字はありません。"
            : $"要確認: 低信頼度文字 {reconstruction.ReviewItems.Count} 件";
    }

    private async void SaveManualText(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || PageList.SelectedItem is not ProjectPage selected) return;
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.SaveManualTextAsync(selected.Id, TextEditor.Text, CancellationToken.None);
        _pages = _pages.Select(page => page.Id == selected.Id ? page with { ProofreadingStatus = ProofreadingStatus.ManuallyEdited } : page).ToList();
        RefreshPages();
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
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            var cacheDirectory = Path.Combine(_projectDirectory, ".tatescribe-cache");
            var preprocessor = new ScreenshotPreprocessor();
            var packagePages = new List<ProofreadingPackagePage>();
            foreach (var page in selectedPages)
            {
                if (!File.Exists(page.SourcePath)) throw new FileNotFoundException("校正パッケージに含める元画像が見つかりません。", page.SourcePath);
                var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);
                var reconstruction = VerticalTextReconstruction.Reconstruct(state.RawPaddleWords, 20, .75);
                string? cropped = null;
                if (IncludeCroppedImages.IsChecked == true)
                    cropped = (await preprocessor.PrepareAsync(page.SourcePath, cacheDirectory, page.Crop ?? NormalizedCrop.Full, page.RotationDegrees, CancellationToken.None)).CachePath;
                packagePages.Add(new ProofreadingPackagePage(page.Id, page.SortOrder, page.FileName, page.SourceHash, page.SourcePath, cropped,
                    reconstruction.Text, state.SuggestedText, reconstruction.ReviewItems.Count, page.PageRole.ToString(), page.DisplayProfile.ToString(),
                    reconstruction.ReviewItems.Select(item => new ProofreadingReviewItem(item.Code, item.Message, item.Word?.Text ?? string.Empty)).ToArray()));
            }
            var batchId = Guid.NewGuid();
            var request = new ProofreadingPackageRequest(await repository.GetProjectIdAsync(CancellationToken.None), Path.GetFileName(Path.TrimEndingDirectorySeparator(_projectDirectory)), batchId,
                destination, format, packagePages);
            await new ProofreadingPackageExporter().ExportAsync(request, CancellationToken.None);
            await repository.RecordProofreadingExportAsync(batchId, selectedPages.Select(page => page.Id).ToArray(), CancellationToken.None);
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
            var content = await ReadProofreadingContentAsync(dialog.FileName, CancellationToken.None);
            var document = ProofreadingImportParser.Parse(content);
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            var preview = await repository.PrepareConfirmedImportAsync(document, CancellationToken.None);
            var details = await BuildImportDetailsAsync(repository, preview, CancellationToken.None);
            if (preview.Issues.Any(issue => issue.IsError))
            {
                MessageBox.Show(this, $"検証エラーのため本文は保存しません。{Environment.NewLine}{details}", "校正結果の検証", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selection = new ProofreadingImportWindow(preview, details) { Owner = this };
            if (selection.ShowDialog() != true) return;
            await repository.SaveConfirmedTextAsync(preview, selection.AcceptedMarkers, CancellationToken.None);
            _pages = (await repository.LoadPagesAsync(CancellationToken.None)).ToList();
            RefreshPages();
            ReviewStatus.Text = $"校正済み本文を {preview.Candidates.Count} ページ保存しました。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "校正済みテキストを取り込めません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<string> ReadProofreadingContentAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)) return await File.ReadAllTextAsync(path, cancellationToken);
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.SingleOrDefault(entry => string.Equals(entry.FullName, "proofread.txt", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.SingleOrDefault(entry => string.Equals(entry.FullName, "proofread.md", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("ZIP内に proofread.txt または proofread.md がありません。");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task<string> BuildImportDetailsAsync(SqliteProjectRepository repository, ProofreadingImportPreview preview, CancellationToken cancellationToken)
    {
        var lines = preview.Issues.Select(issue => $"{(issue.IsError ? "エラー" : "警告")}: {issue.Code} {issue.PageMarker ?? string.Empty}").ToList();
        foreach (var candidate in preview.Candidates)
        {
            var state = await repository.LoadPageTextStateAsync(candidate.PageId, cancellationToken);
            var baseline = state.ManualText ?? state.SuggestedText ?? VerticalTextReconstruction.Reconstruct(state.RawPaddleWords, 20, .75).Text;
            var delta = candidate.ConfirmedText.Length - baseline.Length;
            lines.Add($"PAGE {candidate.PageMarker}: {baseline.Length} → {candidate.ConfirmedText.Length} 文字（{delta:+#;-#;0}）、追加・削除・変更は確定前プレビュー対象");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private async void ExportDocx(object sender, RoutedEventArgs e)
    {
        if (_projectDirectory is null || _pages.Count == 0) return;
        try
        {
            IsEnabled = false;
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            var pageTexts = new List<string>();
            var skippedPages = 0;
            var unproofreadPages = 0;
            foreach (var page in _pages.Where(page => page.IsIncluded).OrderBy(page => page.SortOrder))
            {
                var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);
                if (page.PageRole is PageRole.Illustration or PageRole.Blank or PageRole.Other) continue;
                var reconstructed = VerticalTextReconstruction.Reconstruct(state.RawPaddleWords, 20, 0.75).Text;
                var text = state.ConfirmedText ?? state.ManualText ?? state.SuggestedText ?? reconstructed;
                if (state.ConfirmedText is null) unproofreadPages++;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    pageTexts.Add(page.PageRole == PageRole.ChapterTitle ? BookDocumentAssembler.CreateChapterPageText(text) : text);
                }
                else skippedPages++;
            }
            if (unproofreadPages > 0 && MessageBox.Show(this, $"未校正ページ {unproofreadPages} 件を含めてDOCXを出力します。続行しますか？", "未校正本文を含む出力", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var outputPath = BookFolderPaths.GetDocumentPath(_projectDirectory);
            var document = BookDocumentAssembler.Assemble(pageTexts) with { PageBreakBeforeChapters = PageBreakBeforeChapters.IsChecked == true };
            await new OpenXmlDocumentExporter().ExportAsync(document, outputPath, CancellationToken.None);
            var summary = skippedPages == 0 ? $"{pageTexts.Count} ページを出力しました。" : $"{pageTexts.Count} ページを出力し、本文のない {skippedPages} ページを除外しました。";
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
            await using var worker = new JsonLinesOcrWorker(python, workerScript);
            await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, CancellationToken.None);
            var preprocessor = new ScreenshotPreprocessor();
            var cacheDirectory = Path.Combine(projectDirectory, ".tatescribe-cache");
            var failedPages = new List<string>();
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                ReviewStatus.Text = $"OCR実行中: {index + 1}/{pages.Count} {page.FileName}";
                try
                {
                    if (!File.Exists(page.SourcePath)) throw new FileNotFoundException("OCR対象の元画像が見つかりません。", page.SourcePath);
                    var prepared = await preprocessor.PrepareAsync(page.SourcePath, cacheDirectory, page.Crop ?? NormalizedCrop.Full, page.RotationDegrees, _ocrCancellation.Token);
                    var paddle = await worker.RecognizeAsync(new OcrRequest(Guid.NewGuid().ToString("N"), "paddle", prepared.CachePath), _ocrCancellation.Token);
                    var tesseract = await worker.RecognizeAsync(new OcrRequest(Guid.NewGuid().ToString("N"), "tesseract", prepared.CachePath), _ocrCancellation.Token);
                    var paddleText = VerticalTextReconstruction.Reconstruct(paddle.Words, 20, 0.75).Text;
                    var rawTesseractText = string.Concat(tesseract.Words.Select(word => word.Text));
                    var orderedPaddleWords = VerticalTextReconstruction.OrderWordsForReadingWithRawOrdinals(paddle.Words, 20);
                    var proposal = PunctuationMerger.ProposeWithRawWordOrdinals(paddleText, rawTesseractText, orderedPaddleWords, 16);
                    await repository.SaveOcrAnalysisAsync(page.Id, paddle, rawTesseractText, proposal, _ocrCancellation.Token);
                    if (PageList.SelectedItem is ProjectPage selected && selected.Id == page.Id)
                    {
                        TextEditor.Text = proposal.SuggestedText;
                        TextSourceStatus.Text = "表示中: 補正候補（PaddleOCR原本と座標は保持されています）";
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { failedPages.Add(page.FileName); }
            }
            ReviewStatus.Text = failedPages.Count == 0
                ? $"OCR完了: {pages.Count} ページ"
                : $"OCR完了: {pages.Count - failedPages.Count}/{pages.Count} ページ（失敗: {string.Join(", ", failedPages)}）";
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

    private static string ResolveRuntimePath(params string[] parts)
    {
        var packagedPath = Path.Combine([AppContext.BaseDirectory, .. parts]);
        return File.Exists(packagedPath) ? packagedPath : Path.Combine([Directory.GetCurrentDirectory(), .. parts]);
    }
}
