using System.IO.Compression;
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
        Assert.Contains("LowConfidence", await ReadEntryAsync(archive, "review-items.json"), StringComparison.Ordinal);
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

        Assert.Contains(preview.Issues, issue => issue.Code == "PageOrderChanged" && issue.IsError);
        Assert.Contains(preview.Issues, issue => issue.Code == "ExtremeTextLengthChange" && !issue.IsError);
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException(path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
