using System.IO.Compression;
using System.Text.Json;
using TateScribe.Core.Ocr;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Proofreading;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.Tests;

public sealed class ProofreadingPackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Export_writes_stable_page_files_manifest_and_provenance_markers()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var projectId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var output = Path.Combine(_directory, "proofreading.zip");
        var request = new ProofreadingPackageRequest(
            projectId,
            "Book",
            batchId,
            output,
            ProofreadingPackageFormat.Zip,
            [new ProofreadingPackagePage(Guid.NewGuid(), 0, "source.png", "source-hash", source, null, "下書き", "候補", 2, "Body", "ReflowVertical", [new ProofreadingReviewItem("LowConfidence", "確認", "語")])]);

        await new ProofreadingPackageExporter().ExportAsync(request, CancellationToken.None);

        using var archive = ZipFile.OpenRead(output);
        Assert.Contains(archive.Entries, entry => entry.FullName == "instructions.md");
        Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "ocr.txt");
        Assert.Contains(archive.Entries, entry => entry.FullName == "review-items.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "images-original/PAGE-0001.png");
        var ocr = await ReadEntryAsync(archive, "ocr.txt");
        Assert.Contains($"[[PROJECT_ID:{projectId:D}]]", ocr, StringComparison.Ordinal);
        Assert.Contains($"[[BATCH_ID:{batchId:D}]]", ocr, StringComparison.Ordinal);
        Assert.Contains("[[PAGE:0001]]", ocr, StringComparison.Ordinal);
        Assert.Contains("[[TEXT_BEGIN]]", ocr, StringComparison.Ordinal);
        Assert.Contains("[[TEXT_END]]", ocr, StringComparison.Ordinal);
        Assert.Contains("[[REPORT_BEGIN]]", ocr, StringComparison.Ordinal);
        var instructions = await ReadEntryAsync(archive, "instructions.md");
        Assert.Contains("TEXT_BEGIN", instructions, StringComparison.Ordinal);
        Assert.Contains("REPORT_BEGIN", instructions, StringComparison.Ordinal);
        Assert.Contains("LowConfidence", await ReadEntryAsync(archive, "review-items.json"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("確定", "手動", "提案", "原本", "確定", "Confirmed")]
    [InlineData(null, "手動", "提案", "原本", "手動", "Manual")]
    [InlineData(null, null, "提案", "原本", "提案", "Suggested")]
    [InlineData(null, null, null, "原本", "原本", "RawPaddle")]
    public async Task Export_selects_the_safest_available_text_and_records_its_source(
        string? confirmed, string? manual, string? suggested, string rawPaddle, string expected, string expectedSource)
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var output = Path.Combine(_directory, $"{Guid.NewGuid():N}.zip");
        var page = new ProofreadingPackagePage(
            Guid.NewGuid(), 0, "source.png", "hash", source, null, rawPaddle, suggested, 0,
            "Body", "ReflowVertical", ManualText: manual, ConfirmedText: confirmed);

        await new ProofreadingPackageExporter().ExportAsync(
            new ProofreadingPackageRequest(Guid.NewGuid(), "Book", Guid.NewGuid(), output, ProofreadingPackageFormat.Zip, [page]),
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(output);
        var ocr = await ReadEntryAsync(archive, "ocr.txt");
        Assert.Contains($"[[TEXT_BEGIN]]\n{expected}\n[[TEXT_END]]", ocr.Replace("\r\n", "\n"), StringComparison.Ordinal);
        using var manifest = JsonDocument.Parse(await ReadEntryAsync(archive, "manifest.json"));
        Assert.Equal(2, manifest.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(expectedSource, manifest.RootElement.GetProperty("pages")[0].GetProperty("textSource").GetString());
    }

    [Fact]
    public void Page_text_state_falls_back_to_reconstructed_raw_paddle_text()
    {
        var state = new PageTextState(Guid.NewGuid(), null, "paddle", "model",
            [new OcrWord("右", .9, .8, 0, .9, .1), new OcrWord("左", .9, .2, 0, .3, .1)]);

        var selected = state.SelectForProofreading();

        Assert.Equal("RawPaddle", selected.Source);
        Assert.Equal("右左", selected.Text);
    }

    [Fact]
    public void Parse_requires_matching_header_and_splits_confirmed_text_by_page()
    {
        var projectId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var text = $"""
            [[TATESCRIBE_FORMAT:1]]
            [[PROJECT_ID:{projectId:D}]]
            [[BATCH_ID:{batchId:D}]]

            [[PAGE:0001]]
            [[SOURCE_FILE:one.png]]
            [[PAGE_ROLE:Body]]
            [[DISPLAY_PROFILE:ReflowVertical]]

            第一ページ
            [[PAGE:0002]]
            [[SOURCE_FILE:two.png]]
            [[PAGE_ROLE:Body]]
            [[DISPLAY_PROFILE:ReflowVertical]]

            第二ページ
            """;

        var document = ProofreadingImportParser.Parse(text);

        Assert.Equal(projectId, document.ProjectId);
        Assert.Equal(batchId, document.BatchId);
        Assert.Collection(document.Pages,
            page => Assert.Equal(("0001", "第一ページ"), (page.PageMarker, page.ConfirmedText)),
            page => Assert.Equal(("0002", "第二ページ"), (page.PageMarker, page.ConfirmedText)));
    }

    [Fact]
    public void Parse_format_2_keeps_only_text_ranges_and_preserves_body_whitespace()
    {
        var projectId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var text = $"[[TATESCRIBE_FORMAT:2]]\n[[PROJECT_ID:{projectId:D}]]\n[[BATCH_ID:{batchId:D}]]\n\n" +
                   "[[PAGE:0001]]\n[[TEXT_BEGIN]]\n　段落\n\n末尾\n\n[[TEXT_END]]\n[[JOIN_TO_NEXT:ParagraphBreak]]\n\n" +
                   "[[REPORT_BEGIN]]\n判読不能: PAGE-0001\n主な修正一覧\n[[REPORT_END]]\n";

        var document = ProofreadingImportParser.Parse(text);

        var page = Assert.Single(document.Pages);
        Assert.Equal("　段落\n\n末尾\n", page.ConfirmedText);
        Assert.Equal(BoundaryJoinType.ParagraphBreak, page.JoinToNext);
        Assert.DoesNotContain("判読不能", page.ConfirmedText, StringComparison.Ordinal);
        Assert.Contains("主な修正一覧", document.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_format_2_safely_removes_a_single_outer_markdown_fence()
    {
        var projectId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var text = $"```text\n[[TATESCRIBE_FORMAT:2]]\n[[PROJECT_ID:{projectId:D}]]\n[[BATCH_ID:{batchId:D}]]\n" +
                   "[[PAGE:0001]]\n[[TEXT_BEGIN]]\n本文の[[注記]]\n[[TEXT_END]]\n" +
                   "[[JOIN_TO_NEXT:DirectJoin]]\n" +
                   "[[REPORT_BEGIN]]\nなし\n[[REPORT_END]]\n```\n";

        var document = ProofreadingImportParser.Parse(text);

        Assert.Equal("本文の[[注記]]", Assert.Single(document.Pages).ConfirmedText);
    }

    [Theory]
    [InlineData("[[TEXT_END]]", "TEXT_END")]
    [InlineData("[[REPORT_END]]", "REPORT_END")]
    public void Parse_format_2_rejects_missing_closing_markers(string markerToRemove, string expectedMessage)
    {
        var text = CreateFormat2Text().Replace(markerToRemove, string.Empty, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ProofreadingImportParser.Parse(text));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_format_2_rejects_unexpected_text_outside_blocks()
    {
        var text = CreateFormat2Text() + "\n取り込み先不明の文章";

        var error = Assert.Throws<InvalidDataException>(() => ProofreadingImportParser.Parse(text));

        Assert.Contains("outside", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_format_2_rejects_nested_or_duplicate_text_markers()
    {
        var text = CreateFormat2Text().Replace("[[TEXT_BEGIN]]", "[[TEXT_BEGIN]]\n[[TEXT_BEGIN]]", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => ProofreadingImportParser.Parse(text));
    }

    [Fact]
    public void Parse_format_2_rejects_a_missing_page_join_marker()
    {
        var text = CreateFormat2Text().Replace("[[JOIN_TO_NEXT:DirectJoin]]", string.Empty, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ProofreadingImportParser.Parse(text));

        Assert.Contains("JOIN_TO_NEXT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_format_2_rejects_a_join_marker_inside_the_text_block()
    {
        var text = CreateFormat2Text().Replace(
            "本文\n[[TEXT_END]]",
            "本文\n[[JOIN_TO_NEXT:DirectJoin]]\n[[TEXT_END]]",
            StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ProofreadingImportParser.Parse(text));

        Assert.Contains("JOIN_TO_NEXT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_2_export_and_import_round_trip()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "roundtrip.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var output = Path.Combine(_directory, "roundtrip.zip");
        var projectId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        await new ProofreadingPackageExporter().ExportAsync(
            new ProofreadingPackageRequest(projectId, "Book", batchId, output, ProofreadingPackageFormat.Zip,
            [new ProofreadingPackagePage(Guid.NewGuid(), 0, "roundtrip.png", "hash", source, null, "本文", null, 0,
                "Body", "ReflowVertical")]), CancellationToken.None);

        using var archive = ZipFile.OpenRead(output);
        var document = ProofreadingImportParser.Parse(await ReadEntryAsync(archive, "ocr.txt"));

        Assert.Equal(2, document.FormatVersion);
        Assert.Equal(projectId, document.ProjectId);
        Assert.Equal(batchId, document.BatchId);
        Assert.Equal("本文", Assert.Single(document.Pages).ConfirmedText);
    }

    [Fact]
    public async Task Export_preserves_the_original_image_extension_in_the_stable_package_name()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.jpg");
        await File.WriteAllBytesAsync(source, [0xFF, 0xD8, 0xFF]);
        var output = Path.Combine(_directory, "proofreading.zip");
        var request = new ProofreadingPackageRequest(Guid.NewGuid(), "Book", Guid.NewGuid(), output, ProofreadingPackageFormat.Zip,
            [new ProofreadingPackagePage(Guid.NewGuid(), 0, "source.jpg", "hash", source, null, "本文", null, 0, "Body", "ReflowVertical")]);

        await new ProofreadingPackageExporter().ExportAsync(request, CancellationToken.None);

        using var archive = ZipFile.OpenRead(output);
        Assert.Contains(archive.Entries, entry => entry.FullName == "images-original/PAGE-0001.jpg");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "images-original/PAGE-0001.png");
    }

    [Fact]
    public async Task Repository_validates_a_batch_before_saving_confirmed_text()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "one.png", "C:\\one.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);
        var document = new ProofreadingImportDocument(1, projectId, batchId, [new ProofreadingImportPage("0001", "確定本文")]);

        var preview = await repository.PrepareConfirmedImportAsync(document, CancellationToken.None);
        await repository.SaveConfirmedTextAsync(preview, new HashSet<string>(StringComparer.Ordinal) { "0001" }, CancellationToken.None);

        Assert.DoesNotContain(preview.Issues, issue => issue.IsError);
        Assert.Equal("確定本文", (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).ConfirmedText);
    }

    [Fact]
    public async Task Repository_saves_only_pages_selected_from_an_import_preview()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var first = new ProjectPage(Guid.NewGuid(), "one.png", "C:\\one.png", "one", 0, true, 0);
        var second = new ProjectPage(Guid.NewGuid(), "two.png", "C:\\two.png", "two", 1, true, 0);
        await repository.SavePagesAsync([first, second], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [first.Id, second.Id], CancellationToken.None);

        var preview = await repository.PrepareConfirmedImportAsync(new ProofreadingImportDocument(1, projectId, batchId,
            [new ProofreadingImportPage("0001", "first confirmed"), new ProofreadingImportPage("0002", "second confirmed")]), CancellationToken.None);
        await repository.SaveConfirmedTextAsync(preview, new HashSet<string>(StringComparer.Ordinal) { "0002" }, CancellationToken.None);

        Assert.Null((await repository.LoadPageTextStateAsync(first.Id, CancellationToken.None)).ConfirmedText);
        Assert.Equal("second confirmed", (await repository.LoadPageTextStateAsync(second.Id, CancellationToken.None)).ConfirmedText);
    }

    [Fact]
    public async Task Repository_flags_reordered_pages_and_extreme_text_changes()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var first = new ProjectPage(Guid.NewGuid(), "one.png", "C:\\one.png", "one", 0, true, 0);
        var second = new ProjectPage(Guid.NewGuid(), "two.png", "C:\\two.png", "two", 1, true, 0);
        await repository.SavePagesAsync([first, second], CancellationToken.None);
        await repository.ReplaceOcrWordsAsync(first.Id, "paddle", "model", [new TateScribe.Core.Ocr.OcrWord("短文", .9, 0, 0, 1, 1)], CancellationToken.None);
        await repository.ReplaceOcrWordsAsync(second.Id, "paddle", "model", [new TateScribe.Core.Ocr.OcrWord("短文", .9, 0, 0, 1, 1)], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [first.Id, second.Id], CancellationToken.None);

        var preview = await repository.PrepareConfirmedImportAsync(new ProofreadingImportDocument(1, projectId, batchId,
            [new ProofreadingImportPage("0002", new string('長', 100)), new ProofreadingImportPage("0001", "短文")]), CancellationToken.None);

        Assert.Contains(preview.Issues, issue => issue.Code == "PageOrderChanged" && !issue.IsError);
        Assert.Contains(preview.Issues, issue => issue.Code == "ExtremeTextLengthChange" && !issue.IsError);
    }

    [Fact]
    public async Task Repository_detects_stale_manual_text_and_changed_source_images()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "one.png", "C:\\one.png", "hash-before", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "出力時本文", CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);

        await repository.SaveManualTextAsync(page.Id, "更新後本文", CancellationToken.None);
        await repository.SavePagesAsync([page with { SourceHash = "hash-after" }], CancellationToken.None);
        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(2, projectId, batchId, [new ProofreadingImportPage("0001", "校正結果")]),
            CancellationToken.None);

        Assert.Contains(preview.Issues, issue => issue.Code == "BaselineTextChanged" && !issue.IsError);
        Assert.Contains(preview.Issues, issue => issue.Code == "SourceImageChanged" && issue.IsError);
    }

    [Fact]
    public async Task Repository_accepts_an_unchanged_export_snapshot()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "one.png", "C:\\one.png", "hash", 0, true, 90,
            new TateScribe.Core.Images.NormalizedCrop(.1, .2, .9, .8));
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "本文", CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);

        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(2, projectId, batchId, [new ProofreadingImportPage("0001", "校正本文")]),
            CancellationToken.None);

        Assert.DoesNotContain(preview.Issues, issue => issue.Code.EndsWith("Changed", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Issues, issue => issue.IsError);
    }

    [Fact]
    public async Task Repository_saves_accepted_clean_pages_while_blocking_an_error_page()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var first = new ProjectPage(Guid.NewGuid(), "one.png", "C:\\one.png", "one-before", 0, true, 0);
        var second = new ProjectPage(Guid.NewGuid(), "two.png", "C:\\two.png", "two", 1, true, 0);
        await repository.SavePagesAsync([first, second], CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [first.Id, second.Id], CancellationToken.None);
        await repository.SavePagesAsync([first with { SourceHash = "one-after" }, second], CancellationToken.None);
        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(
                2, await repository.GetProjectIdAsync(CancellationToken.None), batchId,
                [new ProofreadingImportPage("0001", "危険"), new ProofreadingImportPage("0002", "安全")]),
            CancellationToken.None);

        await repository.SaveConfirmedTextAsync(
            preview, new HashSet<string>(StringComparer.Ordinal) { "0002" }, CancellationToken.None);

        Assert.Null((await repository.LoadPageTextStateAsync(first.Id, CancellationToken.None)).ConfirmedText);
        Assert.Equal("安全", (await repository.LoadPageTextStateAsync(second.Id, CancellationToken.None)).ConfirmedText);
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException(path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string CreateFormat2Text()
    {
        var projectId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        return $"[[TATESCRIBE_FORMAT:2]]\n[[PROJECT_ID:{projectId:D}]]\n[[BATCH_ID:{batchId:D}]]\n" +
               "[[PAGE:0001]]\n[[TEXT_BEGIN]]\n本文\n[[TEXT_END]]\n" +
               "[[JOIN_TO_NEXT:DirectJoin]]\n" +
               "[[REPORT_BEGIN]]\nなし\n[[REPORT_END]]";
    }

    public void Dispose()
    {
        TestFileCleanup.DeleteDirectory(_directory);
    }
}
