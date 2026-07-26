using TateScribe.Core.Ocr;
using TateScribe.Infrastructure.Ocr;

namespace TateScribe.Tests;

public sealed class JsonLinesOcrWorkerTests
{
    [Fact]
    public async Task Recognize_mock_engine_returns_versioned_empty_evidence()
    {
        var root = FindRepositoryRoot();
        await using var worker = new JsonLinesOcrWorker("python", Path.Combine(root, "ocr-worker", "worker.py"));

        var result = await worker.RecognizeAsync(new OcrRequest("sample-id", "mock", "sample.png"), CancellationToken.None);

        Assert.Equal("sample-id", result.RequestId);
        Assert.Equal("mock", result.Engine);
        Assert.Empty(result.Words);
    }

    [Fact]
    public async Task Recognize_unconfigured_engine_reports_retryable_failure()
    {
        var root = FindRepositoryRoot();
        await using var worker = new JsonLinesOcrWorker("python", Path.Combine(root, "ocr-worker", "worker.py"));

        var exception = await Assert.ThrowsAsync<OcrWorkerException>(() => worker.RecognizeAsync(new OcrRequest("missing", "paddle", "sample.png"), CancellationToken.None));

        Assert.True(exception.CanRetry);
        Assert.Equal("PaddleOCR", exception.Stage);
        Assert.Equal("ValueError", exception.ExceptionType);
    }

    [Fact]
    public async Task Recognize_reuses_one_worker_process_for_multiple_requests()
    {
        var root = FindRepositoryRoot();
        await using var worker = new JsonLinesOcrWorker("python", Path.Combine(root, "ocr-worker", "worker.py"));

        var first = await worker.RecognizeAsync(new OcrRequest("first", "mock", "sample.png"), CancellationToken.None);
        var second = await worker.RecognizeAsync(new OcrRequest("second", "mock", "sample.png"), CancellationToken.None);

        Assert.Equal("first", first.RequestId);
        Assert.Equal("second", second.RequestId);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
