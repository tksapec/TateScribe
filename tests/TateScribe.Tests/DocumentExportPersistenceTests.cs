using DocumentFormat.OpenXml.Packaging;
using OpenCvSharp;
using TateScribe.App.Services;
using TateScribe.Core.Export;
using TateScribe.Core.Projects;
using TateScribe.Core.Ruby;
using TateScribe.Infrastructure.Export;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.Tests;

public sealed class DocumentExportPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Preparation_is_read_only_until_successful_output_persistence_and_deduplicates_snapshot()
    {
        var (repository, page, projectId) = await CreateProjectAsync();
        await using (repository)
        {
            var service = new DocumentExportService();
            var structured = await service.PrepareStructuredAsync(directory, [page], false, CancellationToken.None);
            var denden = await service.PrepareDendenAsync(directory, [page], false, CancellationToken.None);

            Assert.Null(structured.ExistingSnapshotId);
            Assert.Null(denden.ExistingSnapshotId);
            Assert.Null(await repository.FindDocumentSnapshotAsync(projectId, structured.Document.DocumentTextHash, CancellationToken.None));

            var first = await service.PersistAfterSuccessfulOutputAsync(directory, structured.Document, CancellationToken.None);
            var second = await service.PersistAfterSuccessfulOutputAsync(directory, structured.Document, CancellationToken.None);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public async Task Preflight_confirmed_count_matches_rendered_docx_ruby()
    {
        var (repository, page, projectId) = await CreateProjectAsync();
        await using (repository)
        {
            var service = new DocumentExportService();
            var draft = await service.PrepareStructuredAsync(directory, [page], false, CancellationToken.None);
            var snapshotId = await service.PersistAfterSuccessfulOutputAsync(directory, draft.Document, CancellationToken.None);
            var paragraph = Assert.Single(draft.Document.Paragraphs);
            var batchId = Guid.NewGuid();
            await repository.RecordRubyBatchAsync(batchId, projectId, snapshotId, RubyPolicy.PreserveOriginalOnly,
                [new RubyPackagePage(page.Id, "0001", page.SourcePath, null)], [], CancellationToken.None);
            await repository.SaveRubyImportAsync(snapshotId, RubyPolicy.PreserveOriginalOnly,
                new RubyImportDocument(1, projectId, batchId, draft.Document.DocumentTextHash,
                    [new RubyAnnotationProposal(paragraph.ParagraphId.ToString("D"), 0, 2, "本文", "ほんぶん",
                        RubySource.UserConfirmed, 1, [], "test", Guid.NewGuid(), RubyAnnotationStatus.Confirmed)], []),
                CancellationToken.None);

            var prepared = await service.PrepareStructuredAsync(directory, [page], false, CancellationToken.None);
            Assert.Equal(1, prepared.Preflight.ConfirmedRubyCount);
            var path = Path.Combine(directory, "rendered.docx");
            await new OpenXmlDocumentExporter().ExportAsync(
                prepared.Document, path, false, "游明朝", CancellationToken.None);
            using var word = WordprocessingDocument.Open(path, false);
            Assert.Equal(1, Count(word.MainDocumentPart!.Document.OuterXml, "<w:ruby>"));
        }
    }

    [Fact]
    public async Task Ruby_package_output_failure_leaves_no_snapshot_or_batch()
    {
        var (repository, page, projectId) = await CreateProjectAsync();
        await using (repository)
        {
            var draft = await new DocumentExportService().PrepareStructuredAsync(directory, [page], false, CancellationToken.None);
            var destination = Path.Combine(directory, "existing-package");
            Directory.CreateDirectory(destination);

            await Assert.ThrowsAsync<IOException>(() => new RubyWorkflowService().ExportPackageAsync(
                directory, [page], RubyPolicy.PreserveOriginalOnly, destination, CancellationToken.None));

            Assert.Null(await repository.FindDocumentSnapshotAsync(projectId, draft.Document.DocumentTextHash, CancellationToken.None));
            Assert.Empty(await repository.LoadRubyBatchHistoryAsync(CancellationToken.None));
        }
    }

    private async Task<(SqliteProjectRepository Repository, ProjectPage Page, Guid ProjectId)> CreateProjectAsync()
    {
        Directory.CreateDirectory(directory);
        var repository = await SqliteProjectRepository.CreateAsync(directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", Path.Combine(directory, "page.png"), "hash", 0, true, 0);
        byte[] png;
        using (var image = new Mat(4, 4, MatType.CV_8UC3, Scalar.White))
            Cv2.ImEncode(".png", image, out png);
        await File.WriteAllBytesAsync(page.SourcePath, png);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "本文", CancellationToken.None);
        return (repository, page, await repository.GetProjectIdAsync(CancellationToken.None));
    }

    private static int Count(string value, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }
        return count;
    }

    public void Dispose() => TestFileCleanup.DeleteDirectory(directory);
}
