using TateScribe.Infrastructure.Import;

namespace TateScribe.Tests;

public sealed class TestDataImportTests
{
    [Fact]
    public async Task Import_orders_each_sample_book_by_embedded_capture_time_and_hashes_sources()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "testdata"), "*.png");

        var pages = await new ImageImporter().ImportAsync(files, CancellationToken.None);

        Assert.Equal(20, pages.Count);
        Assert.Equal("IMG_20260505_132622.png", pages[0].FileName);
        Assert.Equal("IMG_20260725_083219.png", pages[^1].FileName);
        Assert.All(pages, page => Assert.Equal(64, page.SourceHash.Length));
        Assert.Equal(Enumerable.Range(0, 20), pages.Select(page => page.SortOrder));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
