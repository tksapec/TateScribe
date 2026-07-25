using TateScribe.Core.Ocr;
using TateScribe.Infrastructure.Ocr;

namespace TateScribe.Tests;

public sealed class TesseractOcrAcceptanceTests
{
    [Fact]
    public async Task Local_vertical_japanese_tesseract_keeps_punctuation_when_runtime_exists()
    {
        var root = FindRepositoryRoot();
        var python = Path.Combine(root, "ocr-runtime", "Scripts", "python.exe");
        var image = Path.Combine(root, "testdata", "成瀬は天下を取りにいく", "IMG_20260725_083159.png");
        if (!File.Exists(python) || !File.Exists(image)) return;

        await using var worker = new JsonLinesOcrWorker(python, Path.Combine(root, "ocr-worker", "worker.py"));
        var result = await worker.RecognizeAsync(new OcrRequest("tesseract-acceptance", "tesseract", image), CancellationToken.None);

        var text = string.Concat(result.Words.Select(word => word.Text));
        Assert.Equal("tesseract", result.Engine);
        Assert.Contains("、", text);
        Assert.Contains("。", text);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
