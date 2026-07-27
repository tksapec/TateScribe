using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Validation;
using TateScribe.Core.Export;
using TateScribe.Core.Proofreading;
using TateScribe.Infrastructure.Export;

namespace TateScribe.Tests;

public sealed class DocxExportTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"TateScribe-{Guid.NewGuid():N}.docx");

    [Fact]
    public void Export_preflight_uses_one_summary_for_docx_and_denden_safety_counts()
    {
        var page = new TateScribe.Core.Projects.ProjectPage(
            Guid.NewGuid(), "other.png", "other.png", "hash", 0, true, 0,
            PageRole: TateScribe.Core.Projects.PageRole.Other);
        var preflight = new ExportPreflightResult(
            120,
            3,
            1,
            [page],
            45,
            2,
            4,
            2,
            3,
            [new ExportPreflightIssue(
                "IllustrationPlacementAdjusted",
                "挿絵位置を段落後へ調整しました。")]);

        var message = preflight.FormatConfirmation("でんでん用データ");

        Assert.True(preflight.RequiresConfirmation);
        Assert.False(preflight.HasFatalErrors);
        Assert.Contains("未校正ページ: 3", message, StringComparison.Ordinal);
        Assert.Contains("PageRole=Otherの本文ページ: 1", message, StringComparison.Ordinal);
        Assert.Contains("確定ルビ: 45", message, StringComparison.Ordinal);
        Assert.Contains("未確定ルビ: 4", message, StringComparison.Ordinal);
        Assert.Contains("Proposedルビ: 2", message, StringComparison.Ordinal);
        Assert.Contains("Staleルビ: 2", message, StringComparison.Ordinal);
        Assert.Contains("挿絵: 3", message, StringComparison.Ordinal);
        Assert.Contains("出力されません", message, StringComparison.Ordinal);
    }

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
    public async Task Export_recognizes_a_chapter_after_a_direct_join_body_page()
    {
        var chapter = BookDocumentAssembler.CreateChapterPageText("Chapter title");
        var document = BookDocumentAssembler.Assemble([
            new ExportPageText("Previous body", BoundaryJoinType.DirectJoin),
            new ExportPageText(chapter, BoundaryJoinType.DirectJoin),
        ]) with
        {
            PageBreakBeforeChapters = true,
        };

        await new OpenXmlDocumentExporter().ExportAsync(
            document, _path, CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var xml = word.MainDocumentPart!.Document.OuterXml;
        var paragraphs = word.MainDocumentPart.Document.Body!
            .Elements<Paragraph>()
            .ToArray();
        Assert.Equal(2, paragraphs.Length);
        Assert.Equal("Previous body", paragraphs[0].InnerText);
        Assert.Equal("Chapter title", paragraphs[1].InnerText);
        Assert.Equal(
            "Heading1",
            paragraphs[1].ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        var chapterProperties = paragraphs[1].ParagraphProperties;
        Assert.NotNull(chapterProperties);
        Assert.NotNull(chapterProperties.GetFirstChild<PageBreakBefore>());
        Assert.DoesNotContain("[[CHAPTER:", xml, StringComparison.Ordinal);
        Assert.Empty(new OpenXmlValidator().Validate(word));
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
