using DocumentFormat.OpenXml.Packaging;
using TateScribe.Core.Export;
using TateScribe.Core.Layout;
using TateScribe.Core.Ocr;
using TateScribe.Infrastructure.Export;
using TateScribe.Infrastructure.Ocr;

namespace TateScribe.Tests;

public sealed class EndToEndAcceptanceTests : IDisposable
{
    private readonly string _outputPath = Path.Combine(Path.GetTempPath(), $"TateScribe-{Guid.NewGuid():N}.docx");

    [Fact]
    public async Task Local_screenshot_flows_from_paddle_ocr_to_horizontal_docx_without_page_breaks()
    {
        var root = FindRepositoryRoot();
        var python = Path.Combine(root, "ocr-runtime", "Scripts", "python.exe");
        var image = Path.Combine(root, "testdata", "7つの会議", "IMG_20260505_132622.png");
        if (!File.Exists(python) || !File.Exists(image)) return;

        await using var worker = new JsonLinesOcrWorker(python, Path.Combine(root, "ocr-worker", "worker.py"));
        var ocr = await worker.RecognizeAsync(new OcrRequest("end-to-end", "paddle", image), CancellationToken.None);
        var reconstructed = VerticalTextReconstruction.Reconstruct(ocr.Words, 20, 0.75);
        await new OpenXmlDocumentExporter().ExportAsync(BookDocumentAssembler.Assemble([reconstructed.Text]), _outputPath, CancellationToken.None);

        using var document = WordprocessingDocument.Open(_outputPath, false);
        var xml = document.MainDocumentPart!.Document.OuterXml;
        Assert.Contains("第一話", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("w:type=\"page\"", xml, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
