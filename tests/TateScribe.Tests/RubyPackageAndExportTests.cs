using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using TateScribe.Core.Denden;
using TateScribe.Core.ChatGpt;
using TateScribe.Core.Export;
using TateScribe.Core.Ocr;
using TateScribe.Core.Ruby;
using TateScribe.Infrastructure.Denden;
using TateScribe.Infrastructure.Export;
using TateScribe.Infrastructure.Ruby;

namespace TateScribe.Tests;

public sealed class RubyPackageAndExportTests : IDisposable
{
    private readonly string tempPath = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Candidate_selector_includes_manual_body_returns_but_not_automatic_body_words()
    {
        var runId = Guid.NewGuid();
        var words = new[]
        {
            new OcrWordReviewState(runId, 0, new OcrWord("候補", .9, 1, 2, 3, 4),
                "RubyCandidate", false, false, "RubyCandidate"),
            new OcrWordReviewState(runId, 1, new OcrWord("本文へ戻した", .8, 5, 6, 7, 8),
                "Body", true, true, "RubyCandidate"),
            new OcrWordReviewState(runId, 2, new OcrWord("通常本文", .95, 9, 10, 11, 12),
                "Body", true, true, "Body"),
        };

        var candidates = RubyOcrCandidateSelector.Select("0001", "前後の本文", words);

        Assert.Collection(candidates,
            candidate =>
            {
                Assert.Equal("候補", candidate.OcrText);
                Assert.False(candidate.ReturnedToBody);
            },
            candidate =>
            {
                Assert.Equal("本文へ戻した", candidate.OcrText);
                Assert.True(candidate.ReturnedToBody);
                Assert.True(candidate.IncludedInDraft);
            });
    }

    [Fact]
    public async Task Ruby_package_is_a_directory_with_prompt_schema_document_candidates_and_images()
    {
        Directory.CreateDirectory(tempPath);
        var image = Path.Combine(tempPath, "page.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3]);
        var destination = Path.Combine(tempPath, "ruby-package");
        var document = CreateDocument();
        var request = new RubyPackageRequest(document.ProjectId, Guid.NewGuid(), RubyPolicy.PreserveOriginalOnly,
            document, [new RubyPackagePage(Guid.NewGuid(), "0001", image, null)],
            [new RubyOcrCandidate("0001", "八角", 1, 2, 3, 4, .9, "男の名前", Guid.NewGuid(), false, true)],
            destination);

        await new RubyPackageExporter().ExportAsync(request, CancellationToken.None);

        Assert.True(Directory.Exists(destination));
        Assert.True(File.Exists(Path.Combine(destination, "instructions.md")));
        Assert.True(File.Exists(Path.Combine(destination, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(destination, "confirmed-document.json")));
        Assert.True(File.Exists(Path.Combine(destination, "ruby-candidates.json")));
        Assert.True(File.Exists(Path.Combine(destination, "output-schema.json")));
        Assert.True(File.Exists(Path.Combine(destination, "images-original", "PAGE-0001.png")));
        Assert.Equal(
            new ChatGptPromptTemplateProvider().GetTemplate(ChatGptTaskType.RubyAnnotation),
            await File.ReadAllTextAsync(Path.Combine(destination, "instructions.md")));
    }

    [Fact]
    public async Task Structured_docx_preserves_text_and_emits_multiple_valid_rubies()
    {
        Directory.CreateDirectory(tempPath);
        var path = Path.Combine(tempPath, "book.docx");
        var document = CreateDocument();

        await new OpenXmlDocumentExporter { RubyFontSizeHalfPoints = 8, RubyRaiseHalfPoints = 8 }
            .ExportAsync(document, path, true, "游明朝", CancellationToken.None);

        using var word = WordprocessingDocument.Open(path, false);
        var validationErrors = new OpenXmlValidator().Validate(word).ToArray();
        Assert.True(validationErrors.Length == 0,
            string.Join(Environment.NewLine, validationErrors.Select(error =>
                $"{error.Description} Node={error.Node?.OuterXml} Path={error.Path?.XPath}")));
        var xml = word.MainDocumentPart!.Document.OuterXml;
        Assert.Equal(2, Count(xml, "<w:ruby>"));
        Assert.Contains("<w:hps w:val=\"8\"", xml, StringComparison.Ordinal);
        Assert.Contains("八角", xml, StringComparison.Ordinal);
        Assert.Contains("万二", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denden_output_is_deterministic_utf8_lf_and_escapes_body_markup()
    {
        Directory.CreateDirectory(tempPath);
        var first = Path.Combine(tempPath, "first");
        var second = Path.Combine(tempPath, "second");
        var document = CreateDocument(withMarkup: true);
        var options = new DendenExportOptions("書名", "著者");

        await new DendenExportService().ExportAsync(document, options, first, CancellationToken.None);
        await new DendenExportService().ExportAsync(document, options, second, CancellationToken.None);

        foreach (var name in new[] { "book.md", "ddconv.yml", "default.css", "README.txt" })
        {
            var left = await File.ReadAllBytesAsync(Path.Combine(first, name));
            var right = await File.ReadAllBytesAsync(Path.Combine(second, name));
            Assert.Equal(left, right);
            Assert.False(left.Length >= 3 && left[0] == 0xEF && left[1] == 0xBB && left[2] == 0xBF);
            Assert.DoesNotContain((byte)'\r', left);
        }
        var markdown = await File.ReadAllTextAsync(Path.Combine(first, "book.md"), Encoding.UTF8);
        Assert.Contains("{八角|やすみ}", markdown, StringComparison.Ordinal);
        Assert.Contains("\\{注\\}", markdown, StringComparison.Ordinal);
        Assert.Contains("pageDirection: rtl", await File.ReadAllTextAsync(Path.Combine(first, "ddconv.yml")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(first, "ruby.csv")));
        Assert.Empty(Directory.GetFiles(first, "*.epub"));
        Assert.Empty(Directory.GetFiles(first, "*.zip"));
    }

    [Fact]
    public async Task Denden_horizontal_option_keeps_yaml_and_css_writing_direction_consistent()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "horizontal");

        await new DendenExportService().ExportAsync(
            CreateDocument(),
            new DendenExportOptions("書名", "著者", VerticalWriting: false),
            destination,
            CancellationToken.None);

        Assert.Contains(
            "pageDirection: ltr",
            await File.ReadAllTextAsync(Path.Combine(destination, "ddconv.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "writing-mode: horizontal-tb",
            await File.ReadAllTextAsync(Path.Combine(destination, "default.css")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denden_copies_only_explicit_cover_and_illustration_inputs_with_stable_names()
    {
        Directory.CreateDirectory(tempPath);
        var cover = Path.Combine(tempPath, "selected-cover.jpeg");
        var firstIllustration = Path.Combine(tempPath, "scene-b.png");
        var secondIllustration = Path.Combine(tempPath, "scene-a.jpg");
        await File.WriteAllBytesAsync(cover, [1, 2, 3]);
        await File.WriteAllBytesAsync(firstIllustration, [4, 5]);
        await File.WriteAllBytesAsync(secondIllustration, [6, 7]);
        var destination = Path.Combine(tempPath, "with-images");

        await new DendenExportService().ExportAsync(
            CreateDocument(),
            new DendenExportOptions(
                "書名",
                "著者",
                CoverImagePath: cover,
                IllustrationImagePaths: [firstIllustration, secondIllustration]),
            destination,
            CancellationToken.None);

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            await File.ReadAllBytesAsync(Path.Combine(destination, "cover.jpg")));
        Assert.Equal(
            new byte[] { 4, 5 },
            await File.ReadAllBytesAsync(Path.Combine(
                destination, "images", "illustration-001.png")));
        Assert.Equal(
            new byte[] { 6, 7 },
            await File.ReadAllBytesAsync(Path.Combine(
                destination, "images", "illustration-002.jpg")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(destination, "images")).Length);
    }

    [Fact]
    public async Task Denden_maps_roles_splits_chapters_and_keeps_different_readings_inline()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "split");
        var pageId = Guid.NewGuid();
        StructuredParagraph Paragraph(DocumentElementRole role, params InlineElement[] inlines)
        {
            var text = string.Concat(inlines.Select(inline => inline is TextInline t ? t.Text : ((RubyInline)inline).BaseText));
            return new StructuredParagraph(Guid.NewGuid(), role, inlines, DocumentTextHash.Compute(text),
                [new SourceSpan(pageId, "0001", 0, text.Length)]);
        }
        var draft = new StructuredDocument(Guid.NewGuid(),
        [
            Paragraph(DocumentElementRole.ChapterTitle, new TextInline("第一章")),
            Paragraph(DocumentElementRole.BodyParagraph,
                new RubyInline(Guid.NewGuid(), "八角", "やすみ", RubySource.ImageConfirmed, 1),
                new TextInline("と"),
                new RubyInline(Guid.NewGuid(), "八角", "はっかく", RubySource.TextConfirmed, 1)),
            Paragraph(DocumentElementRole.SectionTitle, new TextInline("小見出し")),
            Paragraph(DocumentElementRole.SectionNumber, new TextInline("2")),
            Paragraph(DocumentElementRole.SceneBreak, new TextInline(string.Empty)),
            Paragraph(DocumentElementRole.ChapterTitle, new TextInline("第二章")),
            Paragraph(DocumentElementRole.BodyParagraph, new TextInline("本文")),
        ], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };

        await new DendenExportService().ExportAsync(document,
            new DendenExportOptions("書名", "著者", SplitByChapter: true),
            destination, CancellationToken.None);

        Assert.Equal(string.Empty, await File.ReadAllTextAsync(Path.Combine(destination, "book.md")));
        var firstChapter = await File.ReadAllTextAsync(Path.Combine(destination, "chapter-001.md"));
        Assert.Contains("# 第一章", firstChapter, StringComparison.Ordinal);
        Assert.Contains("{八角|やすみ}と{八角|はっかく}", firstChapter, StringComparison.Ordinal);
        Assert.Contains("## 小見出し", firstChapter, StringComparison.Ordinal);
        Assert.Contains("## 2", firstChapter, StringComparison.Ordinal);
        Assert.Contains("***", firstChapter, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, "chapter-002.md")));
    }

    [Fact]
    public async Task Ruby_csv_is_opt_in_and_rejects_a_term_with_conflicting_readings()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "conflict");
        var pageId = Guid.NewGuid();
        var text = "八角と八角";
        var paragraph = new StructuredParagraph(Guid.NewGuid(), DocumentElementRole.BodyParagraph,
        [
            new RubyInline(Guid.NewGuid(), "八角", "やすみ", RubySource.ImageConfirmed, 1),
            new TextInline("と"),
            new RubyInline(Guid.NewGuid(), "八角", "はっかく", RubySource.TextConfirmed, 1),
        ], DocumentTextHash.Compute(text), [new SourceSpan(pageId, "0001", 0, text.Length)]);
        var draft = new StructuredDocument(Guid.NewGuid(), [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DendenExportService().ExportAsync(document,
                new DendenExportOptions("書名", "著者",
                    ApprovedGlobalRubies: new Dictionary<string, string> { ["八角"] = "やすみ" }),
                destination, CancellationToken.None));

        Assert.Contains("複数の読み", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    private static StructuredDocument CreateDocument(bool withMarkup = false)
    {
        var pageId = Guid.NewGuid();
        var text = withMarkup ? "男の{注}八角と万二" : "男の名前は八角と万二";
        var baseParagraph = new StructuredParagraph(
            Guid.NewGuid(), DocumentElementRole.BodyParagraph, [new TextInline(text)],
            DocumentTextHash.Compute(text), [new SourceSpan(pageId, "0001", 0, text.Length)], "page-0001:0");
        var firstStart = text.IndexOf("八角", StringComparison.Ordinal);
        var secondStart = text.IndexOf("万二", StringComparison.Ordinal);
        var withRuby = RubyDocumentComposer.Apply(baseParagraph,
        [
            new(baseParagraph.ParagraphId.ToString("D"), firstStart, 2, "八角", "やすみ",
                RubySource.ImageConfirmed, 1, ["0001"], "画像", Guid.NewGuid(), RubyAnnotationStatus.Confirmed),
            new(baseParagraph.ParagraphId.ToString("D"), secondStart, 2, "万二", "まんじ",
                RubySource.TextConfirmed, 1, ["0001"], "本文", Guid.NewGuid(), RubyAnnotationStatus.Confirmed),
        ]);
        var draft = new StructuredDocument(Guid.NewGuid(), [withRuby], string.Empty);
        return draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        for (var start = 0; (start = value.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0; start += fragment.Length) count++;
        return count;
    }

    public void Dispose()
    {
        if (Directory.Exists(tempPath)) Directory.Delete(tempPath, recursive: true);
    }
}
