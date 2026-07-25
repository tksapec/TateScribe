using Microsoft.Data.Sqlite;
using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Core.Images;

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
                INSERT INTO pages (id, file_name, source_path, source_hash, sort_order, included, rotation_degrees, crop_left, crop_top, crop_right, crop_bottom)
                VALUES ($id, $name, $path, $hash, $order, $included, $rotation, $left, $top, $right, $bottom)
                ON CONFLICT(id) DO UPDATE SET
                    file_name = excluded.file_name,
                    source_path = excluded.source_path,
                    source_hash = excluded.source_hash,
                    sort_order = excluded.sort_order,
                    included = excluded.included,
                    rotation_degrees = excluded.rotation_degrees, crop_left = excluded.crop_left, crop_top = excluded.crop_top, crop_right = excluded.crop_right, crop_bottom = excluded.crop_bottom;
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
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPage>> LoadPagesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, file_name, source_path, source_hash, sort_order, included, rotation_degrees, crop_left, crop_top, crop_right, crop_bottom FROM pages ORDER BY sort_order;";
        var result = new List<ProjectPage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectPage(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) != 0, reader.GetInt32(6), new NormalizedCrop(reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9), reader.GetDouble(10))));
        }
        return result;
    }

    public async Task ReplaceOcrWordsAsync(Guid pageId, string engine, string modelVersion, IReadOnlyList<OcrWord> words, CancellationToken cancellationToken)
    {
        await using var transaction = _connection.BeginTransaction();
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
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveManualTextAsync(Guid pageId, string text, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO manual_page_text (page_id, text, updated_utc) VALUES ($pageId, $text, $utc) ON CONFLICT(page_id) DO UPDATE SET text = excluded.text, updated_utc = excluded.updated_utc;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PageTextState> LoadPageTextStateAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT engine, model_version, text, confidence, left_x, top_y, right_x, bottom_y FROM ocr_words WHERE page_id = $pageId ORDER BY ordinal;";
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var words = new List<OcrWord>();
        var engine = "none";
        var model = "none";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                engine = reader.GetString(0);
                model = reader.GetString(1);
                words.Add(new OcrWord(reader.GetString(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7)));
            }
        }
        var manual = _connection.CreateCommand();
        manual.CommandText = "SELECT text FROM manual_page_text WHERE page_id = $pageId;";
        manual.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        var manualText = await manual.ExecuteScalarAsync(cancellationToken) as string;
        return new PageTextState(pageId, manualText, engine, model, words);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
        await _connection.DisposeAsync();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS pages (
                id TEXT PRIMARY KEY NOT NULL,
                file_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                included INTEGER NOT NULL,
                rotation_degrees INTEGER NOT NULL,
                crop_left REAL NOT NULL DEFAULT 0, crop_top REAL NOT NULL DEFAULT 0,
                crop_right REAL NOT NULL DEFAULT 1, crop_bottom REAL NOT NULL DEFAULT 1
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
                PRIMARY KEY (page_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS manual_page_text (
                page_id TEXT PRIMARY KEY NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                text TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        foreach (var statement in new[] { "ALTER TABLE pages ADD COLUMN crop_left REAL NOT NULL DEFAULT 0;", "ALTER TABLE pages ADD COLUMN crop_top REAL NOT NULL DEFAULT 0;", "ALTER TABLE pages ADD COLUMN crop_right REAL NOT NULL DEFAULT 1;", "ALTER TABLE pages ADD COLUMN crop_bottom REAL NOT NULL DEFAULT 1;" })
        {
            try { var migration = _connection.CreateCommand(); migration.CommandText = statement; await migration.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqliteException) { }
        }
    }
}
