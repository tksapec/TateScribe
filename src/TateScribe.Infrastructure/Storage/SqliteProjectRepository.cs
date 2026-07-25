using Microsoft.Data.Sqlite;
using TateScribe.Core.Projects;

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
        var clear = _connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM pages;";
        await clear.ExecuteNonQueryAsync(cancellationToken);
        foreach (var page in pages)
        {
            var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO pages (id, file_name, source_path, source_hash, sort_order, included, rotation_degrees) VALUES ($id, $name, $path, $hash, $order, $included, $rotation);";
            command.Parameters.AddWithValue("$id", page.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", page.FileName);
            command.Parameters.AddWithValue("$path", page.SourcePath);
            command.Parameters.AddWithValue("$hash", page.SourceHash);
            command.Parameters.AddWithValue("$order", page.SortOrder);
            command.Parameters.AddWithValue("$included", page.IsIncluded ? 1 : 0);
            command.Parameters.AddWithValue("$rotation", page.RotationDegrees);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPage>> LoadPagesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, file_name, source_path, source_hash, sort_order, included, rotation_degrees FROM pages ORDER BY sort_order;";
        var result = new List<ProjectPage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectPage(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) != 0, reader.GetInt32(6)));
        }
        return result;
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
                rotation_degrees INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
