using TateScribe.Core.Images;
using TateScribe.Infrastructure.Images;

namespace TateScribe.Tests;

public sealed class ScreenshotPreprocessorTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Prepare_creates_deterministic_cache_without_changing_source()
    {
        var source = Path.Combine(FindRepositoryRoot(), "testdata", "IMG_20260725_083157.png");
        var originalHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(File.OpenRead(source)));

        var first = await new ScreenshotPreprocessor().PrepareAsync(source, _cacheDirectory, NormalizedCrop.Full, 0, CancellationToken.None);
        var second = await new ScreenshotPreprocessor().PrepareAsync(source, _cacheDirectory, NormalizedCrop.Full, 0, CancellationToken.None);

        Assert.True(File.Exists(first.CachePath));
        Assert.Equal(first.CachePath, second.CachePath);
        Assert.Equal(originalHash, Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(File.OpenRead(source))));
        Assert.True(first.Width > 0);
        Assert.True(first.Height > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
