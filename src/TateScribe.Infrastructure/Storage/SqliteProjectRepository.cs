using Microsoft.Data.Sqlite;
using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Core.Images;
using TateScribe.Core.Layout;
using TateScribe.Core.Proofreading;

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
                INSERT INTO pages (id, file_name, source_path, source_hash, sort_order, included, rotation_degrees, crop_left, crop_top, crop_right, crop_bottom, display_profile, page_role, printed_page_number, proofreading_status, review_item_count)
                VALUES ($id, $name, $path, $hash, $order, $included, $rotation, $left, $top, $right, $bottom, $profile, $role, $printedPage, $status, $reviewCount)
                ON CONFLICT(id) DO UPDATE SET
                    file_name = excluded.file_name,
                    source_path = excluded.source_path,
                    source_hash = excluded.source_hash,
                    sort_order = excluded.sort_order,
                    included = excluded.included,
                    rotation_degrees = excluded.rotation_degrees, crop_left = excluded.crop_left, crop_top = excluded.crop_top, crop_right = excluded.crop_right, crop_bottom = excluded.crop_bottom,
                    display_profile = excluded.display_profile, page_role = excluded.page_role, printed_page_number = excluded.printed_page_number, proofreading_status = excluded.proofreading_status, review_item_count = excluded.review_item_count;
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
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPage>> LoadPagesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, file_name, source_path, source_hash, sort_order, included, rotation_degrees, crop_left, crop_top, crop_right, crop_bottom, display_profile, page_role, printed_page_number, proofreading_status, review_item_count FROM pages ORDER BY sort_order;";
        var result = new List<ProjectPage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectPage(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) != 0, reader.GetInt32(6),
                new NormalizedCrop(reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9), reader.GetDouble(10)),
                Enum.Parse<DisplayProfile>(reader.GetString(11)), Enum.Parse<PageRole>(reader.GetString(12)), reader.IsDBNull(13) ? null : reader.GetString(13), Enum.Parse<ProofreadingStatus>(reader.GetString(14)), reader.GetInt32(15)));
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
        var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO manual_page_text (page_id, text, updated_utc) VALUES ($pageId, $text, $utc) ON CONFLICT(page_id) DO UPDATE SET text = excluded.text, updated_utc = excluded.updated_utc;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await UpdateProofreadingStatusAsync(pageId, ProofreadingStatus.ManuallyEdited, cancellationToken);
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
        await using var transaction = _connection.BeginTransaction();
        var export = _connection.CreateCommand();
        export.Transaction = transaction;
        export.CommandText = "INSERT INTO proofreading_exports (batch_id, project_id, exported_utc) VALUES ($batch, $project, $utc);";
        export.Parameters.AddWithValue("$batch", batchId.ToString("D"));
        export.Parameters.AddWithValue("$project", projectId.ToString("D"));
        export.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await export.ExecuteNonQueryAsync(cancellationToken);
        for (var index = 0; index < pageIds.Count; index++)
        {
            var page = _connection.CreateCommand();
            page.Transaction = transaction;
            page.CommandText = "INSERT INTO proofreading_export_pages (batch_id, page_id, page_marker) VALUES ($batch, $page, $marker);";
            page.Parameters.AddWithValue("$batch", batchId.ToString("D"));
            page.Parameters.AddWithValue("$page", pageIds[index].ToString("D"));
            page.Parameters.AddWithValue("$marker", (index + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture));
            await page.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        foreach (var pageId in pageIds) await UpdateProofreadingStatusAsync(pageId, ProofreadingStatus.ExportedForProofreading, cancellationToken);
    }

    public async Task<ProofreadingImportPreview> PrepareConfirmedImportAsync(ProofreadingImportDocument document, CancellationToken cancellationToken)
    {
        var issues = new List<ProofreadingImportIssue>();
        var projectId = await GetProjectIdAsync(cancellationToken);
        if (document.ProjectId != projectId)
            issues.Add(new ProofreadingImportIssue("ProjectMismatch", "The proofreading text belongs to a different project.", null, true));

        var known = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var knownCommand = _connection.CreateCommand();
        knownCommand.CommandText = "SELECT page_marker, page_id FROM proofreading_export_pages WHERE batch_id = $batch ORDER BY page_marker;";
        knownCommand.Parameters.AddWithValue("$batch", document.BatchId.ToString("D"));
        await using (var reader = await knownCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) known[reader.GetString(0)] = Guid.Parse(reader.GetString(1));
        }
        if (known.Count == 0)
            issues.Add(new ProofreadingImportIssue("UnknownBatch", "The proofreading batch is not recorded by this project.", null, true));

        var importedKnownMarkers = document.Pages.Where(page => known.ContainsKey(page.PageMarker)).Select(page => page.PageMarker).ToArray();
        if (!importedKnownMarkers.SequenceEqual(known.Keys, StringComparer.Ordinal))
            issues.Add(new ProofreadingImportIssue("PageOrderChanged", "The proofreading text changes the exported page order.", null, true));

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
            if (known.TryGetValue(page.PageMarker, out var pageId))
            {
                var state = await LoadPageTextStateAsync(pageId, cancellationToken);
                var baseline = state.ManualText ?? state.SuggestedText ?? VerticalTextReconstruction.Reconstruct(state.RawPaddleWords, 20, .75).Text;
                if (baseline.Length > 0 && Math.Abs(page.ConfirmedText.Length - baseline.Length) > Math.Max(20, baseline.Length / 2))
                    issues.Add(new ProofreadingImportIssue("ExtremeTextLengthChange", "The imported text length differs substantially from the OCR draft.", page.PageMarker, false));
            }
        }
        var candidates = document.Pages
            .Where(page => known.TryGetValue(page.PageMarker, out _))
            .GroupBy(page => page.PageMarker, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => new ProofreadingImportCandidate(known[group.Key], group.Key, group.Single().ConfirmedText))
            .ToArray();
        return new ProofreadingImportPreview(document, candidates, issues);
    }

    public async Task SaveConfirmedTextAsync(ProofreadingImportPreview preview, IReadOnlySet<string> acceptedMarkers, CancellationToken cancellationToken)
    {
        if (preview.Issues.Any(issue => issue.IsError)) throw new InvalidOperationException("Proofreading import contains errors and cannot be saved.");
        await using var transaction = _connection.BeginTransaction();
        foreach (var candidate in preview.Candidates.Where(candidate => acceptedMarkers.Contains(candidate.PageMarker)))
        {
            var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO page_text_versions (page_id, kind, text, created_utc, source) VALUES ($pageId, 'Confirmed', $text, $utc, 'ChatGPTImport');";
            insert.Parameters.AddWithValue("$pageId", candidate.PageId.ToString("D"));
            insert.Parameters.AddWithValue("$text", candidate.ConfirmedText);
            insert.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
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
        await SaveOcrRunAsync(transaction, pageId, paddle.Engine, paddle.ModelVersion, paddle.Words, executedAt, rubyCandidates, cancellationToken);
        await SaveOcrRunAsync(transaction, pageId, "tesseract", "jpn_vert", [new OcrWord(rawTesseractText, .8, 0, 0, 1, 1)], executedAt, null, cancellationToken);
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
        proposalCommand.Parameters.AddWithValue("$text", proposal.SuggestedText);
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
        await transaction.CommitAsync(cancellationToken);
        var reviewCount = proposal.ReviewItems.Count + paddle.Words.Count(word => word.Confidence < .75);
        await UpdateProofreadingStatusAsync(pageId, reviewCount == 0 ? ProofreadingStatus.Draft : ProofreadingStatus.ReviewRequired, cancellationToken);
        var review = _connection.CreateCommand();
        review.CommandText = "UPDATE pages SET review_item_count = $count WHERE id = $pageId;";
        review.Parameters.AddWithValue("$count", reviewCount);
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
        confirmed.CommandText = "SELECT text, created_utc, source FROM page_text_versions WHERE page_id = $pageId AND kind = 'Confirmed' ORDER BY created_utc DESC LIMIT 1;";
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

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
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

    private async Task SaveOcrRunAsync(SqliteTransaction transaction, Guid pageId, string engine, string modelVersion, IReadOnlyList<OcrWord> words, DateTimeOffset executedAt, IReadOnlySet<OcrWord>? rubyCandidates, CancellationToken cancellationToken)
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
            insert.CommandText = "INSERT INTO ocr_run_words (run_id, ordinal, text, confidence, left_x, top_y, right_x, bottom_y, role, included_in_draft, coordinate_status) VALUES ($run, $ordinal, $text, $confidence, $left, $top, $right, $bottom, $role, $included, 'Known');";
            insert.Parameters.AddWithValue("$run", runId.ToString("D"));
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.Parameters.AddWithValue("$text", word.Text);
            insert.Parameters.AddWithValue("$confidence", word.Confidence);
            insert.Parameters.AddWithValue("$left", word.Left);
            insert.Parameters.AddWithValue("$top", word.Top);
            insert.Parameters.AddWithValue("$right", word.Right);
            insert.Parameters.AddWithValue("$bottom", word.Bottom);
            var isRubyCandidate = rubyCandidates?.Contains(word) == true;
            insert.Parameters.AddWithValue("$role", isRubyCandidate ? "RubyCandidate" : "Body");
            insert.Parameters.AddWithValue("$included", isRubyCandidate ? 0 : 1);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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
                printed_page_number TEXT NULL, proofreading_status TEXT NOT NULL DEFAULT 'NotOcrProcessed', review_item_count INTEGER NOT NULL DEFAULT 0
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
                coordinate_status TEXT NOT NULL, PRIMARY KEY (run_id, ordinal)
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
                kind TEXT NOT NULL, text TEXT NOT NULL, created_utc TEXT NOT NULL, source TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS project_metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS proofreading_exports (
                batch_id TEXT PRIMARY KEY NOT NULL, project_id TEXT NOT NULL, exported_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS proofreading_export_pages (
                batch_id TEXT NOT NULL REFERENCES proofreading_exports(batch_id) ON DELETE CASCADE,
                page_id TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                page_marker TEXT NOT NULL, PRIMARY KEY (batch_id, page_marker)
            );
            CREATE TABLE IF NOT EXISTS proofreading_imports (
                batch_id TEXT NOT NULL REFERENCES proofreading_exports(batch_id) ON DELETE CASCADE,
                imported_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        foreach (var statement in new[]
        {
            "ALTER TABLE pages ADD COLUMN crop_left REAL NOT NULL DEFAULT 0;", "ALTER TABLE pages ADD COLUMN crop_top REAL NOT NULL DEFAULT 0;", "ALTER TABLE pages ADD COLUMN crop_right REAL NOT NULL DEFAULT 1;", "ALTER TABLE pages ADD COLUMN crop_bottom REAL NOT NULL DEFAULT 1;",
            "ALTER TABLE pages ADD COLUMN display_profile TEXT NOT NULL DEFAULT 'ReflowVertical';", "ALTER TABLE pages ADD COLUMN page_role TEXT NOT NULL DEFAULT 'Body';", "ALTER TABLE pages ADD COLUMN printed_page_number TEXT NULL;", "ALTER TABLE pages ADD COLUMN proofreading_status TEXT NOT NULL DEFAULT 'NotOcrProcessed';", "ALTER TABLE pages ADD COLUMN review_item_count INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE ocr_words ADD COLUMN coordinate_status TEXT NOT NULL DEFAULT 'Known';"
        })
        {
            try { var migration = _connection.CreateCommand(); migration.Transaction = transaction; migration.CommandText = statement; await migration.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqliteException) { }
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
        var version = _connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "UPDATE schema_version SET version = 1;";
        await version.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
