using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TateScribe.Core.Export;
using TateScribe.Infrastructure.Export;

namespace TateScribe.Tests;

public sealed class DocxExportTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"TateScribe-{Guid.NewGuid():N}.docx");

    [Fact]
    public async Task Export_writes_heading_and_ruby_without_page_markers()
    {
        var exporter = new OpenXmlDocumentExporter();
        var document = new ExportDocument([
            new ExportParagraph(ExportStyle.Heading1, "第一章"),
            new ExportParagraph(ExportStyle.Normal, "本文", new RubyAnnotation("本文", "ほんぶん"))
        ]);

        await exporter.ExportAsync(document, _path, CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var xml = word.MainDocumentPart!.Document.OuterXml;
        Assert.Contains("Heading1", xml, StringComparison.Ordinal);
        Assert.Contains("ruby", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ScreenshotBoundary", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("w:type=\"page\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_writes_section_properties_for_standard_docx_renderers()
    {
        var exporter = new OpenXmlDocumentExporter();

        await exporter.ExportAsync(new ExportDocument([new ExportParagraph(ExportStyle.Normal, "本文")]), _path, CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var section = word.MainDocumentPart!.Document.Body!.GetFirstChild<SectionProperties>();
        Assert.NotNull(section);
        Assert.NotNull(section.GetFirstChild<PageSize>());
        Assert.NotNull(section.GetFirstChild<PageMargin>());
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
