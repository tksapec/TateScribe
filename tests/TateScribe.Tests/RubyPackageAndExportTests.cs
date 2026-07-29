using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenCvSharp;
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
                Assert.Equal("候補", candidate.ReadingCandidate);
                Assert.Null(candidate.BaseTextCandidate);
                Assert.False(candidate.ReturnedToBody);
            },
            candidate =>
            {
                Assert.Equal("本文へ戻した", candidate.ReadingCandidate);
                Assert.Null(candidate.BaseTextCandidate);
                Assert.True(candidate.ReturnedToBody);
                Assert.True(candidate.IncludedInDraft);
            });
    }

    [Fact]
    public void Candidate_selector_links_reading_to_a_nearby_vertical_body_region()
    {
        var runId = Guid.NewGuid();
        var words = new[]
        {
            new OcrWordReviewState(runId, 0, new OcrWord("ヤスミ", .91, 80, 10, 90, 50),
                "RubyCandidate", false, false, "RubyCandidate"),
            new OcrWordReviewState(runId, 1, new OcrWord("八角", .97, 60, 12, 76, 48),
                "Body", true, true, "Body"),
            new OcrWordReviewState(runId, 2, new OcrWord("遠い本文", .95, 10, 200, 30, 250),
                "Body", true, true, "Body"),
        };

        var candidate = Assert.Single(RubyOcrCandidateSelector.Select(
            "0001", "ページ全体の本文を親文字にしない", words));

        Assert.Equal("ヤスミ", candidate.ReadingCandidate);
        Assert.Equal("八角", candidate.BaseTextCandidate);
        Assert.NotNull(candidate.LinkConfidence);
        Assert.True(candidate.LinkConfidence > 0.5);
        Assert.NotEqual("ページ全体の本文を親文字にしない", candidate.BaseTextCandidate);
    }

    [Fact]
    public void Candidate_selector_leaves_base_text_null_when_coordinate_link_is_ambiguous()
    {
        var runId = Guid.NewGuid();
        var words = new[]
        {
            new OcrWordReviewState(runId, 0, new OcrWord("よみ", .9, 50, 10, 60, 50),
                "RubyCandidate", false, false, "RubyCandidate"),
            new OcrWordReviewState(runId, 1, new OcrWord("甲", .9, 35, 10, 45, 50),
                "Body", true, true, "Body"),
            new OcrWordReviewState(runId, 2, new OcrWord("乙", .9, 65, 10, 75, 50),
                "Body", true, true, "Body"),
        };

        var candidate = Assert.Single(RubyOcrCandidateSelector.Select(
            "0001", "甲乙", words));

        Assert.Null(candidate.BaseTextCandidate);
        Assert.Null(candidate.LinkConfidence);
    }

    [Fact]
    public void Candidate_selector_does_not_link_a_vertically_overlapping_but_distant_body_region()
    {
        var runId = Guid.NewGuid();
        var words = new[]
        {
            new OcrWordReviewState(runId, 0, new OcrWord("ヤスミ", .92, 500, 10, 510, 50),
                "RubyCandidate", true, true, "RubyCandidate"),
            new OcrWordReviewState(runId, 1, new OcrWord("八角", .95, 0, 10, 10, 50),
                "Body", true, true, "Body"),
        };

        var candidate = Assert.Single(RubyOcrCandidateSelector.Select(
            "0001", "八角", words));

        Assert.Null(candidate.BaseTextCandidate);
        Assert.Null(candidate.LinkConfidence);
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
        var candidatesJson = await File.ReadAllTextAsync(
            Path.Combine(destination, "ruby-candidates.json"));
        Assert.Contains("\"readingCandidate\"", candidatesJson, StringComparison.Ordinal);
        Assert.Contains("\"baseTextCandidate\"", candidatesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ocrText\"", candidatesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"legacyAdjacentBodyText\"", candidatesJson, StringComparison.Ordinal);
        var schema = await File.ReadAllTextAsync(Path.Combine(destination, "output-schema.json"));
        using var parsedSchema = JsonDocument.Parse(schema);
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            parsedSchema.RootElement.GetProperty("$schema").GetString());
        Assert.Contains("\"evidence\": { \"type\": \"string\", \"minLength\": 1", schema, StringComparison.Ordinal);
        Assert.Contains("\"uniqueItems\": true", schema, StringComparison.Ordinal);
        Assert.Contains("\"minItems\": 1", schema, StringComparison.Ordinal);
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
            var left = await File.ReadAllBytesAsync(Path.Combine(first, name == "README.txt" ? name : Path.Combine("upload", name)));
            var right = await File.ReadAllBytesAsync(Path.Combine(second, name == "README.txt" ? name : Path.Combine("upload", name)));
            Assert.Equal(left, right);
            Assert.False(left.Length >= 3 && left[0] == 0xEF && left[1] == 0xBB && left[2] == 0xBF);
            Assert.DoesNotContain((byte)'\r', left);
        }
        var markdown = await File.ReadAllTextAsync(Path.Combine(first, "upload", "book.md"), Encoding.UTF8);
        Assert.Contains("{八角|やすみ}", markdown, StringComparison.Ordinal);
        Assert.Contains("\\{注\\}", markdown, StringComparison.Ordinal);
        var yaml = await File.ReadAllTextAsync(Path.Combine(first, "upload", "ddconv.yml"));
        Assert.StartsWith("ddconvVersion: 1.0\n", yaml, StringComparison.Ordinal);
        Assert.Contains("titles:\n  - content: \"書名\"", yaml, StringComparison.Ordinal);
        Assert.Contains("creators:\n  - content: \"著者\"\n    role: aut", yaml, StringComparison.Ordinal);
        Assert.Contains("pageDirection: rtl", yaml, StringComparison.Ordinal);
        Assert.Contains("  skipCover: true", yaml, StringComparison.Ordinal);
        Assert.Contains("  titlepage: true", yaml, StringComparison.Ordinal);
        Assert.Contains("  tocInSpine: true", yaml, StringComparison.Ordinal);
        Assert.Contains("  tocDisplayDepth: 2", yaml, StringComparison.Ordinal);
        Assert.Contains("  displayLandmarksNav: false", yaml, StringComparison.Ordinal);
        Assert.Contains("  displayLoiNav: false", yaml, StringComparison.Ordinal);
        Assert.Contains("  tcyDigit: 2", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("titlePage:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("tableOfContents", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("tcyDigitCount", yaml, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(first, "upload", "ruby.csv")));
        Assert.Empty(Directory.GetFiles(first, "*.epub"));
        Assert.Empty(Directory.GetFiles(first, "*.zip"));
    }

    [Fact]
    public async Task Denden_writes_a_root_readme_and_an_upload_only_package()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "upload-layout");

        await new DendenExportService().ExportAsync(
            CreateDocument(), new DendenExportOptions("Book", "Author"), destination, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destination, "README.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "book.md")));
        var upload = Path.Combine(destination, "upload");
        Assert.Equal(
            ["book.md", "ddconv.yml", "default.css"],
            Directory.GetFiles(upload).Select(path => Path.GetFileName(path)!).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        var readme = await File.ReadAllTextAsync(Path.Combine(destination, "README.txt"));
        Assert.Contains("upload", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every file", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never select or upload this README", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Denden_escapes_untrusted_body_text_without_escaping_generated_markdown_or_rubies()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "escaping");
        var pageId = Guid.NewGuid();
        StructuredParagraph Paragraph(DocumentElementRole role, params InlineElement[] inlines)
        {
            var text = string.Concat(inlines.Select(inline => inline is TextInline value ? value.Text : ((RubyInline)inline).BaseText));
            return new StructuredParagraph(Guid.NewGuid(), role, inlines, DocumentTextHash.Compute(text),
                [new SourceSpan(pageId, "0001", 0, text.Length)]);
        }
        var draft = new StructuredDocument(Guid.NewGuid(),
        [
            Paragraph(DocumentElementRole.ChapterTitle, new TextInline("A & <B>")),
            Paragraph(DocumentElementRole.BodyParagraph, new TextInline("1986. What a great season.")),
            Paragraph(DocumentElementRole.BodyParagraph, new TextInline("    indented")),
            Paragraph(DocumentElementRole.BodyParagraph, new TextInline("\tindented")),
            Paragraph(DocumentElementRole.BodyParagraph, new TextInline("{brace|pipe}\\backslash *em* [link]! `code` _under_")),
            Paragraph(DocumentElementRole.BodyParagraph, new TextInline("\u3000full width space")),
            Paragraph(DocumentElementRole.BodyParagraph, new RubyInline(Guid.NewGuid(), "base", "reading", RubySource.TextConfirmed, 1)),
            Paragraph(DocumentElementRole.SceneBreak, new TextInline(string.Empty)),
        ], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };

        await new DendenExportService().ExportAsync(document, new DendenExportOptions("Book", "Author"), destination, CancellationToken.None);

        var markdown = await File.ReadAllTextAsync(Path.Combine(destination, "upload", "book.md"));
        Assert.Contains("# A &amp; &lt;B&gt;", markdown, StringComparison.Ordinal);
        Assert.Contains("1986&#46; What a great season.", markdown, StringComparison.Ordinal);
        Assert.Contains("&#32;&#32;&#32;&#32;indented", markdown, StringComparison.Ordinal);
        Assert.Contains("&#9;indented", markdown, StringComparison.Ordinal);
        Assert.Contains("\\{brace\\|pipe\\}\\\\backslash \\*em\\* \\[link\\]\\! \\`code\\` \\_under\\_", markdown, StringComparison.Ordinal);
        Assert.Contains("\u3000full width space", markdown, StringComparison.Ordinal);
        Assert.Contains("{base|reading}", markdown, StringComparison.Ordinal);
        Assert.Contains("***", markdown, StringComparison.Ordinal);
        var renderedLines = markdown.Split('\n').Select(System.Net.WebUtility.HtmlDecode).ToArray();
        Assert.Contains("1986. What a great season.", renderedLines);
        Assert.Contains("    indented", renderedLines);
        Assert.Contains("\tindented", renderedLines);
    }

    [Fact]
    public async Task Denden_applies_illustration_rotation_then_crop_without_changing_the_source()
    {
        Directory.CreateDirectory(tempPath);
        var source = Path.Combine(tempPath, "rotated.png");
        using (var image = new Mat(6, 8, MatType.CV_8UC3, new Scalar(10, 20, 30)))
            Assert.True(Cv2.ImWrite(source, image));
        var original = await File.ReadAllBytesAsync(source);
        var illustration = new DendenIllustration(
            Guid.NewGuid(), 1, source, "Illustration", Crop: new TateScribe.Core.Images.NormalizedCrop(0, 0, .5, 1), RotationDegrees: 90);
        var document = CreateDocument();
        var export = new DendenExportDocument(document,
            [new DendenParagraphBlock(document.Paragraphs[0]), new DendenIllustrationBlock(illustration)]);
        var destination = Path.Combine(tempPath, "transformed-illustration");

        await new DendenExportService().ExportAsync(export, new DendenExportOptions("Book", "Author"), destination, CancellationToken.None);

        using var rendered = Cv2.ImRead(Path.Combine(destination, "upload", "illustration-001.png"), ImreadModes.Color);
        Assert.Equal(3, rendered.Width);
        Assert.Equal(8, rendered.Height);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Theory]
    [InlineData(90, "left", 3, 8, 0, 5)]
    [InlineData(90, "right", 3, 8, 0, 2)]
    [InlineData(90, "top", 6, 4, 0, 5)]
    [InlineData(90, "bottom", 6, 4, 4, 5)]
    [InlineData(180, "left", 4, 6, 7, 5)]
    [InlineData(180, "right", 4, 6, 3, 5)]
    [InlineData(180, "top", 8, 3, 7, 5)]
    [InlineData(180, "bottom", 8, 3, 7, 2)]
    [InlineData(270, "left", 3, 8, 7, 0)]
    [InlineData(270, "right", 3, 8, 7, 3)]
    [InlineData(270, "top", 6, 4, 7, 0)]
    [InlineData(270, "bottom", 6, 4, 3, 0)]
    public async Task Denden_transforms_all_crop_sides_in_rotated_coordinates(
        int rotation,
        string side,
        int expectedWidth,
        int expectedHeight,
        byte expectedSourceX,
        byte expectedSourceY)
    {
        Directory.CreateDirectory(tempPath);
        var source = Path.Combine(tempPath, $"coordinate-{rotation}-{side}.png");
        WriteCoordinateImage(source);
        var crop = side switch
        {
            "left" => new TateScribe.Core.Images.NormalizedCrop(0, 0, .5, 1),
            "right" => new TateScribe.Core.Images.NormalizedCrop(.5, 0, 1, 1),
            "top" => new TateScribe.Core.Images.NormalizedCrop(0, 0, 1, .5),
            _ => new TateScribe.Core.Images.NormalizedCrop(0, .5, 1, 1),
        };
        var document = CreateDocument();
        var export = new DendenExportDocument(document,
        [
            new DendenParagraphBlock(document.Paragraphs[0]),
            new DendenIllustrationBlock(new DendenIllustration(
                Guid.NewGuid(), 1, source, "Illustration", Crop: crop, RotationDegrees: rotation)),
        ]);
        var destination = Path.Combine(tempPath, $"coordinates-{rotation}-{side}");

        await new DendenExportService().ExportAsync(export, new DendenExportOptions("Book", "Author"), destination, CancellationToken.None);

        using var rendered = Cv2.ImRead(Path.Combine(destination, "upload", "illustration-001.png"), ImreadModes.Color);
        Assert.Equal(expectedWidth, rendered.Width);
        Assert.Equal(expectedHeight, rendered.Height);
        var pixel = rendered.At<Vec3b>(0, 0);
        Assert.Equal(expectedSourceX, pixel.Item0);
        Assert.Equal(expectedSourceY, pixel.Item1);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("webp")]
    public async Task Denden_transformed_supported_and_converted_illustrations_are_png_and_preserve_originals(string extension)
    {
        Directory.CreateDirectory(tempPath);
        var source = Path.Combine(tempPath, $"transformed.{extension}");
        if (extension == "gif")
            await File.WriteAllBytesAsync(source, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="));
        else
            WriteArtificialImage(source, new Scalar(15, 30, 45));
        var original = await File.ReadAllBytesAsync(source);
        var document = CreateDocument();
        var export = new DendenExportDocument(document,
        [
            new DendenParagraphBlock(document.Paragraphs[0]),
            new DendenIllustrationBlock(new DendenIllustration(
                Guid.NewGuid(), 1, source, "Illustration", RotationDegrees: 90)),
        ]);
        var destination = Path.Combine(tempPath, $"transformed-{extension}");

        await new DendenExportService().ExportAsync(export, new DendenExportOptions("Book", "Author"), destination, CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(destination, "upload", "illustration-001.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, bytes[..4]);
        Assert.False(File.Exists(Path.Combine(destination, "upload", "illustration-001.jpg")));
        Assert.False(File.Exists(Path.Combine(destination, "upload", "illustration-001.gif")));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task Denden_rejects_a_transformed_image_larger_than_three_mebibytes()
    {
        Directory.CreateDirectory(tempPath);
        var source = Path.Combine(tempPath, "oversized-transformed.png");
        using (var image = new Mat(1600, 1600, MatType.CV_8UC3))
        {
            Cv2.Randu(image, Scalar.All(0), Scalar.All(256));
            Assert.True(Cv2.ImWrite(source, image));
        }
        var document = CreateDocument();
        var export = new DendenExportDocument(document,
        [
            new DendenParagraphBlock(document.Paragraphs[0]),
            new DendenIllustrationBlock(new DendenIllustration(
                Guid.NewGuid(), 1, source, "Illustration", RotationDegrees: 90)),
        ]);
        var destination = Path.Combine(tempPath, "oversized-transformed-output");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DendenExportService().ExportAsync(export, new DendenExportOptions("Book", "Author"), destination, CancellationToken.None));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Denden_rejects_invalid_tcy_digit_before_creating_destination()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "invalid-tcy");

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new DendenExportService().ExportAsync(
                CreateDocument(),
                new DendenExportOptions("書名", "著者", TcyDigitCount: 1),
                destination,
                CancellationToken.None));

        Assert.Equal("TcyDigitCount", error.ParamName);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Denden_uses_ja_when_language_is_blank_and_quotes_yaml_safely()
    {
        Directory.CreateDirectory(tempPath);
        var destination = Path.Combine(tempPath, "yaml-escaping");

        await new DendenExportService().ExportAsync(
            CreateDocument(),
            new DendenExportOptions("題名:\n\"引用\"", "著者\\名\t補記", Language: " "),
            destination,
            CancellationToken.None);

        var yaml = await File.ReadAllTextAsync(Path.Combine(destination, "upload", "ddconv.yml"));
        Assert.Contains("content: \"題名:\\n\\\"引用\\\"\"", yaml, StringComparison.Ordinal);
        Assert.Contains("content: \"著者\\\\名\\t補記\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("題名:\n", yaml, StringComparison.Ordinal);
        Assert.Contains("language: \"ja\"", yaml, StringComparison.Ordinal);
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
            await File.ReadAllTextAsync(Path.Combine(destination, "upload", "ddconv.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "writing-mode: horizontal-tb",
            await File.ReadAllTextAsync(Path.Combine(destination, "upload", "default.css")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denden_copies_only_explicit_cover_and_illustration_inputs_with_stable_names()
    {
        Directory.CreateDirectory(tempPath);
        var cover = Path.Combine(tempPath, "selected-cover.jpeg");
        var firstIllustration = Path.Combine(tempPath, "scene-b.png");
        var secondIllustration = Path.Combine(tempPath, "scene-a.jpg");
        var thirdIllustration = Path.Combine(tempPath, "scene-c.gif");
        WriteArtificialImage(cover, new Scalar(10, 20, 30));
        WriteArtificialImage(firstIllustration, new Scalar(40, 50, 60));
        WriteArtificialImage(secondIllustration, new Scalar(70, 80, 90));
        await File.WriteAllBytesAsync(
            thirdIllustration,
            Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="));
        var destination = Path.Combine(tempPath, "with-images");

        await new DendenExportService().ExportAsync(
            CreateDocument(),
            new DendenExportOptions(
                "書名",
                "著者",
                CoverImagePath: cover,
                IllustrationImagePaths: [firstIllustration, secondIllustration, thirdIllustration]),
            destination,
            CancellationToken.None);

        Assert.Equal(
            await File.ReadAllBytesAsync(cover),
            await File.ReadAllBytesAsync(Path.Combine(destination, "upload", "cover.jpg")));
        Assert.Equal(
            await File.ReadAllBytesAsync(firstIllustration),
            await File.ReadAllBytesAsync(Path.Combine(destination, "upload", "illustration-001.png")));
        Assert.Equal(
            await File.ReadAllBytesAsync(secondIllustration),
            await File.ReadAllBytesAsync(Path.Combine(destination, "upload", "illustration-002.jpg")));
        Assert.Equal(
            await File.ReadAllBytesAsync(thirdIllustration),
            await File.ReadAllBytesAsync(Path.Combine(destination, "upload", "illustration-003.gif")));
        Assert.False(Directory.Exists(Path.Combine(destination, "images")));
        var markdown = await File.ReadAllTextAsync(Path.Combine(destination, "upload", "book.md"));
        Assert.Contains("![挿絵 1](illustration-001.png)", markdown, StringComparison.Ordinal);
        Assert.Contains("![挿絵 2](illustration-002.jpg)", markdown, StringComparison.Ordinal);
        Assert.Contains("![挿絵 3](illustration-003.gif)", markdown, StringComparison.Ordinal);
        Assert.Contains("Select every file inside upload", await File.ReadAllTextAsync(
            Path.Combine(destination, "README.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denden_converts_webp_cover_to_png_and_matches_file_signature()
    {
        Directory.CreateDirectory(tempPath);
        var cover = Path.Combine(tempPath, "cover.webp");
        WriteArtificialImage(cover, new Scalar(20, 40, 60));
        var destination = Path.Combine(tempPath, "converted-cover");

        await new DendenExportService().ExportAsync(
            CreateDocument(),
            new DendenExportOptions("書名", "著者", CoverImagePath: cover),
            destination,
            CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(destination, "upload", "cover.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, bytes[..4]);
        Assert.False(File.Exists(Path.Combine(destination, "upload", "cover.jpg")));
    }

    [Theory]
    [InlineData("broken.png", "iVBORw0KGgo=")]
    [InlineData("broken.jpg", "/9j/")]
    [InlineData("broken.gif", "R0lGODlh")]
    public async Task Denden_rejects_truncated_supported_images_before_creating_destination(
        string fileName,
        string base64)
    {
        Directory.CreateDirectory(tempPath);
        var cover = Path.Combine(tempPath, fileName);
        await File.WriteAllBytesAsync(cover, Convert.FromBase64String(base64));
        var destination = Path.Combine(tempPath, $"invalid-{Path.GetExtension(fileName)[1..]}");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new DendenExportService().ExportAsync(
                CreateDocument(),
                new DendenExportOptions("書名", "著者", CoverImagePath: cover),
                destination,
                CancellationToken.None));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Denden_rejects_a_header_complete_but_undecodable_gif()
    {
        Directory.CreateDirectory(tempPath);
        var cover = Path.Combine(tempPath, "spoofed.gif");
        await File.WriteAllBytesAsync(
            cover,
            [
                .. "GIF89a"u8.ToArray(),
                0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                0x2c, 0x00, 0x3b,
            ]);
        var destination = Path.Combine(tempPath, "spoofed-gif");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new DendenExportService().ExportAsync(
                CreateDocument(),
                new DendenExportOptions("書名", "著者", CoverImagePath: cover),
                destination,
                CancellationToken.None));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Denden_allows_exactly_one_hundred_upload_files()
    {
        Directory.CreateDirectory(tempPath);
        var illustration = Path.Combine(tempPath, "illustration.png");
        WriteArtificialImage(illustration, new Scalar(1, 2, 3));
        var destination = Path.Combine(tempPath, "exactly-one-hundred-files");

        await new DendenExportService().ExportAsync(
            CreateDocument(),
            new DendenExportOptions(
                "書名",
                "著者",
                IllustrationImagePaths: Enumerable.Repeat(illustration, 97).ToArray()),
            destination,
            CancellationToken.None);

        Assert.Equal(100, Directory.GetFiles(Path.Combine(destination, "upload")).Length);
        Assert.True(File.Exists(Path.Combine(destination, "README.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "upload", "README.txt")));
    }

    [Fact]
    public async Task Denden_rejects_one_hundred_and_one_upload_files_before_creation()
    {
        Directory.CreateDirectory(tempPath);
        var illustration = Path.Combine(tempPath, "illustration.png");
        WriteArtificialImage(illustration, new Scalar(1, 2, 3));
        var destination = Path.Combine(tempPath, "one-hundred-and-one-files");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DendenExportService().ExportAsync(
                CreateDocument(),
                new DendenExportOptions(
                    "書名",
                    "著者",
                    IllustrationImagePaths: Enumerable.Repeat(illustration, 98).ToArray()),
                destination,
                CancellationToken.None));

        Assert.Contains("100", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Denden_rejects_an_output_image_larger_than_three_mebibytes()
    {
        Directory.CreateDirectory(tempPath);
        var oversized = Path.Combine(tempPath, "oversized.png");
        byte[] encoded;
        using (var image = new Mat(1600, 1600, MatType.CV_8UC3))
        {
            Cv2.Randu(image, Scalar.All(0), Scalar.All(256));
            Cv2.ImEncode(
                ".png",
                image,
                out encoded,
                new ImageEncodingParam(ImwriteFlags.PngCompression, 0));
        }
        await File.WriteAllBytesAsync(oversized, encoded);
        Assert.True(new FileInfo(oversized).Length > 3 * 1024 * 1024);
        var destination = Path.Combine(tempPath, "oversized-output");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DendenExportService().ExportAsync(
                CreateDocument(),
                new DendenExportOptions("書名", "著者", CoverImagePath: oversized),
                destination,
                CancellationToken.None));

        Assert.Contains("cover.png", error.Message, StringComparison.Ordinal);
        Assert.Contains("MB", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
        File.Delete(oversized);
        Assert.False(File.Exists(oversized));
    }

    [Fact]
    public async Task Denden_ordered_blocks_place_an_illustration_after_a_joined_paragraph()
    {
        Directory.CreateDirectory(tempPath);
        var image = Path.Combine(tempPath, "joined-illustration.png");
        WriteArtificialImage(image, new Scalar(12, 34, 56));
        var firstPage = Guid.NewGuid();
        var laterPage = Guid.NewGuid();
        var text = "前ページから次ページまで続く本文";
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline(text)],
            DocumentTextHash.Compute(text),
            [
                new SourceSpan(firstPage, "0001", 0, 6),
                new SourceSpan(laterPage, "0003", 6, text.Length - 6),
            ]);
        var draft = new StructuredDocument(Guid.NewGuid(), [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var illustration = new DendenIllustration(
            Guid.NewGuid(), 2, image, "挿絵 1", "場面");
        var export = new DendenExportDocument(
            document,
            [
                new DendenParagraphBlock(paragraph),
                new DendenIllustrationBlock(illustration, PlacementAdjusted: true),
            ]);
        var destination = Path.Combine(tempPath, "ordered-blocks");

        await new DendenExportService().ExportAsync(
            export,
            new DendenExportOptions("書名", "著者"),
            destination,
            CancellationToken.None);

        var markdown = await File.ReadAllTextAsync(Path.Combine(destination, "upload", "book.md"));
        var paragraphPosition = markdown.IndexOf(text, StringComparison.Ordinal);
        var imagePosition = markdown.IndexOf("![挿絵 1](illustration-001.png)", StringComparison.Ordinal);
        Assert.True(paragraphPosition >= 0 && imagePosition > paragraphPosition);
        Assert.Contains("場面", markdown, StringComparison.Ordinal);
        Assert.Single(export.Warnings);
        Assert.Equal("IllustrationPlacementAdjusted", export.Warnings[0].Code);
    }

    [Fact]
    public async Task Denden_uses_official_figure_markup_when_illustration_list_is_enabled()
    {
        Directory.CreateDirectory(tempPath);
        var image = Path.Combine(tempPath, "figure.png");
        WriteArtificialImage(image, new Scalar(12, 34, 56));
        var document = CreateDocument();
        var illustration = new DendenIllustration(
            Guid.NewGuid(), 1, image, "挿絵 1", "図1. 場面");
        var export = new DendenExportDocument(
            document,
            [
                new DendenParagraphBlock(document.Paragraphs[0]),
                new DendenIllustrationBlock(illustration),
            ]);
        var destination = Path.Combine(tempPath, "figure-list");

        await new DendenExportService().ExportAsync(
            export,
            new DendenExportOptions("書名", "著者", DisplayIllustrationList: true),
            destination,
            CancellationToken.None);

        var markdown = await File.ReadAllTextAsync(Path.Combine(destination, "upload", "book.md"));
        Assert.Contains("<figure class=\"illustration\">", markdown, StringComparison.Ordinal);
        Assert.Contains("<img src=\"illustration-001.png\" alt=\"挿絵 1\">", markdown, StringComparison.Ordinal);
        Assert.Contains("<figcaption>図1. 場面</figcaption>", markdown, StringComparison.Ordinal);
        Assert.Contains("</figure>", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denden_rejects_an_empty_split_export_before_creating_destination()
    {
        Directory.CreateDirectory(tempPath);
        var draft = new StructuredDocument(Guid.NewGuid(), [], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var destination = Path.Combine(tempPath, "empty-split");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DendenExportService().ExportAsync(
                document,
                new DendenExportOptions("書名", "著者", SplitByChapter: true),
                destination,
                CancellationToken.None));

        Assert.Contains("Markdown", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void Denden_inspection_reports_fatal_validation_without_throwing()
    {
        var draft = new StructuredDocument(Guid.NewGuid(), [], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var export = new DendenExportDocument(document, []);

        var issues = new DendenExportService().Inspect(
            export,
            new DendenExportOptions("書名", "著者", SplitByChapter: true));

        var issue = Assert.Single(issues);
        Assert.True(issue.IsFatal);
        Assert.Equal("DendenValidationFailed", issue.Code);
        Assert.Contains("Markdown", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Denden_assembler_uses_page_order_without_splitting_a_cross_page_paragraph()
    {
        var firstPage = Guid.NewGuid();
        var illustrationPage = Guid.NewGuid();
        var thirdPage = Guid.NewGuid();
        var text = "連結された本文";
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline(text)],
            DocumentTextHash.Compute(text),
            [
                new SourceSpan(firstPage, "0001", 0, 3),
                new SourceSpan(thirdPage, "0003", 3, text.Length - 3),
            ]);
        var draft = new StructuredDocument(Guid.NewGuid(), [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var illustration = new DendenIllustration(
            illustrationPage, 2, "illustration.png", "挿絵 1");

        var export = DendenDocumentAssembler.Assemble(
            document,
            new Dictionary<Guid, int>
            {
                [firstPage] = 1,
                [illustrationPage] = 2,
                [thirdPage] = 3,
            },
            [illustration]);

        Assert.Collection(
            export.Blocks,
            block => Assert.IsType<DendenParagraphBlock>(block),
            block => Assert.True(Assert.IsType<DendenIllustrationBlock>(block).PlacementAdjusted));
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

        Assert.False(File.Exists(Path.Combine(destination, "book.md")));
        var firstChapter = await File.ReadAllTextAsync(Path.Combine(destination, "upload", "chapter-001.md"));
        Assert.Contains("# 第一章", firstChapter, StringComparison.Ordinal);
        Assert.Contains("{八角|やすみ}と{八角|はっかく}", firstChapter, StringComparison.Ordinal);
        Assert.Contains("## 小見出し", firstChapter, StringComparison.Ordinal);
        Assert.Contains("## 2", firstChapter, StringComparison.Ordinal);
        Assert.Contains("***", firstChapter, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, "upload", "chapter-002.md")));
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

    private static void WriteArtificialImage(string path, Scalar color)
    {
        using var image = new Mat(8, 8, MatType.CV_8UC3, color);
        Assert.True(Cv2.ImWrite(path, image));
    }

    private static void WriteCoordinateImage(string path)
    {
        using var image = new Mat(6, 8, MatType.CV_8UC3);
        const int height = 6;
        const int width = 8;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image.Set(y, x, new Vec3b((byte)x, (byte)y, 0));
        Assert.True(Cv2.ImWrite(path, image));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempPath)) Directory.Delete(tempPath, recursive: true);
    }
}
