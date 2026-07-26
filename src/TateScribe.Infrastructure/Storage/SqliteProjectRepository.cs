using Microsoft.Data.Sqlite;
using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Core.Images;
using TateScribe.Core.Layout;
using TateScribe.Core.Proofreading;
using System.Security.Cryptography;
using System.Text;

namespace TateScribe.Infrastructure.Storage;

public sealed class SqliteProjectRepository : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteProjectRepository(SqliteConnection connection) => _connection = connection;

    public static async Task<SqliteProjectRepository> CreateAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(projectDirectory, "project.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        var repository = new SqliteProjectRepository(connection);
        await repository.InitializeAsync(cancellationToken);
        return repository;
    }

    public async Task SavePagesAsync(IReadOnlyList<ProjectPage> pages, CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        foreach (var page in pages)
        {
            var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pages (id, file_name, source_path, source_hash, sort_order, included, rotation_degrees, crop_left, crop_top, crop_right, crop_bottom, display_profile, page_role, printed_page_number, proofreading_status, review_item_count, ocr_status, boundary_join_type)
                VALUES ($id, $name, $path, $hash, $order, $included, $rotation, $left, $top, $right, $bottom, $profile, $role, $printedPage, $status, $reviewCount, $ocrStatus, $joinType)
                ON CONFLICT(id) DO UPDATE SET
                    file_name = excluded.file_name,
                    source_path = excluded.source_path,
                    source_hash = excluded.source_hash,
                    sort_order = excluded.sort_order,
                    included = excluded.included,
                    rotation_degrees = excluded.rotation_degrees, crop_left = excluded.crop_left, crop_top = excluded.crop_top, crop_right = excluded.crop_right, crop_bottom = excluded.crop_bottom,
                    display_profile = excluded.display_profile, page_role = excluded.page_role, printed_page_number = excluded.printed_page_number;
                """;
            command.Parameters.AddWithValue("$id", page.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", page.FileName);
            command.Parameters.AddWithValue("$path", page.SourcePath);
            command.Parameters.AddWithValue("$hash", page.SourceHash);
            command.Parameters.AddWithValue("$order", page.SortOrder);
            command.Parameters.AddWithValue("$included", page.IsIncluded ? 1 : 0);
            command.Parameters.AddWithValue("$rotation", page.RotationDegrees);
            var crop = page.Crop ?? NormalizedCrop.Full;
            command.Parameters.AddWithValue("$left", crop.Left); command.Parameters.AddWithValue("$top", crop.Top); command.Parameters.AddWithValue("$right", crop.Right); command.Parameters.AddWithValue("$bottom", crop.Bottom);
            command.Parameters.AddWithValue("$profile", page.DisplayProfile.ToString());
            command.Parameters.AddWithValue("$role", page.PageRole.ToString());
            command.Parameters.AddWithValue("$printedPage", (object?)page.PrintedPageNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("$status", page.ProofreadingStatus.ToString());
            command.Parameters.AddWithValue("$reviewCount", page.ReviewItemCount);
            command.Parameters.AddWithValue("$ocrStatus", page.OcrStatus.ToString());
            command.Parameters.AddWithValue("$joinType", page.BoundaryJoinType.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPage>> LoadPagesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, file_name, source_path, source_hash, sort_order, included, rotation_degrees, crop_left, crop_top, crop_right, crop_bottom, display_profile, page_role, printed_page_number, proofreading_status, review_item_count, ocr_status, boundary_join_type FROM pages ORDER BY sort_order;";
        var result = new List<ProjectPage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectPage(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) != 0, reader.GetInt32(6),
                new NormalizedCrop(reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9), reader.GetDouble(10)),
                Enum.Parse<DisplayProfile>(reader.GetString(11)), Enum.Parse<PageRole>(reader.GetString(12)), reader.IsDBNull(13) ? null : reader.GetString(13),
                ParseProofreadingStatus(reader.GetString(14)), reader.GetInt32(15), Enum.Parse<OcrStatus>(reader.GetString(16)),
                Enum.Parse<BoundaryJoinType>(reader.GetString(17))));
        }
        return result;
    }

    public async Task ReplaceOcrWordsAsync(Guid pageId, string engine, string modelVersion, IReadOnlyList<OcrWord> words, CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        await ReplaceOcrWordsAsync(transaction, pageId, engine, modelVersion, words, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReplaceOcrWordsAsync(SqliteTransaction transaction, Guid pageId, string engine, string modelVersion, IReadOnlyList<OcrWord> words, CancellationToken cancellationToken)
    {
        var clear = _connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM ocr_words WHERE page_id = $pageId;";
        clear.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await clear.ExecuteNonQueryAsync(cancellationToken);
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO ocr_words (page_id, ordinal, engine, model_version, text, confidence, left_x, top_y, right_x, bottom_y) VALUES ($pageId, $ordinal, $engine, $model, $text, $confidence, $left, $top, $right, $bottom);";
            insert.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
            insert.Parameters.AddWithValue("$ordinal", index);
            insert.Parameters.AddWithValue("$engine", engine);
            insert.Parameters.AddWithValue("$model", modelVersion);
            insert.Parameters.AddWithValue("$text", word.Text);
            insert.Parameters.AddWithValue("$confidence", word.Confidence);
            insert.Parameters.AddWithValue("$left", word.Left);
            insert.Parameters.AddWithValue("$top", word.Top);
            insert.Parameters.AddWithValue("$right", word.Right);
            insert.Parameters.AddWithValue("$bottom", word.Bottom);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task SaveManualTextAsync(Guid pageId, string text, CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO manual_page_text (page_id, text, updated_utc) VALUES ($pageId, $text, $utc) ON CONFLICT(page_id) DO UPDATE SET text = excluded.text, updated_utc = excluded.updated_utc;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AppendTextVersionIfChangedAsync(transaction, pageId, "Manual", text, "ManualEdit", null, cancellationToken);
        await UpdateProofreadingStatusAsync(transaction, pageId, ProofreadingStatus.ManuallyEdited, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PageTextVersion>> LoadPageTextVersionsAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT kind, text, created_utc, source FROM page_text_versions WHERE page_id = $pageId ORDER BY rowid DESC;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var versions = new List<PageTextVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            versions.Add(new PageTextVersion(pageId, reader.GetString(0), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture), reader.GetString(3)));
        return versions;
    }

    public async Task RestoreTextVersionAsync(
        PageTextVersion version,
        CancellationToken cancellationToken)
    {
        var latestOcrRunId = version.Kind == "Confirmed"
            ? await GetLatestPaddleRunIdAsync(version.PageId, cancellationToken)
            : null;
        await using var transaction = _connection.BeginTransaction();
        if (version.Kind == "Manual")
        {
            var current = _connection.CreateCommand();
            current.Transaction = transaction;
            current.CommandText = """
                INSERT INTO manual_page_text (page_id, text, updated_utc)
                VALUES ($pageId, $text, $utc)
                ON CONFLICT(page_id) DO UPDATE SET text = excluded.text, updated_utc = excluded.updated_utc;
                """;
            current.Parameters.AddWithValue("$pageId", version.PageId.ToString("D"));
            current.Parameters.AddWithValue("$text", version.Text);
            current.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await current.ExecuteNonQueryAsync(cancellationToken);
            await AppendTextVersionIfChangedAsync(
                transaction, version.PageId, "Manual", version.Text, "HistoryRestore", null, cancellationToken);
            await UpdateProofreadingStatusAsync(transaction, version.PageId, ProofreadingStatus.ManuallyEdited, cancellationToken);
        }
        else if (version.Kind == "Confirmed")
        {
            await AppendTextVersionIfChangedAsync(
                transaction, version.PageId, "Confirmed", version.Text, "HistoryRestore",
                latestOcrRunId, cancellationToken);
            await UpdateProofreadingStatusAsync(transaction, version.PageId, ProofreadingStatus.Confirmed, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported text version kind: {version.Kind}");
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<Guid> GetProjectIdAsync(CancellationToken cancellationToken)
    {
        var select = _connection.CreateCommand();
        select.CommandText = "SELECT value FROM project_metadata WHERE key = 'project_id';";
        var existing = await select.ExecuteScalarAsync(cancellationToken) as string;
        if (Guid.TryParse(existing, out var projectId)) return projectId;

        projectId = Guid.NewGuid();
        var insert = _connection.CreateCommand();
        insert.CommandText = "INSERT INTO project_metadata (key, value) VALUES ('project_id', $value) ON CONFLICT(key) DO NOTHING;";
        insert.Parameters.AddWithValue("$value", projectId.ToString("D"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        var persisted = await select.ExecuteScalarAsync(cancellationToken) as string;
        return Guid.TryParse(persisted, out projectId) ? projectId : throw new InvalidDataException("Project identifier could not be persisted.");
    }

    public async Task RecordProofreadingExportAsync(Guid batchId, IReadOnlyList<Guid> pageIds, CancellationToken cancellationToken)
    {
        var projectId = await GetProjectIdAsync(cancellationToken);
        var pagesById = (await LoadPagesAsync(cancellationToken)).ToDictionary(page => page.Id);
        var snapshots = new List<ProofreadingExportSnapshot>();
        foreach (var pageId in pageIds)
        {
            if (!pagesById.TryGetValue(pageId, out var projectPage))
                throw new InvalidOperationException($"Cannot export missing page {pageId:D}.");
            var state = await LoadPageTextStateAsync(pageId, cancellationToken);
            var selected = state.SelectForProofreading();
            snapshots.Add(new ProofreadingExportSnapshot(
                projectPage, HashText(selected.Text), selected.Source,
                await GetLatestPaddleRunIdAsync(pageId, cancellationToken)));
        }

        await using var transaction = _connection.BeginTransaction();
        var export = _connection.CreateCommand();
        export.Transaction = transaction;
        export.CommandText = "INSERT INTO proofreading_exports (batch_id, project_id, exported_utc) VALUES ($batch, $project, $utc);";
        export.Parameters.AddWithValue("$batch", batchId.ToString("D"));
        export.Parameters.AddWithValue("$project", projectId.ToString("D"));
        export.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await export.ExecuteNonQueryAsync(cancellationToken);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var crop = snapshot.Page.Crop ?? NormalizedCrop.Full;
            var page = _connection.CreateCommand();
            page.Transaction = transaction;
            page.CommandText = """
                INSERT INTO proofreading_export_pages (
                    batch_id, page_id, page_marker, source_hash, baseline_text_hash, text_source,
                    crop_left, crop_top, crop_right, crop_bottom, rotation_degrees, page_role,
                    display_profile, sort_order, ocr_run_id)
                VALUES (
                    $batch, $page, $marker, $sourceHash, $textHash, $textSource,
                    $left, $top, $right, $bottom, $rotation, $role, $profile, $sortOrder, $ocrRun);
                """;
            page.Parameters.AddWithValue("$batch", batchId.ToString("D"));
            page.Parameters.AddWithValue("$page", snapshot.Page.Id.ToString("D"));
            page.Parameters.AddWithValue("$marker", (index + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture));
            page.Parameters.AddWithValue("$sourceHash", snapshot.Page.SourceHash);
            page.Parameters.AddWithValue("$textHash", snapshot.BaselineTextHash);
            page.Parameters.AddWithValue("$textSource", snapshot.TextSource);
            page.Parameters.AddWithValue("$left", crop.Left);
            page.Parameters.AddWithValue("$top", crop.Top);
            page.Parameters.AddWithValue("$right", crop.Right);
            page.Parameters.AddWithValue("$bottom", crop.Bottom);
            page.Parameters.AddWithValue("$rotation", snapshot.Page.RotationDegrees);
            page.Parameters.AddWithValue("$role", snapshot.Page.PageRole.ToString());
            page.Parameters.AddWithValue("$profile", snapshot.Page.DisplayProfile.ToString());
            page.Parameters.AddWithValue("$sortOrder", snapshot.Page.SortOrder);
            page.Parameters.AddWithValue("$ocrRun", (object?)snapshot.OcrRunId?.ToString("D") ?? DBNull.Value);
            await page.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        foreach (var pageId in pageIds) await UpdateProofreadingStatusAsync(pageId, ProofreadingStatus.ExportedForProofreading, cancellationToken);
    }

    public async Task<ProofreadingImportPreview> PrepareConfirmedImportAsync(ProofreadingImportDocument document, CancellationToken cancellationToken)
    {
        var issues = new List<ProofreadingImportIssue>();
        var baselines = new Dictionary<string, string>(StringComparer.Ordinal);
        var projectId = await GetProjectIdAsync(cancellationToken);
        if (document.ProjectId != projectId)
            issues.Add(new ProofreadingImportIssue("ProjectMismatch", "The proofreading text belongs to a different project.", null, true));

        var known = new Dictionary<string, ExportedPageState>(StringComparer.Ordinal);
        var knownCommand = _connection.CreateCommand();
        knownCommand.CommandText = """
            SELECT page_marker, page_id, source_hash, baseline_text_hash, text_source,
                   crop_left, crop_top, crop_right, crop_bottom, rotation_degrees, page_role,
                   display_profile, sort_order, ocr_run_id
            FROM proofreading_export_pages WHERE batch_id = $batch ORDER BY page_marker;
            """;
        knownCommand.Parameters.AddWithValue("$batch", document.BatchId.ToString("D"));
        await using (var reader = await knownCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var marker = reader.GetString(0);
                known[marker] = new ExportedPageState(
                    Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    new NormalizedCrop(reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8)),
                    reader.GetInt32(9), reader.GetString(10), reader.GetString(11), reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)));
            }
        }
        if (known.Count == 0)
            issues.Add(new ProofreadingImportIssue("UnknownBatch", "The proofreading batch is not recorded by this project.", null, true));

        var importedKnownMarkers = document.Pages.Where(page => known.ContainsKey(page.PageMarker)).Select(page => page.PageMarker).ToArray();
        if (!importedKnownMarkers.SequenceEqual(known.Keys, StringComparer.Ordinal))
            issues.Add(new ProofreadingImportIssue("PageOrderChanged", "The proofreading text changes the exported page order.", null, false));

        foreach (var duplicate in document.Pages.GroupBy(page => page.PageMarker, StringComparer.Ordinal).Where(group => group.Count() > 1))
            issues.Add(new ProofreadingImportIssue("DuplicatePageMarker", "The proofreading text repeats a page marker.", duplicate.Key, true));
        foreach (var marker in known.Keys.Except(document.Pages.Select(page => page.PageMarker), StringComparer.Ordinal))
            issues.Add(new ProofreadingImportIssue("MissingPageMarker", "A page marker from the exported batch is missing.", marker, true));
        foreach (var page in document.Pages)
        {
            if (!known.ContainsKey(page.PageMarker))
                issues.Add(new ProofreadingImportIssue("UnknownPageMarker", "The proofreading text contains a page that was not exported in this batch.", page.PageMarker, true));
            if (string.IsNullOrWhiteSpace(page.ConfirmedText))
                issues.Add(new ProofreadingImportIssue("EmptyConfirmedText", "The imported page has no confirmed text.", page.PageMarker, false));
            if (page.ConfirmedText.Contains("［判読不能", StringComparison.Ordinal))
                issues.Add(new ProofreadingImportIssue("UnreadableText", "The imported page retains an unreadable-text marker.", page.PageMarker, false));
            if (ContainsInvalidStructuralMarker(page.ConfirmedText))
                issues.Add(new ProofreadingImportIssue("InvalidStructureMarker", "The imported page contains an unsupported management marker.", page.PageMarker, true));
            if (known.TryGetValue(page.PageMarker, out var exported))
            {
                var currentPage = (await LoadPagesAsync(cancellationToken)).SingleOrDefault(candidate => candidate.Id == exported.PageId);
                if (currentPage is null)
                {
                    issues.Add(new ProofreadingImportIssue("PageDeleted", "The exported page no longer exists.", page.PageMarker, true));
                    continue;
                }
                if (!currentPage.IsIncluded)
                    issues.Add(new ProofreadingImportIssue("PageExcluded", "The exported page is currently excluded.", page.PageMarker, true));
                var currentSourceHash = File.Exists(currentPage.SourcePath)
                    ? await HashFileAsync(currentPage.SourcePath, cancellationToken)
                    : currentPage.SourceHash;
                if (!string.Equals(currentSourceHash, exported.SourceHash, StringComparison.Ordinal))
                    issues.Add(new ProofreadingImportIssue("SourceImageChanged", "The source image changed after export.", page.PageMarker, true));

                var state = await LoadPageTextStateAsync(exported.PageId, cancellationToken);
                var selected = state.SelectForProofreading();
                var baseline = selected.Text;
                baselines[page.PageMarker] = baseline;
                if (!string.Equals(HashText(baseline), exported.BaselineTextHash, StringComparison.Ordinal)
                    || !string.Equals(selected.Source, exported.TextSource, StringComparison.Ordinal))
                    issues.Add(new ProofreadingImportIssue("BaselineTextChanged", "OCR or edited text changed after export.", page.PageMarker, false));
                if (currentPage.SortOrder != exported.SortOrder)
                    issues.Add(new ProofreadingImportIssue("PageSortOrderChanged", "The page order changed after export.", page.PageMarker, false));
                if (currentPage.RotationDegrees != exported.RotationDegrees)
                    issues.Add(new ProofreadingImportIssue("RotationChanged", "The page rotation changed after export.", page.PageMarker, false));
                if (currentPage.PageRole.ToString() != exported.PageRole)
                    issues.Add(new ProofreadingImportIssue("PageRoleChanged", "The page role changed after export.", page.PageMarker, false));
                if (currentPage.DisplayProfile.ToString() != exported.DisplayProfile)
                    issues.Add(new ProofreadingImportIssue("DisplayProfileChanged", "The display profile changed after export.", page.PageMarker, false));
                if ((currentPage.Crop ?? NormalizedCrop.Full) != exported.Crop)
                    issues.Add(new ProofreadingImportIssue("CropChanged", "The crop settings changed after export.", page.PageMarker, false));
                if (await GetLatestPaddleRunIdAsync(exported.PageId, cancellationToken) != exported.OcrRunId)
                    issues.Add(new ProofreadingImportIssue("OcrRunChanged", "OCR was rerun after export.", page.PageMarker, false));
                if (baseline.Length > 0 && Math.Abs(page.ConfirmedText.Length - baseline.Length) > Math.Max(20, baseline.Length / 2))
                    issues.Add(new ProofreadingImportIssue("ExtremeTextLengthChange", "The imported text length differs substantially from the OCR draft.", page.PageMarker, false));
            }
        }
        var candidates = document.Pages
            .Where(page => known.TryGetValue(page.PageMarker, out _))
            .GroupBy(page => page.PageMarker, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group =>
            {
                var confirmedText = group.Single().ConfirmedText;
                var baseline = baselines.GetValueOrDefault(group.Key, string.Empty);
                return new ProofreadingImportCandidate(
                    known[group.Key].PageId, group.Key, confirmedText, baseline,
                    ProofreadingDiff.Calculate(baseline, confirmedText), group.Single().JoinToNext);
            })
            .ToArray();
        return new ProofreadingImportPreview(document, candidates, issues);
    }

    public async Task SaveConfirmedTextAsync(ProofreadingImportPreview preview, IReadOnlySet<string> acceptedMarkers, CancellationToken cancellationToken)
    {
        if (preview.Issues.Any(issue => issue.IsError && issue.PageMarker is null))
            throw new InvalidOperationException("Proofreading import contains project-level errors and cannot be saved.");
        if (preview.Issues.Any(issue => issue.IsError && issue.PageMarker is not null && acceptedMarkers.Contains(issue.PageMarker)))
            throw new InvalidOperationException("An error page cannot be accepted.");
        await using var transaction = _connection.BeginTransaction();
        foreach (var candidate in preview.Candidates.Where(candidate => acceptedMarkers.Contains(candidate.PageMarker)))
        {
            var baselineRunId = await GetExportedOcrRunIdAsync(
                transaction, preview.Document.BatchId, candidate.PageMarker, cancellationToken);
            await AppendTextVersionIfChangedAsync(
                transaction, candidate.PageId, "Confirmed", candidate.ConfirmedText,
                "ChatGPTImport", baselineRunId, cancellationToken);
            var join = _connection.CreateCommand();
            join.Transaction = transaction;
            join.CommandText = "UPDATE pages SET boundary_join_type = $join WHERE id = $pageId;";
            join.Parameters.AddWithValue("$join", candidate.JoinToNext.ToString());
            join.Parameters.AddWithValue("$pageId", candidate.PageId.ToString("D"));
            await join.ExecuteNonQueryAsync(cancellationToken);
        }
        var import = _connection.CreateCommand();
        import.Transaction = transaction;
        import.CommandText = "INSERT INTO proofreading_imports (batch_id, imported_utc) VALUES ($batch, $utc);";
        import.Parameters.AddWithValue("$batch", preview.Document.BatchId.ToString("D"));
        import.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await import.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var candidate in preview.Candidates.Where(candidate => acceptedMarkers.Contains(candidate.PageMarker)))
            await UpdateProofreadingStatusAsync(candidate.PageId, ProofreadingStatus.Confirmed, cancellationToken);
    }

    public async Task SaveOcrAnalysisAsync(Guid pageId, OcrPageResult paddle, string rawTesseractText, OcrMergeProposal proposal, CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        await ReplaceOcrWordsAsync(transaction, pageId, paddle.Engine, paddle.ModelVersion, paddle.Words, cancellationToken);
        var executedAt = DateTimeOffset.UtcNow;
        var rubyCandidates = paddle.Words.Except(RubyFilter.ExcludeCandidates(paddle.Words)).ToHashSet();
        var paddleRunId = await SaveOcrRunAsync(transaction, pageId, paddle.Engine, paddle.ModelVersion, paddle.Words, executedAt, rubyCandidates, cancellationToken);
        await SaveOcrRunAsync(transaction, pageId, "tesseract", "jpn_vert", [new OcrWord(rawTesseractText, .8, 0, 0, 1, 1)], executedAt, null, cancellationToken);
        var reviewedWords = await LoadRunWordStatesAsync(transaction, paddleRunId, cancellationToken);
        var suggestedText = reviewedWords.Any(word => word.IsManualOverride)
            ? VerticalTextReconstruction.ReconstructReviewed(reviewedWords, 20, .75).Text
            : proposal.SuggestedText;
        var auxiliary = _connection.CreateCommand();
        auxiliary.Transaction = transaction;
        auxiliary.CommandText = """
            INSERT INTO ocr_auxiliary_results (page_id, engine, model_version, text, executed_utc)
            VALUES ($pageId, $engine, $model, $text, $utc)
            ON CONFLICT(page_id) DO UPDATE SET engine = excluded.engine, model_version = excluded.model_version,
                text = excluded.text, executed_utc = excluded.executed_utc;
            """;
        auxiliary.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        auxiliary.Parameters.AddWithValue("$engine", "tesseract");
        auxiliary.Parameters.AddWithValue("$model", "jpn_vert");
        auxiliary.Parameters.AddWithValue("$text", rawTesseractText);
        auxiliary.Parameters.AddWithValue("$utc", executedAt.ToString("O"));
        await auxiliary.ExecuteNonQueryAsync(cancellationToken);

        var proposalCommand = _connection.CreateCommand();
        proposalCommand.Transaction = transaction;
        proposalCommand.CommandText = """
            INSERT INTO ocr_merge_proposals (page_id, suggested_text, created_utc)
            VALUES ($pageId, $text, $utc)
            ON CONFLICT(page_id) DO UPDATE SET suggested_text = excluded.suggested_text, created_utc = excluded.created_utc;
            """;
        proposalCommand.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        proposalCommand.Parameters.AddWithValue("$text", suggestedText);
        proposalCommand.Parameters.AddWithValue("$utc", executedAt.ToString("O"));
        await proposalCommand.ExecuteNonQueryAsync(cancellationToken);

        var clearOperations = _connection.CreateCommand();
        clearOperations.Transaction = transaction;
        clearOperations.CommandText = "DELETE FROM ocr_merge_operations WHERE page_id = $pageId;";
        clearOperations.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await clearOperations.ExecuteNonQueryAsync(cancellationToken);
        for (var ordinal = 0; ordinal < proposal.Operations.Count; ordinal++)
        {
            var operation = proposal.Operations[ordinal];
            var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ocr_merge_operations (page_id, ordinal, operation_type, suggested_text_index, original_text, proposed_text, anchor_word_ordinal, confidence, reason)
                VALUES ($pageId, $ordinal, $type, $index, $original, $proposed, $anchor, $confidence, $reason);
                """;
            insert.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.Parameters.AddWithValue("$type", operation.Type.ToString());
            insert.Parameters.AddWithValue("$index", operation.SuggestedTextIndex);
            insert.Parameters.AddWithValue("$original", operation.OriginalText);
            insert.Parameters.AddWithValue("$proposed", operation.ProposedText);
            insert.Parameters.AddWithValue("$anchor", (object?)operation.AnchorWordOrdinal ?? DBNull.Value);
            insert.Parameters.AddWithValue("$confidence", operation.Confidence);
            insert.Parameters.AddWithValue("$reason", operation.Reason);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var clearReviewItems = _connection.CreateCommand();
        clearReviewItems.Transaction = transaction;
        clearReviewItems.CommandText = "DELETE FROM review_items WHERE page_id = $pageId AND source IN ('OCR', 'Merge', 'Ruby');";
        clearReviewItems.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await clearReviewItems.ExecuteNonQueryAsync(cancellationToken);
        foreach (var item in proposal.ReviewItems)
            await InsertReviewItemAsync(
                transaction, pageId, item.Code, item.Message, "Merge", item.Word?.Text, cancellationToken);
        foreach (var word in paddle.Words.Where(word => word.Confidence < .75))
            await InsertReviewItemAsync(
                transaction, pageId, "LowConfidence",
                $"OCR confidence {word.Confidence:P0} requires review.", "OCR", word.Text, cancellationToken);
        foreach (var word in reviewedWords.Where(word => word.Role == "RubyCandidate"))
            await InsertReviewItemAsync(
                transaction, pageId, "RubyCandidate",
                $"Ruby candidate at {word.Word.Left:0},{word.Word.Top:0}-{word.Word.Right:0},{word.Word.Bottom:0}.",
                "Ruby", word.Word.Text, cancellationToken);
        var pageRun = _connection.CreateCommand();
        pageRun.Transaction = transaction;
        pageRun.CommandText = "UPDATE pages SET last_ocr_run_id = $runId WHERE id = $pageId;";
        pageRun.Parameters.AddWithValue("$runId", paddleRunId.ToString("D"));
        pageRun.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await pageRun.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var reviewCount = proposal.ReviewItems.Count
            + paddle.Words.Count(word => word.Confidence < .75)
            + reviewedWords.Count(word => word.Role == "RubyCandidate");
        await UpdateOcrStatusAsync(pageId, reviewCount == 0 ? OcrStatus.Completed : OcrStatus.ReviewRequired, cancellationToken);
        var currentPage = (await LoadPagesAsync(cancellationToken)).Single(page => page.Id == pageId);
        var nextProofreadingStatus = currentPage.ProofreadingStatus is
            ProofreadingStatus.ManuallyEdited or ProofreadingStatus.ExportedForProofreading or ProofreadingStatus.Confirmed or ProofreadingStatus.Stale
            ? ProofreadingStatus.Stale
            : reviewCount == 0 ? ProofreadingStatus.Draft : ProofreadingStatus.ReviewRequired;
        await UpdateProofreadingStatusAsync(pageId, nextProofreadingStatus, cancellationToken);
        var review = _connection.CreateCommand();
        review.CommandText = "UPDATE pages SET review_item_count = (SELECT COUNT(*) FROM review_items WHERE page_id = $pageId) WHERE id = $pageId;";
        review.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await review.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PageTextState> LoadPageTextStateAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT engine, model_version, text, confidence, left_x, top_y, right_x, bottom_y, coordinate_status FROM ocr_words WHERE page_id = $pageId ORDER BY ordinal;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var words = new List<OcrWord>();
        var engine = "none";
        var model = "none";
        var rawPaddleCoordinatesKnown = true;
        string? legacyMergedText = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                engine = reader.GetString(0);
                model = reader.GetString(1);
                var text = reader.GetString(2);
                if (string.Equals(engine, "legacy-merged", StringComparison.Ordinal))
                {
                    rawPaddleCoordinatesKnown = false;
                    legacyMergedText ??= text;
                    continue;
                }
                rawPaddleCoordinatesKnown &= string.Equals(reader.GetString(8), "Known", StringComparison.Ordinal);
                words.Add(new OcrWord(text, reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7)));
            }
        }
        var manual = _connection.CreateCommand();
        manual.CommandText = "SELECT text FROM manual_page_text WHERE page_id = $pageId;";
        manual.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var manualText = await manual.ExecuteScalarAsync(cancellationToken) as string;
        var auxiliary = _connection.CreateCommand();
        auxiliary.CommandText = "SELECT text FROM ocr_auxiliary_results WHERE page_id = $pageId;";
        auxiliary.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var rawTesseractText = await auxiliary.ExecuteScalarAsync(cancellationToken) as string;
        var suggested = _connection.CreateCommand();
        suggested.CommandText = "SELECT suggested_text FROM ocr_merge_proposals WHERE page_id = $pageId;";
        suggested.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var suggestedText = await suggested.ExecuteScalarAsync(cancellationToken) as string;
        var confirmed = _connection.CreateCommand();
        confirmed.CommandText = """
            SELECT version.text, version.created_utc, version.source
            FROM page_text_versions AS version
            WHERE version.page_id = $pageId
              AND version.kind = 'Confirmed'
              AND NOT EXISTS (
                  SELECT 1
                  FROM manual_page_text AS manual
                  WHERE manual.page_id = version.page_id
                    AND manual.updated_utc > version.created_utc)
            ORDER BY version.rowid DESC
            LIMIT 1;
            """;
        confirmed.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        string? confirmedText = null;
        DateTimeOffset? confirmedAt = null;
        string? confirmedSource = null;
        await using (var confirmedReader = await confirmed.ExecuteReaderAsync(cancellationToken))
        {
            if (await confirmedReader.ReadAsync(cancellationToken))
            {
                confirmedText = confirmedReader.GetString(0);
                confirmedAt = DateTimeOffset.Parse(confirmedReader.GetString(1), System.Globalization.CultureInfo.InvariantCulture);
                confirmedSource = confirmedReader.GetString(2);
            }
        }
        return new PageTextState(pageId, manualText, engine, model, words, rawTesseractText, suggestedText, confirmedText, confirmedAt, confirmedSource, rawPaddleCoordinatesKnown, legacyMergedText);
    }

    public async Task<IReadOnlyList<OcrRunInfo>> LoadOcrRunsAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, engine, model_version, executed_utc, word_count FROM ocr_runs WHERE page_id = $pageId ORDER BY executed_utc DESC;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var runs = new List<OcrRunInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            runs.Add(new OcrRunInfo(Guid.Parse(reader.GetString(0)), pageId, reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt32(4)));
        return runs;
    }

    public async Task<IReadOnlyList<OcrWordReviewState>> LoadLatestOcrWordStatesAsync(
        Guid pageId,
        CancellationToken cancellationToken)
    {
        var runId = await GetLatestPaddleRunIdAsync(pageId, cancellationToken);
        if (runId is null) return [];
        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT ordinal, text, confidence, left_x, top_y, right_x, bottom_y,
                   role, included_in_draft, manual_override
            FROM ocr_run_words WHERE run_id = $runId ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$runId", runId.Value.ToString("D"));
        var result = new List<OcrWordReviewState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new OcrWordReviewState(
                runId.Value, reader.GetInt32(0),
                new OcrWord(reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3),
                    reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)),
                reader.GetString(7), reader.GetInt32(8) != 0, reader.GetInt32(9) != 0));
        return result;
    }

    public async Task UpdateOcrWordReviewAsync(
        Guid pageId,
        Guid runId,
        int ordinal,
        string role,
        bool includedInDraft,
        CancellationToken cancellationToken)
    {
        if (role is not ("Body" or "RubyCandidate"))
            throw new ArgumentOutOfRangeException(nameof(role));
        await using var transaction = _connection.BeginTransaction();
        var wordCommand = _connection.CreateCommand();
        wordCommand.Transaction = transaction;
        wordCommand.CommandText = """
            SELECT text, left_x, top_y, right_x, bottom_y
            FROM ocr_run_words
            WHERE run_id = $runId AND ordinal = $ordinal
              AND run_id IN (SELECT id FROM ocr_runs WHERE page_id = $pageId);
            """;
        wordCommand.Parameters.AddWithValue("$runId", runId.ToString("D"));
        wordCommand.Parameters.AddWithValue("$ordinal", ordinal);
        wordCommand.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        string wordKey;
        await using (var reader = await wordCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("OCR word no longer exists.");
            wordKey = CreateWordKey(
                reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2),
                reader.GetDouble(3), reader.GetDouble(4));
        }

        var persist = _connection.CreateCommand();
        persist.Transaction = transaction;
        persist.CommandText = """
            INSERT INTO ocr_word_overrides (page_id, word_key, role, included_in_draft, updated_utc)
            VALUES ($pageId, $key, $role, $included, $utc)
            ON CONFLICT(page_id, word_key) DO UPDATE SET
                role = excluded.role, included_in_draft = excluded.included_in_draft,
                updated_utc = excluded.updated_utc;
            """;
        persist.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        persist.Parameters.AddWithValue("$key", wordKey);
        persist.Parameters.AddWithValue("$role", role);
        persist.Parameters.AddWithValue("$included", includedInDraft ? 1 : 0);
        persist.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await persist.ExecuteNonQueryAsync(cancellationToken);

        var update = _connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE ocr_run_words SET role = $role, included_in_draft = $included, manual_override = 1
            WHERE run_id = $runId AND ordinal = $ordinal;
            """;
        update.Parameters.AddWithValue("$role", role);
        update.Parameters.AddWithValue("$included", includedInDraft ? 1 : 0);
        update.Parameters.AddWithValue("$runId", runId.ToString("D"));
        update.Parameters.AddWithValue("$ordinal", ordinal);
        await update.ExecuteNonQueryAsync(cancellationToken);
        var reviewedWords = await LoadRunWordStatesAsync(transaction, runId, cancellationToken);
        var draft = VerticalTextReconstruction.ReconstructReviewed(reviewedWords, 20, .75).Text;
        var clearRubyReview = _connection.CreateCommand();
        clearRubyReview.Transaction = transaction;
        clearRubyReview.CommandText = "DELETE FROM review_items WHERE page_id = $pageId AND source = 'Ruby';";
        clearRubyReview.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await clearRubyReview.ExecuteNonQueryAsync(cancellationToken);
        foreach (var reviewed in reviewedWords.Where(word => word.Role == "RubyCandidate"))
            await InsertReviewItemAsync(
                transaction, pageId, "RubyCandidate",
                $"Ruby candidate at {reviewed.Word.Left:0},{reviewed.Word.Top:0}-{reviewed.Word.Right:0},{reviewed.Word.Bottom:0}.",
                "Ruby", reviewed.Word.Text, cancellationToken);
        var proposal = _connection.CreateCommand();
        proposal.Transaction = transaction;
        proposal.CommandText = """
            INSERT INTO ocr_merge_proposals (page_id, suggested_text, created_utc)
            VALUES ($pageId, $text, $utc)
            ON CONFLICT(page_id) DO UPDATE SET suggested_text = excluded.suggested_text, created_utc = excluded.created_utc;
            """;
        proposal.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        proposal.Parameters.AddWithValue("$text", draft);
        proposal.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await proposal.ExecuteNonQueryAsync(cancellationToken);
        var reviewCount = _connection.CreateCommand();
        reviewCount.Transaction = transaction;
        reviewCount.CommandText = """
            UPDATE pages
            SET review_item_count = (SELECT COUNT(*) FROM review_items WHERE page_id = $pageId),
                ocr_status = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM review_items
                        WHERE page_id = $pageId AND source IN ('OCR', 'Merge', 'Ruby'))
                    THEN 'ReviewRequired'
                    ELSE 'Completed'
                END
            WHERE id = $pageId;
            """;
        reviewCount.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await reviewCount.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<OcrWordReviewState>> LoadRunWordStatesAsync(
        SqliteTransaction transaction,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ordinal, text, confidence, left_x, top_y, right_x, bottom_y,
                   role, included_in_draft, manual_override
            FROM ocr_run_words WHERE run_id = $runId ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        var result = new List<OcrWordReviewState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new OcrWordReviewState(
                runId, reader.GetInt32(0),
                new OcrWord(reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3),
                    reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)),
                reader.GetString(7), reader.GetInt32(8) != 0, reader.GetInt32(9) != 0));
        return result;
    }

    public async Task RecordOcrFailureAsync(
        OcrFailure failure,
        CancellationToken cancellationToken,
        OcrStatus? cancelledStatusToRestore = null)
    {
        await using var transaction = _connection.BeginTransaction();
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ocr_failures (
                id, page_id, file_name, stage, exception_type, message,
                retryable, was_cancelled, occurred_utc)
            VALUES (
                $id, $pageId, $fileName, $stage, $exceptionType, $message,
                $retryable, $cancelled, $occurred);
            """;
        command.Parameters.AddWithValue("$id", failure.Id.ToString("D"));
        command.Parameters.AddWithValue("$pageId", failure.PageId.ToString("D"));
        command.Parameters.AddWithValue("$fileName", failure.FileName);
        command.Parameters.AddWithValue("$stage", failure.Stage.ToString());
        command.Parameters.AddWithValue("$exceptionType", failure.ExceptionType);
        command.Parameters.AddWithValue("$message", failure.Message);
        command.Parameters.AddWithValue("$retryable", failure.Retryable ? 1 : 0);
        command.Parameters.AddWithValue("$cancelled", failure.WasCancelled ? 1 : 0);
        command.Parameters.AddWithValue("$occurred", failure.OccurredAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (failure.WasCancelled)
        {
            if (cancelledStatusToRestore is not null)
            {
                await UpdateOcrStatusAsync(
                    transaction, failure.PageId, cancelledStatusToRestore.Value, cancellationToken);
            }
            else
            {
                var restore = _connection.CreateCommand();
                restore.Transaction = transaction;
                restore.CommandText = """
                    UPDATE pages
                    SET ocr_status = CASE
                        WHEN EXISTS (SELECT 1 FROM ocr_words WHERE ocr_words.page_id = pages.id)
                            THEN CASE
                                WHEN EXISTS (
                                    SELECT 1 FROM review_items
                                    WHERE page_id = pages.id AND source IN ('OCR', 'Merge', 'Ruby'))
                                THEN 'ReviewRequired'
                                ELSE 'Completed'
                            END
                        ELSE 'NotProcessed'
                    END
                    WHERE id = $pageId;
                    """;
                restore.Parameters.AddWithValue("$pageId", failure.PageId.ToString("D"));
                await restore.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        else
        {
            await UpdateOcrStatusAsync(
                transaction, failure.PageId, OcrStatus.Failed, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OcrFailure>> LoadOcrFailuresAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_name, stage, exception_type, message, retryable, was_cancelled, occurred_utc
            FROM ocr_failures WHERE page_id = $pageId ORDER BY occurred_utc DESC, rowid DESC;
            """;
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var failures = new List<OcrFailure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            failures.Add(new OcrFailure(
                Guid.Parse(reader.GetString(0)), pageId, reader.GetString(1),
                Enum.Parse<OcrFailureStage>(reader.GetString(2)), reader.GetString(3), reader.GetString(4),
                reader.GetInt32(5) != 0, reader.GetInt32(6) != 0,
                DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture)));
        return failures;
    }

    public Task SetOcrStatusAsync(Guid pageId, OcrStatus status, CancellationToken cancellationToken) =>
        UpdateOcrStatusAsync(pageId, status, cancellationToken);

    public async Task ReplacePageValidationIssuesAsync(
        IReadOnlyList<PageValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        await ExecuteAsync(transaction, "DELETE FROM review_items WHERE source = 'PageValidation';", cancellationToken);
        foreach (var issue in issues)
        {
            var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO review_items (id, page_id, code, message, source, text, created_utc)
                VALUES ($id, $pageId, $code, $message, 'PageValidation', NULL, $utc);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$pageId", issue.PageId.ToString("D"));
            command.Parameters.AddWithValue("$code", issue.Code);
            command.Parameters.AddWithValue("$message", issue.Message);
            command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var counts = _connection.CreateCommand();
        counts.Transaction = transaction;
        counts.CommandText = """
            UPDATE pages SET review_item_count =
                (SELECT COUNT(*) FROM review_items WHERE review_items.page_id = pages.id);
            """;
        await counts.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredReviewItem>> LoadReviewItemsAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, message, source, text, created_utc
            FROM review_items WHERE page_id = $pageId ORDER BY created_utc, rowid;
            """;
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var items = new List<StoredReviewItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new StoredReviewItem(
                Guid.Parse(reader.GetString(0)), pageId, reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture)));
        return items;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
        SqliteConnection.ClearPool(_connection);
        await _connection.DisposeAsync();
    }

    private async Task UpdateProofreadingStatusAsync(Guid pageId, ProofreadingStatus status, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "UPDATE pages SET proofreading_status = $status WHERE id = $pageId;";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertReviewItemAsync(
        SqliteTransaction transaction,
        Guid pageId,
        string code,
        string message,
        string source,
        string? text,
        CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO review_items (id, page_id, code, message, source, text, created_utc)
            VALUES ($id, $pageId, $code, $message, $source, $text, $utc);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$text", (object?)text ?? DBNull.Value);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateProofreadingStatusAsync(
        SqliteTransaction transaction,
        Guid pageId,
        ProofreadingStatus status,
        CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE pages SET proofreading_status = $status WHERE id = $pageId;";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateOcrStatusAsync(Guid pageId, OcrStatus status, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "UPDATE pages SET ocr_status = $status WHERE id = $pageId;";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateOcrStatusAsync(
        SqliteTransaction transaction,
        Guid pageId,
        OcrStatus status,
        CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE pages SET ocr_status = $status WHERE id = $pageId;";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task AppendTextVersionIfChangedAsync(
        SqliteTransaction transaction,
        Guid pageId,
        string kind,
        string text,
        string source,
        Guid? baselineOcrRunId,
        CancellationToken cancellationToken)
    {
        var latest = _connection.CreateCommand();
        latest.Transaction = transaction;
        latest.CommandText = """
            SELECT kind, text
            FROM page_text_versions
            WHERE page_id = $pageId
            ORDER BY rowid DESC
            LIMIT 1;
            """;
        latest.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        await using (var reader = await latest.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken)
                && string.Equals(reader.GetString(0), kind, StringComparison.Ordinal)
                && string.Equals(reader.GetString(1), text, StringComparison.Ordinal))
                return;
        }

        var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO page_text_versions (page_id, kind, text, created_utc, source, baseline_ocr_run_id)
            VALUES ($pageId, $kind, $text, $utc, $source, $baseline);
            """;
        insert.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        insert.Parameters.AddWithValue("$source", source);
        insert.Parameters.AddWithValue("$baseline", (object?)baselineOcrRunId?.ToString("D") ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool ContainsInvalidStructuralMarker(string text)
    {
        var remaining = text;
        while (true)
        {
            var start = remaining.IndexOf("[[", StringComparison.Ordinal);
            if (start < 0) return false;
            var end = remaining.IndexOf("]]", start, StringComparison.Ordinal);
            if (end < 0) return true;
            var marker = remaining[start..(end + 2)];
            if (!marker.StartsWith("[[CHAPTER:", StringComparison.Ordinal)
                && !marker.StartsWith("[[TITLE:", StringComparison.Ordinal)
                && !marker.StartsWith("[[SECTION_TITLE:", StringComparison.Ordinal)
                && !marker.StartsWith("[[SECTION:", StringComparison.Ordinal)) return true;
            remaining = remaining[(end + 2)..];
        }
    }

    private async Task<Guid> SaveOcrRunAsync(SqliteTransaction transaction, Guid pageId, string engine, string modelVersion, IReadOnlyList<OcrWord> words, DateTimeOffset executedAt, IReadOnlySet<OcrWord>? rubyCandidates, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var run = _connection.CreateCommand();
        run.Transaction = transaction;
        run.CommandText = "INSERT INTO ocr_runs (id, page_id, engine, model_version, executed_utc, word_count) VALUES ($id, $page, $engine, $model, $utc, $count);";
        run.Parameters.AddWithValue("$id", runId.ToString("D"));
        run.Parameters.AddWithValue("$page", pageId.ToString("D"));
        run.Parameters.AddWithValue("$engine", engine);
        run.Parameters.AddWithValue("$model", modelVersion);
        run.Parameters.AddWithValue("$utc", executedAt.ToString("O"));
        run.Parameters.AddWithValue("$count", words.Count);
        await run.ExecuteNonQueryAsync(cancellationToken);
        for (var ordinal = 0; ordinal < words.Count; ordinal++)
        {
            var word = words[ordinal];
            var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            var automaticRole = rubyCandidates?.Contains(word) == true ? "RubyCandidate" : "Body";
            var automaticIncluded = automaticRole == "Body";
            var wordKey = CreateWordKey(word.Text, word.Left, word.Top, word.Right, word.Bottom);
            var overrideCommand = _connection.CreateCommand();
            overrideCommand.Transaction = transaction;
            overrideCommand.CommandText = "SELECT role, included_in_draft FROM ocr_word_overrides WHERE page_id = $pageId AND word_key = $key;";
            overrideCommand.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
            overrideCommand.Parameters.AddWithValue("$key", wordKey);
            string role = automaticRole;
            var included = automaticIncluded;
            var manualOverride = false;
            await using (var overrideReader = await overrideCommand.ExecuteReaderAsync(cancellationToken))
            {
                if (await overrideReader.ReadAsync(cancellationToken))
                {
                    role = overrideReader.GetString(0);
                    included = overrideReader.GetInt32(1) != 0;
                    manualOverride = true;
                }
            }
            insert.CommandText = """
                INSERT INTO ocr_run_words (
                    run_id, ordinal, text, confidence, left_x, top_y, right_x, bottom_y,
                    role, included_in_draft, coordinate_status, manual_override)
                VALUES (
                    $run, $ordinal, $text, $confidence, $left, $top, $right, $bottom,
                    $role, $included, 'Known', $manual);
                """;
            insert.Parameters.AddWithValue("$run", runId.ToString("D"));
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.Parameters.AddWithValue("$text", word.Text);
            insert.Parameters.AddWithValue("$confidence", word.Confidence);
            insert.Parameters.AddWithValue("$left", word.Left);
            insert.Parameters.AddWithValue("$top", word.Top);
            insert.Parameters.AddWithValue("$right", word.Right);
            insert.Parameters.AddWithValue("$bottom", word.Bottom);
            insert.Parameters.AddWithValue("$role", role);
            insert.Parameters.AddWithValue("$included", included ? 1 : 0);
            insert.Parameters.AddWithValue("$manual", manualOverride ? 1 : 0);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        return runId;
    }

    private async Task<Guid?> GetLatestPaddleRunIdAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id FROM ocr_runs
            WHERE page_id = $pageId AND engine = 'paddle'
            ORDER BY executed_utc DESC, rowid DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return Guid.TryParse(value, out var runId) ? runId : null;
    }

    private async Task<Guid?> GetExportedOcrRunIdAsync(
        SqliteTransaction transaction,
        Guid batchId,
        string pageMarker,
        CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ocr_run_id FROM proofreading_export_pages WHERE batch_id = $batch AND page_marker = $marker;";
        command.Parameters.AddWithValue("$batch", batchId.ToString("D"));
        command.Parameters.AddWithValue("$marker", pageMarker);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return Guid.TryParse(value, out var runId) ? runId : null;
    }

    private static ProofreadingStatus ParseProofreadingStatus(string value) =>
        value switch
        {
            "NotProofread" => ProofreadingStatus.NotOcrProcessed,
            _ when Enum.TryParse<ProofreadingStatus>(value, out var status) => status,
            _ => ProofreadingStatus.ReviewRequired
        };

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string CreateWordKey(string text, double left, double top, double right, double bottom)
    {
        var value = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{text}\u001f{left:F3}\u001f{top:F3}\u001f{right:F3}\u001f{bottom:F3}");
        return HashText(value);
    }

    private sealed record ProofreadingExportSnapshot(
        ProjectPage Page,
        string BaselineTextHash,
        string TextSource,
        Guid? OcrRunId);

    private sealed record ExportedPageState(
        Guid PageId,
        string SourceHash,
        string BaselineTextHash,
        string TextSource,
        NormalizedCrop Crop,
        int RotationDegrees,
        string PageRole,
        string DisplayProfile,
        int SortOrder,
        Guid? OcrRunId);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        try
        {
            await ExecuteAsync(transaction, """
            CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);
            INSERT INTO schema_version (version) SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM schema_version);
            CREATE TABLE IF NOT EXISTS pages (
                id TEXT PRIMARY KEY NOT NULL,
                file_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                included INTEGER NOT NULL,
                rotation_degrees INTEGER NOT NULL,
                crop_left REAL NOT NULL DEFAULT 0, crop_top REAL NOT NULL DEFAULT 0,
                crop_right REAL NOT NULL DEFAULT 1, crop_bottom REAL NOT NULL DEFAULT 1,
                display_profile TEXT NOT NULL DEFAULT 'ReflowVertical', page_role TEXT NOT NULL DEFAULT 'Body',
                printed_page_number TEXT NULL, proofreading_status TEXT NOT NULL DEFAULT 'NotOcrProcessed',
                review_item_count INTEGER NOT NULL DEFAULT 0, ocr_status TEXT NOT NULL DEFAULT 'NotProcessed',
                last_ocr_run_id TEXT NULL, boundary_join_type TEXT NOT NULL DEFAULT 'DirectJoin'
            );
            CREATE TABLE IF NOT EXISTS ocr_words (
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                engine TEXT NOT NULL,
                model_version TEXT NOT NULL,
                text TEXT NOT NULL,
                confidence REAL NOT NULL,
                left_x REAL NOT NULL,
                top_y REAL NOT NULL,
                right_x REAL NOT NULL,
                bottom_y REAL NOT NULL,
                coordinate_status TEXT NOT NULL DEFAULT 'Known',
                PRIMARY KEY (page_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS manual_page_text (
                page_id TEXT PRIMARY KEY NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                text TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ocr_auxiliary_results (
                page_id TEXT PRIMARY KEY NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                engine TEXT NOT NULL, model_version TEXT NOT NULL, text TEXT NOT NULL, executed_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ocr_runs (
                id TEXT PRIMARY KEY NOT NULL, page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                engine TEXT NOT NULL, model_version TEXT NOT NULL, executed_utc TEXT NOT NULL, word_count INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ocr_run_words (
                run_id TEXT NOT NULL REFERENCES ocr_runs(id) ON DELETE CASCADE, ordinal INTEGER NOT NULL,
                text TEXT NOT NULL, confidence REAL NOT NULL, left_x REAL NOT NULL, top_y REAL NOT NULL,
                right_x REAL NOT NULL, bottom_y REAL NOT NULL, role TEXT NOT NULL, included_in_draft INTEGER NOT NULL,
                coordinate_status TEXT NOT NULL, manual_override INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (run_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS ocr_word_overrides (
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                word_key TEXT NOT NULL, role TEXT NOT NULL, included_in_draft INTEGER NOT NULL,
                updated_utc TEXT NOT NULL, PRIMARY KEY (page_id, word_key)
            );
            CREATE TABLE IF NOT EXISTS ocr_merge_proposals (
                page_id TEXT PRIMARY KEY NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                suggested_text TEXT NOT NULL, created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ocr_merge_operations (
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL, operation_type TEXT NOT NULL, suggested_text_index INTEGER NOT NULL,
                original_text TEXT NOT NULL, proposed_text TEXT NOT NULL, anchor_word_ordinal INTEGER NULL,
                confidence REAL NOT NULL, reason TEXT NOT NULL, PRIMARY KEY (page_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS page_text_versions (
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                kind TEXT NOT NULL, text TEXT NOT NULL, created_utc TEXT NOT NULL, source TEXT NOT NULL,
                baseline_ocr_run_id TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS project_metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS proofreading_exports (
                batch_id TEXT PRIMARY KEY NOT NULL, project_id TEXT NOT NULL, exported_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS proofreading_export_pages (
                batch_id TEXT NOT NULL REFERENCES proofreading_exports(batch_id) ON DELETE CASCADE,
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                page_marker TEXT NOT NULL, source_hash TEXT NOT NULL DEFAULT '',
                baseline_text_hash TEXT NOT NULL DEFAULT '', text_source TEXT NOT NULL DEFAULT 'RawPaddle',
                crop_left REAL NOT NULL DEFAULT 0, crop_top REAL NOT NULL DEFAULT 0,
                crop_right REAL NOT NULL DEFAULT 1, crop_bottom REAL NOT NULL DEFAULT 1,
                rotation_degrees INTEGER NOT NULL DEFAULT 0, page_role TEXT NOT NULL DEFAULT 'Body',
                display_profile TEXT NOT NULL DEFAULT 'ReflowVertical', sort_order INTEGER NOT NULL DEFAULT 0,
                ocr_run_id TEXT NULL, PRIMARY KEY (batch_id, page_marker)
            );
            CREATE TABLE IF NOT EXISTS proofreading_imports (
                batch_id TEXT NOT NULL REFERENCES proofreading_exports(batch_id) ON DELETE CASCADE,
                imported_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ocr_failures (
                id TEXT PRIMARY KEY NOT NULL,
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                file_name TEXT NOT NULL, stage TEXT NOT NULL, exception_type TEXT NOT NULL,
                message TEXT NOT NULL, retryable INTEGER NOT NULL, was_cancelled INTEGER NOT NULL,
                occurred_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS review_items (
                id TEXT PRIMARY KEY NOT NULL,
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                code TEXT NOT NULL, message TEXT NOT NULL, source TEXT NOT NULL,
                text TEXT NULL, created_utc TEXT NOT NULL
            );
            """, cancellationToken);

            var currentVersion = await ReadSchemaVersionAsync(transaction, cancellationToken);
            if (currentVersion < 1)
            {
                await AddColumnIfMissingAsync(transaction, "pages", "crop_left", "REAL NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "crop_top", "REAL NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "crop_right", "REAL NOT NULL DEFAULT 1", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "crop_bottom", "REAL NOT NULL DEFAULT 1", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "display_profile", "TEXT NOT NULL DEFAULT 'ReflowVertical'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "page_role", "TEXT NOT NULL DEFAULT 'Body'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "printed_page_number", "TEXT NULL", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "proofreading_status", "TEXT NOT NULL DEFAULT 'NotOcrProcessed'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "review_item_count", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "ocr_words", "coordinate_status", "TEXT NOT NULL DEFAULT 'Known'", cancellationToken);
                await SetSchemaVersionAsync(transaction, 1, cancellationToken);
            }

            if (currentVersion < 2)
            {
                await AddColumnIfMissingAsync(transaction, "pages", "ocr_status", "TEXT NOT NULL DEFAULT 'NotProcessed'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "pages", "last_ocr_run_id", "TEXT NULL", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "page_text_versions", "baseline_ocr_run_id", "TEXT NULL", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "source_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "baseline_text_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "text_source", "TEXT NOT NULL DEFAULT 'RawPaddle'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "crop_left", "REAL NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "crop_top", "REAL NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "crop_right", "REAL NOT NULL DEFAULT 1", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "crop_bottom", "REAL NOT NULL DEFAULT 1", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "rotation_degrees", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "page_role", "TEXT NOT NULL DEFAULT 'Body'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "display_profile", "TEXT NOT NULL DEFAULT 'ReflowVertical'", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "sort_order", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
                await AddColumnIfMissingAsync(transaction, "proofreading_export_pages", "ocr_run_id", "TEXT NULL", cancellationToken);
                await SetSchemaVersionAsync(transaction, 2, cancellationToken);
            }

            if (currentVersion < 3)
                await SetSchemaVersionAsync(transaction, 3, cancellationToken);

            if (currentVersion < 4)
            {
                var preserveLegacyReview = _connection.CreateCommand();
                preserveLegacyReview.Transaction = transaction;
                preserveLegacyReview.CommandText = """
                    INSERT INTO review_items (id, page_id, code, message, source, text, created_utc)
                    SELECT lower(hex(randomblob(16))), id, 'LegacyReviewCount',
                           'This page had review items before review-item detail storage was introduced.',
                           'Migration', NULL, $utc
                    FROM pages
                    WHERE review_item_count > 0
                      AND NOT EXISTS (SELECT 1 FROM review_items WHERE review_items.page_id = pages.id);
                    """;
                preserveLegacyReview.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
                await preserveLegacyReview.ExecuteNonQueryAsync(cancellationToken);
                await SetSchemaVersionAsync(transaction, 4, cancellationToken);
            }

            if (currentVersion < 5)
            {
                await AddColumnIfMissingAsync(transaction, "ocr_run_words", "manual_override", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
                await SetSchemaVersionAsync(transaction, 5, cancellationToken);
            }

            if (currentVersion < 6)
            {
                await AddColumnIfMissingAsync(transaction, "pages", "boundary_join_type", "TEXT NOT NULL DEFAULT 'DirectJoin'", cancellationToken);
                await SetSchemaVersionAsync(transaction, 6, cancellationToken);
            }

            var legacyMigration = _connection.CreateCommand();
            legacyMigration.Transaction = transaction;
            legacyMigration.CommandText = """
            INSERT INTO ocr_merge_proposals (page_id, suggested_text, created_utc)
            SELECT page_id, text, $utc FROM ocr_words WHERE engine = 'paddle+tesseract'
            ON CONFLICT(page_id) DO NOTHING;
            UPDATE ocr_words SET engine = 'legacy-merged', coordinate_status = 'Unknown'
            WHERE engine = 'paddle+tesseract' AND left_x = 0 AND top_y = 0 AND right_x = 1 AND bottom_y = 1;
            UPDATE pages SET proofreading_status = 'ReviewRequired', review_item_count = CASE WHEN review_item_count < 1 THEN 1 ELSE review_item_count END
            WHERE id IN (SELECT page_id FROM ocr_words WHERE engine = 'legacy-merged');
            """;
            legacyMigration.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await legacyMigration.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ExecuteAsync(SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> ReadSchemaVersionAsync(SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task SetSchemaVersionAsync(SqliteTransaction transaction, int version, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE schema_version SET version = $version;";
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task AddColumnIfMissingAsync(
        SqliteTransaction transaction,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var columns = _connection.CreateCommand();
        columns.Transaction = transaction;
        columns.CommandText = $"PRAGMA table_info(\"{table}\");";
        var exists = false;
        await using (var reader = await columns.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                exists |= string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase);
        }
        if (exists) return;
        await ExecuteAsync(transaction, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};", cancellationToken);
    }
}
