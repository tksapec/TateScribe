using System.IO;
using TateScribe.Core.Images;
using TateScribe.Core.Layout;
using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Images;
using TateScribe.Infrastructure.Ocr;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App.Services;

public sealed record OcrPageOutcome(Guid PageId, string FileName, bool Succeeded, string? SuggestedText, OcrFailure? Failure);

public sealed record OcrBatchResult(IReadOnlyList<OcrPageOutcome> Pages)
{
    public int SucceededCount => Pages.Count(page => page.Succeeded);
    public IReadOnlyList<OcrFailure> Failures => Pages.Where(page => page.Failure is not null).Select(page => page.Failure!).ToArray();
}

public sealed class OcrOrchestrationService
{
    public async Task<OcrBatchResult> RunAsync(
        string projectDirectory,
        IReadOnlyList<ProjectPage> pages,
        string pythonExecutable,
        string workerScript,
        IProgress<(int Current, int Total, string FileName)>? progress,
        CancellationToken cancellationToken)
    {
        await using var worker = new JsonLinesOcrWorker(pythonExecutable, workerScript);
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, CancellationToken.None);
        var preprocessor = new ScreenshotPreprocessor();
        var cacheDirectory = Path.Combine(projectDirectory, ".tatescribe-cache");
        var outcomes = new List<OcrPageOutcome>();
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            progress?.Report((index + 1, pages.Count, page.FileName));
            var stage = OcrFailureStage.Preprocess;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await repository.SetOcrStatusAsync(page.Id, OcrStatus.Processing, cancellationToken);
                if (!File.Exists(page.SourcePath))
                    throw new FileNotFoundException("OCR対象の元画像が見つかりません。", page.SourcePath);
                var prepared = await preprocessor.PrepareAsync(
                    page.SourcePath, cacheDirectory, page.Crop ?? NormalizedCrop.Full,
                    page.RotationDegrees, cancellationToken);
                stage = OcrFailureStage.PaddleOCR;
                var paddle = await worker.RecognizeAsync(
                    new OcrRequest(Guid.NewGuid().ToString("N"), "paddle", prepared.CachePath), cancellationToken);
                stage = OcrFailureStage.Tesseract;
                var tesseract = await worker.RecognizeAsync(
                    new OcrRequest(Guid.NewGuid().ToString("N"), "tesseract", prepared.CachePath), cancellationToken);
                stage = OcrFailureStage.Merge;
                var paddleText = VerticalTextReconstruction.Reconstruct(paddle.Words, 20, .75).Text;
                var rawTesseractText = string.Concat(tesseract.Words.Select(word => word.Text));
                var ordered = VerticalTextReconstruction.OrderWordsForReadingWithRawOrdinals(paddle.Words, 20);
                var proposal = PunctuationMerger.ProposeWithRawWordOrdinals(
                    paddleText, rawTesseractText, ordered, 16);
                stage = OcrFailureStage.DatabaseSave;
                await repository.SaveOcrAnalysisAsync(page.Id, paddle, rawTesseractText, proposal, cancellationToken);
                outcomes.Add(new OcrPageOutcome(page.Id, page.FileName, true, proposal.SuggestedText, null));
            }
            catch (OperationCanceledException exception)
            {
                var failure = CreateFailure(page, stage, exception, retryable: true, wasCancelled: true);
                await repository.RecordOcrFailureAsync(
                    failure, CancellationToken.None, page.OcrStatus);
                outcomes.Add(new OcrPageOutcome(page.Id, page.FileName, false, null, failure));
                throw;
            }
            catch (Exception exception)
            {
                var workerException = exception as OcrWorkerException;
                var reportedStage = workerException?.Stage switch
                {
                    "PaddleOCR" => OcrFailureStage.PaddleOCR,
                    "Tesseract" => OcrFailureStage.Tesseract,
                    _ => stage
                };
                var failure = CreateFailure(
                    page, reportedStage, exception,
                    workerException?.CanRetry ?? exception is IOException,
                    wasCancelled: false,
                    workerException?.ExceptionType);
                await repository.RecordOcrFailureAsync(failure, CancellationToken.None);
                outcomes.Add(new OcrPageOutcome(page.Id, page.FileName, false, null, failure));
            }
        }
        return new OcrBatchResult(outcomes);
    }

    private static OcrFailure CreateFailure(
        ProjectPage page,
        OcrFailureStage stage,
        Exception exception,
        bool retryable,
        bool wasCancelled,
        string? exceptionType = null) =>
        new(
            Guid.NewGuid(), page.Id, page.FileName, stage,
            exceptionType ?? exception.GetType().Name, exception.Message,
            retryable, wasCancelled, DateTimeOffset.UtcNow);
}
