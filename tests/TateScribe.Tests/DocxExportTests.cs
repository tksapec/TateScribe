using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Validation;
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

    [Fact]
    public async Task Export_adds_a_page_break_before_chapters_only_when_enabled()
    {
        var exporter = new OpenXmlDocumentExporter();
        var document = new ExportDocument([new ExportParagraph(ExportStyle.Heading1, "第一章", null, DocumentElementRole.ChapterTitle)], PageBreakBeforeChapters: true);

        await exporter.ExportAsync(document, _path, CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        Assert.Contains("pageBreakBefore", word.MainDocumentPart!.Document.OuterXml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_defines_required_styles_and_validates_as_open_xml()
    {
        var exporter = new OpenXmlDocumentExporter();
        var document = new ExportDocument([
            new ExportParagraph(ExportStyle.Heading1, "章", null, DocumentElementRole.ChapterTitle),
            new ExportParagraph(ExportStyle.Normal, "1", null, DocumentElementRole.SectionNumber),
            new ExportParagraph(ExportStyle.Normal, "＊", null, DocumentElementRole.SceneBreak),
            new ExportParagraph(ExportStyle.Normal, "　本文")
        ], PageBreakBeforeChapters: true);

        await exporter.ExportAsync(document, _path, CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var styles = word.MainDocumentPart!.StyleDefinitionsPart!.Styles!;
        foreach (var styleId in new[] { "Normal", "Heading1", "Heading2", "Heading3", "SectionNumber", "SceneBreak" })
            Assert.Contains(styles.Elements<Style>(), style => style.StyleId == styleId);
        var paragraphs = word.MainDocumentPart.Document.Body!.Elements<Paragraph>().ToArray();
        Assert.Equal("SectionNumber", paragraphs[1].ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Equal("SceneBreak", paragraphs[2].ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Empty(new OpenXmlValidator().Validate(word));
    }

    [Fact]
    public async Task Export_preserves_an_intentional_blank_body_paragraph()
    {
        var document = BookDocumentAssembler.Assemble(["第一段落\n\n第二段落"]);

        await new OpenXmlDocumentExporter().ExportAsync(document, _path, CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var paragraphs = word.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToArray();
        Assert.Equal(3, paragraphs.Length);
        Assert.Equal(string.Empty, paragraphs[1].InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(word));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
