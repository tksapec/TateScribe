using System.IO;
using TateScribe.Core.Images;
using TateScribe.Core.Layout;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Images;
using TateScribe.Infrastructure.Proofreading;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App.Services;

public sealed class ProofreadingPackageService
{
    public async Task ExportAsync(
        string projectDirectory,
        IReadOnlyList<ProjectPage> pages,
        string destination,
        ProofreadingPackageFormat format,
        bool includeCroppedImages,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        var cacheDirectory = Path.Combine(projectDirectory, ".tatescribe-cache");
        var preprocessor = new ScreenshotPreprocessor();
        var packagePages = new List<ProofreadingPackagePage>();
        foreach (var page in pages)
        {
            if (!File.Exists(page.SourcePath))
                throw new FileNotFoundException("校正パッケージに含める元画像が見つかりません。", page.SourcePath);
            var state = await repository.LoadPageTextStateAsync(page.Id, cancellationToken);
            var reconstruction = VerticalTextReconstruction.Reconstruct(state.RawPaddleWords, 20, .75);
            string? cropped = null;
            if (includeCroppedImages)
                cropped = (await preprocessor.PrepareAsync(
                    page.SourcePath, cacheDirectory, page.Crop ?? NormalizedCrop.Full,
                    page.RotationDegrees, cancellationToken)).CachePath;

            var reviewItems = reconstruction.ReviewItems
                .Select(item => new ProofreadingReviewItem(item.Code, item.Message, item.Word?.Text ?? string.Empty))
                .ToList();
            reviewItems.AddRange((await repository.LoadReviewItemsAsync(page.Id, cancellationToken))
                .Select(item => new ProofreadingReviewItem(item.Code, item.Message, item.Text ?? string.Empty)));
            reviewItems.AddRange((await repository.LoadLatestOcrWordStatesAsync(page.Id, cancellationToken))
                .Where(word => word.Role == "RubyCandidate")
                .Select(word => new ProofreadingReviewItem(
                    "RubyCandidate",
                    $"ルビ候補（座標 {word.Word.Left:0},{word.Word.Top:0}-{word.Word.Right:0},{word.Word.Bottom:0}）",
                    word.Word.Text)));
            var distinctReviewItems = reviewItems
                .GroupBy(item => (item.Code, item.Text))
                .Select(group => group.First())
                .ToArray();

            packagePages.Add(new ProofreadingPackagePage(
                page.Id, page.SortOrder, page.FileName, page.SourceHash, page.SourcePath, cropped,
                reconstruction.Text, state.SuggestedText, reconstruction.ReviewItems.Count,
                page.PageRole.ToString(), page.DisplayProfile.ToString(), distinctReviewItems,
                state.ManualText, state.ConfirmedText, page.BoundaryJoinType));
        }

        var batchId = Guid.NewGuid();
        var request = new ProofreadingPackageRequest(
            await repository.GetProjectIdAsync(cancellationToken),
            Path.GetFileName(Path.TrimEndingDirectorySeparator(projectDirectory)),
            batchId, destination, format, packagePages);
        await new ProofreadingPackageExporter().ExportAsync(request, cancellationToken);
        await repository.RecordProofreadingExportAsync(batchId, pages.Select(page => page.Id).ToArray(), cancellationToken);
    }
}
