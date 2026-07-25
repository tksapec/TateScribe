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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
