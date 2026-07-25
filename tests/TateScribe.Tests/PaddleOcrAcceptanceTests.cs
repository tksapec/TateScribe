using TateScribe.Core.Ocr;
using TateScribe.Infrastructure.Ocr;

namespace TateScribe.Tests;

public sealed class PaddleOcrAcceptanceTests
{
    [Fact]
    public async Task Local_paddle_worker_returns_positioned_japanese_text_when_models_and_testdata_exist()
    {
        var root = FindRepositoryRoot();
        var python = Path.Combine(root, "ocr-runtime", "Scripts", "python.exe");
        var source = Path.Combine(root, "testdata", "7つの会議", "IMG_20260505_132622.png");
        if (!File.Exists(python) || !File.Exists(source)) return;

        await using var worker = new JsonLinesOcrWorker(python, Path.Combine(root, "ocr-worker", "worker.py"));
        var result = await worker.RecognizeAsync(new OcrRequest("paddle-acceptance", "paddle", source), CancellationToken.None);

        Assert.NotEmpty(result.Words);
        Assert.Contains(result.Words, word => word.Text.Contains("第一話", StringComparison.Ordinal));
        Assert.All(result.Words, word => Assert.True(word.Right > word.Left && word.Bottom > word.Top));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
