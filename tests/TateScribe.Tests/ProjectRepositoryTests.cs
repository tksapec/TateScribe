using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Storage;

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
            new ProjectPage(Guid.NewGuid(), "b.png", "C:\\images\\b.png", "hash-b", 1, true, 90),
            new ProjectPage(Guid.NewGuid(), "a.png", "C:\\images\\a.png", "hash-a", 0, true, 0)
        };

        await repository.SavePagesAsync(pages, CancellationToken.None);
        var loaded = await repository.LoadPagesAsync(CancellationToken.None);

        Assert.Equal(["a.png", "b.png"], loaded.Select(x => x.FileName));
        Assert.Equal("hash-b", loaded[1].SourceHash);
        Assert.Equal(90, loaded[1].RotationDegrees);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
