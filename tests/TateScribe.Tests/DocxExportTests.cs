using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Validation;
using System.Diagnostics;
using TateScribe.Core.Export;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Ruby;
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
    public async Task Ruby_uses_default_three_point_offset_and_explicit_japanese_run_properties()
    {
        await new OpenXmlDocumentExporter().ExportAsync(
            CreateStructuredRubyDocument(),
            _path,
            false,
            "游明朝",
            DocxRubyOptions.Default,
            CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var ruby = Assert.Single(word.MainDocumentPart!.Document.Body!
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Ruby>());
        var rubyProperties = ruby.GetFirstChild<RubyProperties>()!;
        var rubyContentRun = ruby.GetFirstChild<RubyContent>()!.GetFirstChild<Run>()!;
        var rubyBaseRun = ruby.GetFirstChild<RubyBase>()!.GetFirstChild<Run>()!;

        Assert.Equal("16", rubyProperties.GetFirstChild<PhoneticGuideRaise>()!.Val!.Value.ToString());
        Assert.Equal("21", rubyProperties.GetFirstChild<PhoneticGuideBaseTextSize>()!.Val!.Value);
        AssertJapaneseRunProperties(rubyContentRun, "游明朝", "10");
        AssertJapaneseRunProperties(rubyBaseRun, "游明朝", "21");
        Assert.Empty(new OpenXmlValidator().Validate(word));
    }

    [Fact]
    public async Task Legacy_exporter_overloads_forward_default_ruby_options()
    {
        var structuredPath = Path.Combine(
            Path.GetTempPath(),
            $"TateScribe-structured-{Guid.NewGuid():N}.docx");
        try
        {
            var exporter = new OpenXmlDocumentExporter();

            await exporter.ExportAsync(
                new ExportDocument([RubyParagraph(ExportStyle.Normal)]),
                _path,
                CancellationToken.None);
            AssertRubyRaise(_path, "16");

            await exporter.ExportAsync(
                CreateStructuredRubyDocument(),
                structuredPath,
                false,
                "游明朝",
                CancellationToken.None);
            AssertRubyRaise(structuredPath, "16");
        }
        finally
        {
            if (File.Exists(structuredPath)) File.Delete(structuredPath);
        }
    }

    [Fact]
    public async Task Ruby_uses_configured_offset_and_effective_paragraph_sizes()
    {
        var document = new ExportDocument([
            RubyParagraph(ExportStyle.Normal),
            RubyParagraph(ExportStyle.Heading1),
            RubyParagraph(ExportStyle.Heading2),
            RubyParagraph(ExportStyle.Heading3),
        ]);

        await new OpenXmlDocumentExporter().ExportAsync(
            document,
            _path,
            new DocxRubyOptions(5),
            CancellationToken.None);

        using var word = WordprocessingDocument.Open(_path, false);
        var rubies = word.MainDocumentPart!.Document.Body!
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Ruby>()
            .ToArray();

        Assert.Collection(
            rubies,
            ruby => AssertRubyMetrics(ruby, "20", "21"),
            ruby => AssertRubyMetrics(ruby, "20", "32"),
            ruby => AssertRubyMetrics(ruby, "20", "28"),
            ruby => AssertRubyMetrics(ruby, "20", "24"));
        Assert.Empty(new OpenXmlValidator().Validate(word));
    }

    [Fact]
    public async Task Ruby_diagnostic_reads_export_without_modifying_docx()
    {
        var document = new ExportDocument([
            new ExportParagraph(ExportStyle.Normal, "base", new RubyAnnotation("base", "reading"))
        ]);
        await new OpenXmlDocumentExporter().ExportAsync(document, _path, CancellationToken.None);
        var originalBytes = await File.ReadAllBytesAsync(_path);
        var scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "compare-docx-ruby.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-Path");
        startInfo.ArgumentList.Add(_path);

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, error);
        Assert.Contains("w:ruby", output, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(_path));
    }

    [Fact]
    public async Task Ruby_diagnostic_reports_every_missing_docx_path()
    {
        var firstMissingPath = Path.Combine(Path.GetTempPath(), $"TateScribe-missing-{Guid.NewGuid():N}.docx");
        var secondMissingPath = Path.Combine(Path.GetTempPath(), $"TateScribe-missing-{Guid.NewGuid():N}.docx");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), "scripts", "compare-docx-ruby.ps1"));
        startInfo.ArgumentList.Add("-Path");
        startInfo.ArgumentList.Add(firstMissingPath);
        startInfo.ArgumentList.Add(secondMissingPath);

        using var process = Process.Start(startInfo)!;
        await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(1, process.ExitCode);
        Assert.Contains(Path.GetFileName(firstMissingPath), error, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(secondMissingPath), error, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TateScribe.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the TateScribe repository root.");
    }

    private static ExportParagraph RubyParagraph(ExportStyle style) =>
        new(style, "本文", new RubyAnnotation("本文", "ほんぶん"));

    private static StructuredDocument CreateStructuredRubyDocument() =>
        new(
            Guid.NewGuid(),
            [
                new StructuredParagraph(
                    Guid.NewGuid(),
                    DocumentElementRole.BodyParagraph,
                    [new RubyInline(Guid.NewGuid(), "本文", "ほんぶん", RubySource.ImageConfirmed, 1)],
                    "hash",
                    [])
            ],
            "document-hash");

    private static void AssertRubyRaise(string path, string expectedRaise)
    {
        using var word = WordprocessingDocument.Open(path, false);
        var ruby = Assert.Single(word.MainDocumentPart!.Document.Body!
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Ruby>());
        Assert.Equal(
            expectedRaise,
            ruby.GetFirstChild<RubyProperties>()!
                .GetFirstChild<PhoneticGuideRaise>()!.Val!.Value.ToString());
    }

    private static void AssertRubyMetrics(
        DocumentFormat.OpenXml.Wordprocessing.Ruby ruby,
        string expectedRaise,
        string expectedBaseSize)
    {
        var properties = ruby.GetFirstChild<RubyProperties>()!;
        Assert.Equal(expectedRaise, properties.GetFirstChild<PhoneticGuideRaise>()!.Val!.Value.ToString());
        Assert.Equal(expectedBaseSize, properties.GetFirstChild<PhoneticGuideBaseTextSize>()!.Val!.Value);
        Assert.Equal(
            expectedBaseSize,
            ruby.GetFirstChild<RubyBase>()!.GetFirstChild<Run>()!
                .RunProperties!.FontSize!.Val!.Value);
    }

    private static void AssertJapaneseRunProperties(Run run, string expectedFont, string expectedSize)
    {
        var properties = run.RunProperties!;
        Assert.Equal(expectedFont, properties.RunFonts!.Ascii!.Value);
        Assert.Equal(expectedFont, properties.RunFonts.HighAnsi!.Value);
        Assert.Equal(expectedFont, properties.RunFonts.EastAsia!.Value);
        Assert.Equal(expectedSize, properties.FontSize!.Val!.Value);
        Assert.Equal(expectedSize, properties.FontSizeComplexScript!.Val!.Value);
        Assert.Equal("ja-JP", properties.Languages!.EastAsia!.Value);
    }
}
