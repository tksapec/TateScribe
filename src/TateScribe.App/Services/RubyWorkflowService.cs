using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TateScribe.Core.Images;
using TateScribe.Core.Ruby;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Images;
using TateScribe.Infrastructure.Ruby;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App.Services;

public sealed record RubyPackageExportResult(
    Guid BatchId,
    Guid SnapshotId,
    StructuredDocument Document,
    int UnproofreadPageCount);

public sealed record RubyImportResult(RubyBatchSnapshot Batch, RubyImportPreview Preview);

public sealed class RubyWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
    public async Task<RubyPackageExportResult> ExportPackageAsync(
        string projectDirectory,
        IReadOnlyList<ProjectPage> pages,
        RubyPolicy policy,
        string destination,
        CancellationToken cancellationToken)
    {
        var preparation = await new DocumentExportService().PrepareStructuredAsync(
            projectDirectory, pages, false, cancellationToken);
        var snapshotId = await new DocumentExportService().PersistAfterSuccessfulOutputAsync(
            projectDirectory, preparation.Document, cancellationToken);
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        var cacheDirectory = Path.Combine(projectDirectory, ".tatescribe-cache");
        var preprocessor = new ScreenshotPreprocessor();
        var packagePages = new List<RubyPackagePage>();
        var candidates = new List<RubyOcrCandidate>();
        var includedPages = pages.Where(page => page.IsIncluded
                && page.PageRole is not (PageRole.Illustration or PageRole.Blank))
            .OrderBy(page => page.SortOrder).ToArray();
        for (var index = 0; index < includedPages.Length; index++)
        {
            var page = includedPages[index];
            if (!File.Exists(page.SourcePath))
                throw new FileNotFoundException("ルビ確認用パッケージに含める元画像が見つかりません。", page.SourcePath);
            var marker = (index + 1).ToString("0000");
            var cropped = (await preprocessor.PrepareAsync(
                page.SourcePath, cacheDirectory, page.Crop ?? NormalizedCrop.Full,
                page.RotationDegrees, cancellationToken)).CachePath;
            packagePages.Add(new RubyPackagePage(page.Id, marker, page.SourcePath, cropped));
            candidates.AddRange(RubyOcrCandidateSelector.Select(
                marker,
                await repository.LoadLatestOcrWordStatesAsync(page.Id, cancellationToken)));
        }
        var batchId = Guid.NewGuid();
        await new RubyPackageExporter().ExportAsync(new RubyPackageRequest(
            preparation.Document.ProjectId, batchId, policy, preparation.Document,
            packagePages, candidates, destination), cancellationToken);
        await repository.RecordRubyBatchAsync(batchId, preparation.Document.ProjectId,
            snapshotId, policy, packagePages, candidates, cancellationToken);
        return new RubyPackageExportResult(batchId, snapshotId, preparation.Document,
            preparation.LegacyPreparation.UnproofreadPageCount);
    }

    public async Task<RubyImportResult> PrepareImportAsync(
        string projectDirectory,
        string jsonPath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        Guid batchId;
        try
        {
            using var root = JsonDocument.Parse(json);
            batchId = root.RootElement.GetProperty("batchId").GetGuid();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("ルビJSONのbatchIdを読み取れません。", exception);
        }
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        var batch = await repository.LoadRubyBatchAsync(batchId, cancellationToken);
        var preview = new RubyImportValidator().Validate(json,
            new RubyValidationContext(batch.Document.ProjectId, batchId, batch.Document, batch.PageMarkers,
                batch.Policy, batch.ConfirmedTextIsStale, batch.OcrCandidates));
        if (preview.Result is not null)
        {
            var identified = preview.Result with
            {
                Annotations = preview.Result.Annotations
                    .Select(item => item with { AnnotationId = Guid.NewGuid() }).ToArray(),
            };
            preview = ValidateReviewed(batch, identified);
        }
        return new RubyImportResult(batch, preview);
    }

    public async Task SaveImportAsync(
        string projectDirectory,
        RubyImportResult result,
        CancellationToken cancellationToken)
    {
        if (!result.Preview.IsValid || result.Preview.Result is null)
            throw new InvalidOperationException("検証エラーがあるルビJSONは保存できません。");
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        await repository.SaveRubyImportAsync(
            result.Batch.SnapshotId, result.Batch.Policy, result.Preview.Result, cancellationToken);
    }

    public RubyImportPreview ValidateReviewed(RubyBatchSnapshot batch, RubyImportDocument reviewed)
    {
        return ValidateReviewedDocument(batch, reviewed);
    }

    public async Task<RubyImportResult?> LoadLatestReviewAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        var batchId = await repository.GetLatestRubyBatchWithAnnotationsIdAsync(cancellationToken);
        if (batchId is null) return null;
        return await LoadReviewAsync(repository, batchId.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<RubyBatchHistoryItem>> LoadRubyBatchHistoryAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(
            projectDirectory,
            cancellationToken);
        return await repository.LoadRubyBatchHistoryAsync(cancellationToken);
    }

    public async Task<RubyImportResult> LoadReviewAsync(
        string projectDirectory,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(
            projectDirectory,
            cancellationToken);
        return await LoadReviewAsync(repository, batchId, cancellationToken);
    }

    private RubyImportPreview ValidateReviewedDocument(
        RubyBatchSnapshot batch,
        RubyImportDocument reviewed)
    {
        var validated = new RubyImportValidator().Validate(
            reviewed,
            new RubyValidationContext(
                batch.Document.ProjectId,
                batch.BatchId,
                batch.Document,
                batch.PageMarkers,
                batch.Policy,
                batch.ConfirmedTextIsStale,
                batch.OcrCandidates));
        return validated.IsValid
            ? validated with { Result = reviewed }
            : validated;
    }

    private async Task<RubyImportResult> LoadReviewAsync(
        SqliteProjectRepository repository,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await repository.LoadRubyBatchAsync(batchId, cancellationToken);
        var annotations = await repository.LoadRubyAnnotationsAsync(batchId, cancellationToken);
        var unresolved = await repository.LoadRubyUnresolvedItemsAsync(batchId, cancellationToken);
        var import = new RubyImportDocument(1, batch.Document.ProjectId, batch.BatchId,
            batch.Document.DocumentTextHash, annotations, unresolved);
        return new RubyImportResult(batch, ValidateReviewed(batch, import));
    }

    public async Task SaveReviewAsync(
        string projectDirectory,
        RubyImportResult result,
        CancellationToken cancellationToken)
    {
        if (!result.Preview.IsValid || result.Preview.Result is null)
            throw new InvalidOperationException("検証エラーがあるルビ候補は保存できません。");
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        await repository.UpdateRubyAnnotationsAsync(result.Preview.Result.Annotations, cancellationToken);
    }
}
