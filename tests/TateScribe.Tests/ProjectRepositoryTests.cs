using TateScribe.Core.Projects;
using TateScribe.Core.Ocr;
using TateScribe.Core.Images;
using TateScribe.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

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
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "project.db")}"))
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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
