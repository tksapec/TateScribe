using TateScribe.Core.Projects;
using TateScribe.Core.Ocr;
using TateScribe.Core.Images;
using TateScribe.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using TateScribe.Core.Layout;
using TateScribe.Core.Proofreading;

namespace TateScribe.Tests;

public sealed class ProjectRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_and_load_preserves_manual_page_order_and_source_hash()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var pages = new[]
        {
            new ProjectPage(Guid.NewGuid(), "b.png", "C:\\images\\b.png", "hash-b", 1, true, 90, new NormalizedCrop(0, 0.1, 1, 0.9)),
            new ProjectPage(Guid.NewGuid(), "a.png", "C:\\images\\a.png", "hash-a", 0, true, 0)
        };

        await repository.SavePagesAsync(pages, CancellationToken.None);
        var loaded = await repository.LoadPagesAsync(CancellationToken.None);

        Assert.Equal(["a.png", "b.png"], loaded.Select(x => x.FileName));
        Assert.Equal("hash-b", loaded[1].SourceHash);
        Assert.Equal(90, loaded[1].RotationDegrees);
        Assert.Equal(new NormalizedCrop(0, 0.1, 1, 0.9), loaded[1].Crop);
    }

    [Fact]
    public async Task Saving_reordered_pages_preserves_existing_ocr_and_manual_text()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var first = new ProjectPage(Guid.NewGuid(), "first.png", "C:\\images\\first.png", "hash-first", 0, true, 0);
        var second = new ProjectPage(Guid.NewGuid(), "second.png", "C:\\images\\second.png", "hash-second", 1, true, 0);

        await repository.SavePagesAsync([first, second], CancellationToken.None);
        await repository.ReplaceOcrWordsAsync(first.Id, "paddle", "model-a", [new OcrWord("recognized", 0.9, 1, 2, 3, 4)], CancellationToken.None);
        await repository.SaveManualTextAsync(first.Id, "corrected", CancellationToken.None);

        await repository.SavePagesAsync(
        [
            second with { SortOrder = 0 },
            first with { SortOrder = 1, IsIncluded = false }
        ], CancellationToken.None);
        var state = await repository.LoadPageTextStateAsync(first.Id, CancellationToken.None);
        var pages = await repository.LoadPagesAsync(CancellationToken.None);

        Assert.Equal("corrected", state.ManualText);
        Assert.Equal("recognized", Assert.Single(state.MachineWords).Text);
        Assert.Equal(["second.png", "first.png"], pages.Select(page => page.FileName));
        Assert.False(pages[1].IsIncluded);
    }

    [Fact]
    public async Task Opening_an_old_project_schema_adds_crop_columns()
    {
        Directory.CreateDirectory(_directory);
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "project.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE pages (id TEXT PRIMARY KEY NOT NULL, file_name TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, sort_order INTEGER NOT NULL, included INTEGER NOT NULL, rotation_degrees INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        await using (var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None))
        {
            var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0, new NormalizedCrop(0, .1, 1, .9));
            await repository.SavePagesAsync([page], CancellationToken.None);

            Assert.Equal(page.Crop, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).Crop);
        }
    }

    [Fact]
    public async Task Save_and_load_persists_profile_role_and_printed_page_number()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0,
            new NormalizedCrop(.1, .2, .9, .8), DisplayProfile.FixedPageVertical,
            PageRole.MixedTitleAndBody, "42", ProofreadingStatus.ReviewRequired);

        await repository.SavePagesAsync([page], CancellationToken.None);
        var loaded = Assert.Single(await repository.LoadPagesAsync(CancellationToken.None));

        Assert.Equal(DisplayProfile.FixedPageVertical, loaded.DisplayProfile);
        Assert.Equal(PageRole.MixedTitleAndBody, loaded.PageRole);
        Assert.Equal("42", loaded.PrintedPageNumber);
        Assert.Equal(ProofreadingStatus.ReviewRequired, loaded.ProofreadingStatus);
    }

    [Fact]
    public async Task Opening_a_legacy_merged_ocr_moves_it_to_an_unknown_coordinate_suggestion()
    {
        Directory.CreateDirectory(_directory);
        var pageId = Guid.NewGuid();
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "project.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE pages (id TEXT PRIMARY KEY NOT NULL, file_name TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, sort_order INTEGER NOT NULL, included INTEGER NOT NULL, rotation_degrees INTEGER NOT NULL);
                CREATE TABLE ocr_words (page_id TEXT NOT NULL, ordinal INTEGER NOT NULL, engine TEXT NOT NULL, model_version TEXT NOT NULL, text TEXT NOT NULL, confidence REAL NOT NULL, left_x REAL NOT NULL, top_y REAL NOT NULL, right_x REAL NOT NULL, bottom_y REAL NOT NULL, PRIMARY KEY (page_id, ordinal));
                INSERT INTO pages VALUES ('{pageId:D}', 'page.png', 'C:\\page.png', 'hash', 0, 1, 0);
                INSERT INTO ocr_words VALUES ('{pageId:D}', 0, 'paddle+tesseract', 'legacy', '統合本文', .8, 0, 0, 1, 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var state = await repository.LoadPageTextStateAsync(pageId, CancellationToken.None);

        Assert.Empty(state.RawPaddleWords);
        Assert.False(state.RawPaddleCoordinatesKnown);
        Assert.Equal("統合本文", state.LegacyMergedText);
        Assert.Equal("統合本文", state.SuggestedText);
    }

    [Fact]
    public async Task Manual_text_saves_append_history_without_consecutive_duplicates()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);

        await repository.SaveManualTextAsync(page.Id, "初版", CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "初版", CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "第二版", CancellationToken.None);

        var versions = await repository.LoadPageTextVersionsAsync(page.Id, CancellationToken.None);
        Assert.Equal(["第二版", "初版"], versions.Where(version => version.Kind == "Manual").Select(version => version.Text));
        Assert.All(versions.Where(version => version.Kind == "Manual"), version => Assert.Equal("ManualEdit", version.Source));
    }

    [Fact]
    public async Task Manual_save_and_manual_history_restore_after_confirmation_become_the_active_text()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "手動初版", CancellationToken.None);
        var firstManual = Assert.Single(
            await repository.LoadPageTextVersionsAsync(page.Id, CancellationToken.None),
            version => version.Kind == "Manual");
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);
        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(1, projectId, batchId, [new ProofreadingImportPage("0001", "確定本文")]),
            CancellationToken.None);
        await repository.SaveConfirmedTextAsync(
            preview, new HashSet<string>(StringComparer.Ordinal) { "0001" }, CancellationToken.None);

        await repository.SaveManualTextAsync(page.Id, "確定後の手動修正", CancellationToken.None);

        var selectedAfterEdit = (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).SelectForProofreading();
        Assert.Equal(("確定後の手動修正", "Manual"), (selectedAfterEdit.Text, selectedAfterEdit.Source));

        await repository.RestoreTextVersionAsync(firstManual, CancellationToken.None);

        var selectedAfterRestore = (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).SelectForProofreading();
        Assert.Equal(("手動初版", "Manual"), (selectedAfterRestore.Text, selectedAfterRestore.Source));
    }

    [Fact]
    public async Task Restoring_a_confirmed_version_after_a_manual_edit_reactivates_confirmation()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);
        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(1, projectId, batchId, [new ProofreadingImportPage("0001", "確定版C")]),
            CancellationToken.None);
        await repository.SaveConfirmedTextAsync(
            preview, new HashSet<string>(StringComparer.Ordinal) { "0001" }, CancellationToken.None);
        var confirmed = Assert.Single(
            await repository.LoadPageTextVersionsAsync(page.Id, CancellationToken.None),
            version => version.Kind == "Confirmed");
        await repository.SaveManualTextAsync(page.Id, "手動版M", CancellationToken.None);

        await repository.RestoreTextVersionAsync(confirmed, CancellationToken.None);

        var selected = (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).SelectForProofreading();
        Assert.Equal(("確定版C", "Confirmed"), (selected.Text, selected.Source));
    }

    [Fact]
    public async Task Reocr_preserves_confirmed_text_and_marks_its_proofreading_state_stale()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);
        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(1, projectId, batchId, [new ProofreadingImportPage("0001", "校正済み")]),
            CancellationToken.None);
        await repository.SaveConfirmedTextAsync(preview, new HashSet<string>(StringComparer.Ordinal) { "0001" }, CancellationToken.None);

        var paddle = new OcrPageResult("request", "paddle", "model", [new OcrWord("再OCR", .9, 0, 0, 1, 1)]);
        await repository.SaveOcrAnalysisAsync(page.Id, paddle, "再OCR", new OcrMergeProposal("再OCR", [], []), CancellationToken.None);

        Assert.Equal("校正済み", (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).ConfirmedText);
        var reloaded = Assert.Single(await repository.LoadPagesAsync(CancellationToken.None));
        Assert.Equal(ProofreadingStatus.Stale, reloaded.ProofreadingStatus);
        Assert.Equal(OcrStatus.Completed, reloaded.OcrStatus);
    }

    [Fact]
    public async Task Reocr_preserves_manual_text_and_manual_proofreading_state()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "手動本文", CancellationToken.None);

        var paddle = new OcrPageResult("request", "paddle", "model", [new OcrWord("再OCR", .9, 0, 0, 1, 1)]);
        await repository.SaveOcrAnalysisAsync(page.Id, paddle, "再OCR", new OcrMergeProposal("再OCR", [], []), CancellationToken.None);

        Assert.Equal("手動本文", (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).ManualText);
        Assert.Equal(ProofreadingStatus.Stale, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).ProofreadingStatus);
    }

    [Fact]
    public async Task Opening_a_project_sets_the_current_schema_version_without_losing_existing_text_or_coordinates()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.ReplaceOcrWordsAsync(page.Id, "paddle", "model", [new OcrWord("字", .9, 1, 2, 3, 4)], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "手動", CancellationToken.None);

        Assert.Equal(6, await repository.GetSchemaVersionAsync(CancellationToken.None));
        var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);
        Assert.Equal("手動", state.ManualText);
        Assert.Equal((1d, 2d, 3d, 4d), (state.MachineWords[0].Left, state.MachineWords[0].Top, state.MachineWords[0].Right, state.MachineWords[0].Bottom));
    }

    [Fact]
    public async Task Ocr_failure_details_are_persisted_without_body_text()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var failure = new OcrFailure(
            Guid.NewGuid(), page.Id, page.FileName, OcrFailureStage.PaddleOCR,
            "ModelInitializationException", "モデルを初期化できません", true, false, DateTimeOffset.UtcNow);

        await repository.RecordOcrFailureAsync(failure, CancellationToken.None);

        var loaded = Assert.Single(await repository.LoadOcrFailuresAsync(page.Id, CancellationToken.None));
        Assert.Equal(failure with { OccurredAt = loaded.OccurredAt }, loaded);
        Assert.Equal(OcrStatus.Failed, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).OcrStatus);
    }

    [Fact]
    public async Task Cancelled_reocr_preserves_the_status_of_existing_successful_ocr()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var paddle = new OcrPageResult("request", "paddle", "model", [new OcrWord("本文", .99, 0, 0, 10, 10)]);
        await repository.SaveOcrAnalysisAsync(
            page.Id, paddle, string.Empty, new OcrMergeProposal("本文", [], []), CancellationToken.None);
        await repository.SetOcrStatusAsync(page.Id, OcrStatus.Processing, CancellationToken.None);
        var cancelled = new OcrFailure(
            Guid.NewGuid(), page.Id, page.FileName, OcrFailureStage.PaddleOCR,
            nameof(OperationCanceledException), "取り消しました", true, true, DateTimeOffset.UtcNow);

        await repository.RecordOcrFailureAsync(cancelled, CancellationToken.None);

        Assert.Equal(OcrStatus.Completed, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).OcrStatus);
    }

    [Fact]
    public async Task Page_validation_issues_are_persisted_as_review_items()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var issue = new PageValidationIssue(page.Id, "PrintedPageGap", "欠落候補");

        await repository.ReplacePageValidationIssuesAsync([issue], CancellationToken.None);

        var stored = Assert.Single(await repository.LoadReviewItemsAsync(page.Id, CancellationToken.None));
        Assert.Equal(("PrintedPageGap", "欠落候補", "PageValidation"), (stored.Code, stored.Message, stored.Source));
    }

    [Fact]
    public async Task Ruby_candidate_manual_classification_survives_redisplay_and_reocr()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var body = new OcrWord("本文", .99, 100, 10, 120, 50);
        var ruby = new OcrWord("ほんぶん", .99, 88, 12, 96, 38);
        var result = new OcrPageResult("request", "paddle", "model", [body, ruby]);
        await repository.SaveOcrAnalysisAsync(page.Id, result, string.Empty, new OcrMergeProposal("本文", [], []), CancellationToken.None);
        Assert.Equal(OcrStatus.ReviewRequired, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).OcrStatus);
        var candidate = Assert.Single(
            await repository.LoadLatestOcrWordStatesAsync(page.Id, CancellationToken.None),
            word => word.Role == "RubyCandidate");

        await repository.UpdateOcrWordReviewAsync(page.Id, candidate.RunId, candidate.Ordinal, "Body", true, CancellationToken.None);
        Assert.Equal(OcrStatus.Completed, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).OcrStatus);
        await repository.SaveOcrAnalysisAsync(page.Id, result with { RequestId = "request-2" }, string.Empty, new OcrMergeProposal("本文", [], []), CancellationToken.None);

        var latest = Assert.Single(
            await repository.LoadLatestOcrWordStatesAsync(page.Id, CancellationToken.None),
            word => word.Word.Text == "ほんぶん");
        Assert.Equal("Body", latest.Role);
        Assert.True(latest.IncludedInDraft);
        Assert.True(latest.IsManualOverride);
        Assert.Equal(OcrStatus.Completed, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).OcrStatus);
        Assert.Contains("ほんぶん", (await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None)).SuggestedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmed_import_persists_the_page_boundary_join_type()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordProofreadingExportAsync(batchId, [page.Id], CancellationToken.None);
        var preview = await repository.PrepareConfirmedImportAsync(
            new ProofreadingImportDocument(
                2, await repository.GetProjectIdAsync(CancellationToken.None), batchId,
                [new ProofreadingImportPage("0001", "本文", BoundaryJoinType.ParagraphBreak)]),
            CancellationToken.None);

        await repository.SaveConfirmedTextAsync(
            preview, new HashSet<string>(StringComparer.Ordinal) { "0001" }, CancellationToken.None);

        Assert.Equal(BoundaryJoinType.ParagraphBreak, Assert.Single(await repository.LoadPagesAsync(CancellationToken.None)).BoundaryJoinType);
    }

    [Fact]
    public async Task Saving_page_metadata_does_not_overwrite_repository_managed_status_or_boundary()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0,
            ProofreadingStatus: ProofreadingStatus.Confirmed,
            OcrStatus: OcrStatus.Completed,
            BoundaryJoinType: BoundaryJoinType.ParagraphBreak);
        await repository.SavePagesAsync([page], CancellationToken.None);

        await repository.SavePagesAsync([
            page with
            {
                PageRole = PageRole.Other,
                ProofreadingStatus = ProofreadingStatus.Draft,
                OcrStatus = OcrStatus.NotProcessed,
                BoundaryJoinType = BoundaryJoinType.DirectJoin
            }
        ], CancellationToken.None);

        var loaded = Assert.Single(await repository.LoadPagesAsync(CancellationToken.None));
        Assert.Equal(PageRole.Other, loaded.PageRole);
        Assert.Equal(ProofreadingStatus.Confirmed, loaded.ProofreadingStatus);
        Assert.Equal(OcrStatus.Completed, loaded.OcrStatus);
        Assert.Equal(BoundaryJoinType.ParagraphBreak, loaded.BoundaryJoinType);
    }

    public void Dispose()
    {
        TestFileCleanup.DeleteDirectory(_directory);
    }
}
