using TateScribe.Core.Projects;
using TateScribe.Core.Ocr;
using TateScribe.Core.Images;
using TateScribe.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using TateScribe.Core.Layout;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Export;
using TateScribe.Core.Ruby;

namespace TateScribe.Tests;

public sealed class ProjectRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Incompatible_schema_7_migration_rolls_back_and_keeps_version_6()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "project.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_version (version INTEGER NOT NULL);
                INSERT INTO schema_version (version) VALUES (6);
                CREATE TABLE document_snapshots (id TEXT PRIMARY KEY NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() =>
            SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None));

        await using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await check.OpenAsync();
        var version = check.CreateCommand();
        version.CommandText = "SELECT version FROM schema_version;";
        Assert.Equal(6L, await version.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Schema_8_keeps_stable_paragraph_ids_and_marks_confirmed_ruby_stale_after_body_edit()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "one.png", Path.Combine(_directory, "one.png"), "hash", 0, true, 0);
        await File.WriteAllBytesAsync(page.SourcePath, [1]);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        const string logicalKey = "page-0001:0";
        var paragraphId = Guid.NewGuid();
        var paragraph = new StructuredParagraph(paragraphId, DocumentElementRole.BodyParagraph,
            [new TextInline("八角")], DocumentTextHash.Compute("八角"),
            [new SourceSpan(page.Id, "0001", 0, 2)], logicalKey);
        var draft = new StructuredDocument(projectId, [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.SaveDocumentSnapshotAsync(document, "Confirmed", CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordRubyBatchAsync(batchId, projectId, snapshotId, RubyPolicy.PreserveOriginalOnly,
            [new RubyPackagePage(page.Id, "0001", page.SourcePath, null)], [], CancellationToken.None);
        var annotation = new RubyAnnotationProposal(paragraphId.ToString("D"), 0, 2, "八角", "やすみ",
            RubySource.ImageConfirmed, 1, ["0001"], "画像", Guid.NewGuid(), RubyAnnotationStatus.Confirmed);
        var unresolved = new RubyUnresolvedItem(
            paragraphId.ToString("D"), 0, 2, "八角", ["0001"], "読みを断定できない");
        await repository.SaveRubyImportAsync(snapshotId, RubyPolicy.PreserveOriginalOnly,
            new RubyImportDocument(
                1, projectId, batchId, document.DocumentTextHash, [annotation], [unresolved]),
            CancellationToken.None);

        var loadedUnresolved = Assert.Single(await repository.LoadRubyUnresolvedItemsAsync(
            batchId, CancellationToken.None));
        Assert.Equal(unresolved.ParagraphId, loadedUnresolved.ParagraphId);
        Assert.Equal(unresolved.Start, loadedUnresolved.Start);
        Assert.Equal(unresolved.Length, loadedUnresolved.Length);
        Assert.Equal(unresolved.BaseText, loadedUnresolved.BaseText);
        Assert.Equal(unresolved.EvidencePageMarkers, loadedUnresolved.EvidencePageMarkers);
        Assert.Equal(unresolved.Reason, loadedUnresolved.Reason);

        await repository.SaveManualTextAsync(page.Id, "八角を修正", CancellationToken.None);

        Assert.Equal(8, await repository.GetSchemaVersionAsync(CancellationToken.None));
        var staleAnnotation = Assert.Single(await repository.LoadRubyAnnotationsAsync(batchId, CancellationToken.None));
        Assert.Equal(RubyAnnotationStatus.Stale, staleAnnotation.Status);
        Assert.Equal(1, await repository.GetRubyAnnotationHistoryCountAsync(
            staleAnnotation.AnnotationId, CancellationToken.None));
        await repository.UpdateRubyAnnotationsAsync([staleAnnotation], CancellationToken.None);
        Assert.Equal(1, await repository.GetRubyAnnotationHistoryCountAsync(
            staleAnnotation.AnnotationId, CancellationToken.None));
        Assert.True((await repository.LoadRubyBatchAsync(batchId, CancellationToken.None)).ConfirmedTextIsStale);
        Assert.Equal(paragraphId,
            await repository.FindStableParagraphIdAsync(projectId, logicalKey, CancellationToken.None));
    }

    [Fact]
    public async Task Page_structure_changes_stale_old_ruby_batches_and_create_a_distinct_snapshot_hash()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "one.png", Path.Combine(_directory, "one.png"), "hash", 0, true, 0);
        await File.WriteAllBytesAsync(page.SourcePath, [1]);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var paragraphId = Guid.NewGuid();
        var body = new StructuredParagraph(
            paragraphId,
            DocumentElementRole.BodyParagraph,
            [new TextInline("同じ本文")],
            DocumentTextHash.Compute("同じ本文"),
            [new SourceSpan(page.Id, "0001", 0, 4)],
            $"{page.Id:D}:0:{DocumentElementRole.BodyParagraph}");
        var bodyDraft = new StructuredDocument(projectId, [body], string.Empty);
        var bodyDocument = bodyDraft with { DocumentTextHash = DocumentTextHash.Compute(bodyDraft) };
        var bodySnapshot = await repository.SaveDocumentSnapshotAsync(
            bodyDocument, "Confirmed", CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordRubyBatchAsync(
            batchId,
            projectId,
            bodySnapshot,
            RubyPolicy.PreserveOriginalOnly,
            [new RubyPackagePage(page.Id, "0001", page.SourcePath, null)],
            [],
            CancellationToken.None);

        await repository.SavePagesAsync(
            [page with { PageRole = PageRole.ChapterTitle }],
            CancellationToken.None);

        Assert.True((await repository.LoadRubyBatchAsync(
            batchId, CancellationToken.None)).ConfirmedTextIsStale);
        var heading = body with
        {
            Role = DocumentElementRole.ChapterTitle,
            LogicalKey = $"{page.Id:D}:0:{DocumentElementRole.ChapterTitle}",
        };
        var headingDraft = new StructuredDocument(projectId, [heading], string.Empty);
        var headingDocument = headingDraft with
        {
            DocumentTextHash = DocumentTextHash.Compute(headingDraft),
        };
        var headingSnapshot = await repository.SaveDocumentSnapshotAsync(
            headingDocument, "Confirmed", CancellationToken.None);
        Assert.NotEqual(bodyDocument.DocumentTextHash, headingDocument.DocumentTextHash);
        Assert.NotEqual(bodySnapshot, headingSnapshot);
    }

    [Fact]
    public async Task Ruby_batch_page_markers_keep_the_original_page_ids_after_reordering()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(
            _directory, CancellationToken.None);
        var first = new ProjectPage(
            Guid.NewGuid(), "one.png", Path.Combine(_directory, "one.png"), "one", 0, true, 0);
        var second = new ProjectPage(
            Guid.NewGuid(), "two.png", Path.Combine(_directory, "two.png"), "two", 1, true, 0);
        await File.WriteAllBytesAsync(first.SourcePath, [1]);
        await File.WriteAllBytesAsync(second.SourcePath, [2]);
        await repository.SavePagesAsync([first, second], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline("本文")],
            DocumentTextHash.Compute("本文"),
            [new SourceSpan(first.Id, "0001", 0, 1), new SourceSpan(second.Id, "0002", 1, 1)],
            $"{first.Id:D}:0:{DocumentElementRole.BodyParagraph}");
        var draft = new StructuredDocument(projectId, [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.SaveDocumentSnapshotAsync(
            document, "Confirmed", CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordRubyBatchAsync(
            batchId,
            projectId,
            snapshotId,
            RubyPolicy.PreserveOriginalOnly,
            [
                new RubyPackagePage(first.Id, "0001", first.SourcePath, null),
                new RubyPackagePage(second.Id, "0002", second.SourcePath, null),
            ],
            [],
            CancellationToken.None);

        await repository.SavePagesAsync(
            [first with { SortOrder = 1 }, second with { SortOrder = 0 }],
            CancellationToken.None);

        var batch = await repository.LoadRubyBatchAsync(batchId, CancellationToken.None);
        Assert.Equal(first.Id, batch.PageIdsByMarker["0001"]);
        Assert.Equal(second.Id, batch.PageIdsByMarker["0002"]);
    }

    [Fact]
    public async Task Confirming_an_overlapping_ruby_stales_the_older_batch_annotation()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "one.png", Path.Combine(_directory, "one.png"), "hash", 0, true, 0);
        await File.WriteAllBytesAsync(page.SourcePath, [1]);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline("東京都")],
            DocumentTextHash.Compute("東京都"),
            [new SourceSpan(page.Id, "0001", 0, 3)],
            $"{page.Id:D}:0:{DocumentElementRole.BodyParagraph}");
        var draft = new StructuredDocument(projectId, [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.SaveDocumentSnapshotAsync(
            document, "Confirmed", CancellationToken.None);
        var firstBatch = Guid.NewGuid();
        var secondBatch = Guid.NewGuid();
        var packagePages = new[] { new RubyPackagePage(page.Id, "0001", page.SourcePath, null) };
        await repository.RecordRubyBatchAsync(
            firstBatch, projectId, snapshotId, RubyPolicy.OriginalAndTextConfirmed,
            packagePages, [], CancellationToken.None);
        await repository.RecordRubyBatchAsync(
            secondBatch, projectId, snapshotId, RubyPolicy.OriginalAndTextConfirmed,
            packagePages, [], CancellationToken.None);
        var first = new RubyAnnotationProposal(
            paragraph.ParagraphId.ToString("D"), 0, 2, "東京", "とうきょう",
            RubySource.TextConfirmed, 1, ["0001"], "本文", Guid.NewGuid(),
            RubyAnnotationStatus.Confirmed);
        var second = new RubyAnnotationProposal(
            paragraph.ParagraphId.ToString("D"), 1, 2, "京都", "きょうと",
            RubySource.TextConfirmed, 1, ["0001"], "本文", Guid.NewGuid(),
            RubyAnnotationStatus.Confirmed);
        await repository.SaveRubyImportAsync(
            snapshotId,
            RubyPolicy.OriginalAndTextConfirmed,
            new RubyImportDocument(
                1, projectId, firstBatch, document.DocumentTextHash, [first], []),
            CancellationToken.None);
        await repository.SaveRubyImportAsync(
            snapshotId,
            RubyPolicy.OriginalAndTextConfirmed,
            new RubyImportDocument(
                1, projectId, secondBatch, document.DocumentTextHash, [second], []),
            CancellationToken.None);
        Assert.Equal(
            RubyAnnotationStatus.Stale,
            Assert.Single(await repository.LoadRubyAnnotationsAsync(
                firstBatch, CancellationToken.None)).Status);
        Assert.Equal(
            RubyAnnotationStatus.Confirmed,
            Assert.Single(await repository.LoadRubyAnnotationsAsync(
                secondBatch, CancellationToken.None)).Status);
        var composed = await repository.LoadStructuredDocumentAsync(
            projectId, snapshotId, CancellationToken.None);
        Assert.Single(Assert.Single(composed.Paragraphs).Inlines.OfType<RubyInline>());
    }

    [Theory]
    [InlineData(RubyAnnotationStatus.Proposed, 1)]
    [InlineData(RubyAnnotationStatus.Confirmed, 0)]
    public async Task Reimporting_the_same_ruby_reuses_the_persisted_id_and_keeps_it_confirmed(
        RubyAnnotationStatus initialStatus,
        int expectedHistoryCount)
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(
            _directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "one.png", Path.Combine(_directory, "one.png"), "hash", 0, true, 0);
        await File.WriteAllBytesAsync(page.SourcePath, [1]);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline("東京")],
            DocumentTextHash.Compute("東京"),
            [new SourceSpan(page.Id, "0001", 0, 2)],
            $"{page.Id:D}:0:{DocumentElementRole.BodyParagraph}");
        var draft = new StructuredDocument(projectId, [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.SaveDocumentSnapshotAsync(
            document, "Confirmed", CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordRubyBatchAsync(
            batchId,
            projectId,
            snapshotId,
            RubyPolicy.OriginalAndTextConfirmed,
            [new RubyPackagePage(page.Id, "0001", page.SourcePath, null)],
            [],
            CancellationToken.None);
        RubyAnnotationProposal Annotation(Guid id, RubyAnnotationStatus status) => new(
            paragraph.ParagraphId.ToString("D"),
            0,
            2,
            "東京",
            "とうきょう",
            RubySource.TextConfirmed,
            1,
            ["0001"],
            "本文",
            id,
            status);
        var firstId = Guid.NewGuid();
        await repository.SaveRubyImportAsync(
            snapshotId,
            RubyPolicy.OriginalAndTextConfirmed,
            new RubyImportDocument(
                1,
                projectId,
                batchId,
                document.DocumentTextHash,
                [Annotation(firstId, initialStatus)],
                []),
            CancellationToken.None);

        await repository.SaveRubyImportAsync(
            snapshotId,
            RubyPolicy.OriginalAndTextConfirmed,
            new RubyImportDocument(
                1,
                projectId,
                batchId,
                document.DocumentTextHash,
                [Annotation(Guid.NewGuid(), RubyAnnotationStatus.Confirmed)],
                []),
            CancellationToken.None);

        var persisted = Assert.Single(await repository.LoadRubyAnnotationsAsync(
            batchId, CancellationToken.None));
        Assert.Equal(firstId, persisted.AnnotationId);
        Assert.Equal(RubyAnnotationStatus.Confirmed, persisted.Status);
        Assert.Equal(
            expectedHistoryCount,
            await repository.GetRubyAnnotationHistoryCountAsync(
                firstId, CancellationToken.None));
        var composed = await repository.LoadStructuredDocumentAsync(
            projectId, snapshotId, CancellationToken.None);
        Assert.Single(Assert.Single(composed.Paragraphs).Inlines.OfType<RubyInline>());
    }

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
    public async Task Restoring_a_different_selected_text_marks_an_existing_ruby_batch_stale()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(
            _directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "初版", CancellationToken.None);
        var first = Assert.Single(
            await repository.LoadPageTextVersionsAsync(page.Id, CancellationToken.None),
            version => version.Kind == "Manual");
        await repository.SaveManualTextAsync(page.Id, "改訂版", CancellationToken.None);
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline("改訂版")],
            DocumentTextHash.Compute("改訂版"),
            [new SourceSpan(page.Id, "0001", 0, 3)],
            $"{page.Id:D}:0:{DocumentElementRole.BodyParagraph}");
        var draft = new StructuredDocument(projectId, [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.SaveDocumentSnapshotAsync(
            document, "Manual", CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordRubyBatchAsync(
            batchId,
            projectId,
            snapshotId,
            RubyPolicy.PreserveOriginalOnly,
            [new RubyPackagePage(page.Id, "0001", page.SourcePath, null)],
            [],
            CancellationToken.None);

        await repository.RestoreTextVersionAsync(first, CancellationToken.None);

        Assert.True((await repository.LoadRubyBatchAsync(
            batchId, CancellationToken.None)).ConfirmedTextIsStale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ocr_word_review_stales_ruby_only_when_it_changes_the_selected_text(
        bool protectWithManualText)
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(
            _directory, CancellationToken.None);
        var page = new ProjectPage(
            Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var words = new[]
        {
            new OcrWord("本文", .95, 0, 0, 10, 10),
            new OcrWord("ルビ", .85, 11, 1, 14, 9),
        };
        await repository.SaveOcrAnalysisAsync(
            page.Id,
            new OcrPageResult("request", "paddle", "model", words),
            string.Empty,
            new OcrMergeProposal("本文", [], []),
            CancellationToken.None);
        if (protectWithManualText)
            await repository.SaveManualTextAsync(page.Id, "保護本文", CancellationToken.None);
        var selected = (await repository.LoadPageTextStateAsync(
            page.Id, CancellationToken.None)).SelectForProofreading().Text;
        var projectId = await repository.GetProjectIdAsync(CancellationToken.None);
        var paragraph = new StructuredParagraph(
            Guid.NewGuid(),
            DocumentElementRole.BodyParagraph,
            [new TextInline(selected)],
            DocumentTextHash.Compute(selected),
            [new SourceSpan(page.Id, "0001", 0, selected.Length)],
            $"{page.Id:D}:0:{DocumentElementRole.BodyParagraph}");
        var draft = new StructuredDocument(projectId, [paragraph], string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.SaveDocumentSnapshotAsync(
            document, "Selected", CancellationToken.None);
        var batchId = Guid.NewGuid();
        await repository.RecordRubyBatchAsync(
            batchId,
            projectId,
            snapshotId,
            RubyPolicy.PreserveOriginalOnly,
            [new RubyPackagePage(page.Id, "0001", page.SourcePath, null)],
            [],
            CancellationToken.None);
        var candidate = Assert.Single(
            await repository.LoadLatestOcrWordStatesAsync(page.Id, CancellationToken.None),
            word => word.AutomaticRole == "RubyCandidate");

        await repository.UpdateOcrWordReviewAsync(
            page.Id,
            candidate.RunId,
            candidate.Ordinal,
            "Body",
            true,
            CancellationToken.None);

        Assert.Equal(
            !protectWithManualText,
            (await repository.LoadRubyBatchAsync(
                batchId, CancellationToken.None)).ConfirmedTextIsStale);
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

        Assert.Equal(8, await repository.GetSchemaVersionAsync(CancellationToken.None));
        var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);
        Assert.Equal("手動", state.ManualText);
        Assert.Equal((1d, 2d, 3d, 4d), (state.MachineWords[0].Left, state.MachineWords[0].Top, state.MachineWords[0].Right, state.MachineWords[0].Bottom));
    }

    [Fact]
    public async Task Schema_8_migration_recomputes_the_automatic_role_for_a_returned_body_word()
    {
        Directory.CreateDirectory(_directory);
        var page = new ProjectPage(
            Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await using (var repository = await SqliteProjectRepository.CreateAsync(
            _directory, CancellationToken.None))
        {
            await repository.SavePagesAsync([page], CancellationToken.None);
            var words = new[]
            {
                new OcrWord("本文", .95, 0, 0, 10, 10),
                new OcrWord("ルビ", .85, 11, 1, 14, 9),
            };
            await repository.SaveOcrAnalysisAsync(
                page.Id,
                new OcrPageResult("request", "paddle", "model", words),
                string.Empty,
                new OcrMergeProposal("本文", [], []),
                CancellationToken.None);
            var initialReviewed = await repository.LoadLatestOcrWordStatesAsync(
                page.Id, CancellationToken.None);
            var candidate = Assert.Single(
                initialReviewed,
                word => word.AutomaticRole == "RubyCandidate");
            var body = Assert.Single(initialReviewed, word => word.AutomaticRole == "Body");
            await repository.UpdateOcrWordReviewAsync(
                page.Id,
                candidate.RunId,
                candidate.Ordinal,
                "Body",
                true,
                CancellationToken.None);
            await repository.UpdateOcrWordReviewAsync(
                page.Id,
                body.RunId,
                body.Ordinal,
                "Body",
                false,
                CancellationToken.None);
        }

        await using (var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_directory, "project.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            var downgrade = connection.CreateCommand();
            downgrade.CommandText = """
                UPDATE schema_version SET version = 7;
                UPDATE ocr_run_words SET automatic_role = 'Body';
                """;
            await downgrade.ExecuteNonQueryAsync();
        }

        await using var migrated = await SqliteProjectRepository.CreateAsync(
            _directory, CancellationToken.None);
        var reviewed = await migrated.LoadLatestOcrWordStatesAsync(
            page.Id, CancellationToken.None);
        var returned = Assert.Single(
            RubyOcrCandidateSelector.Select("0001", "本文", reviewed),
            candidate => candidate.ReturnedToBody);
        Assert.Equal("ルビ", returned.OcrText);
        Assert.Equal(8, await migrated.GetSchemaVersionAsync(CancellationToken.None));
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
