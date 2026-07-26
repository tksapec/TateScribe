using System.Security.Cryptography;
using TateScribe.Core.Pages;
using TateScribe.Core.Projects;

namespace TateScribe.Infrastructure.Import;

public sealed class ImageImporter
{
    public async Task<IReadOnlyList<ProjectPage>> ImportAsync(IEnumerable<string> sourcePaths, CancellationToken cancellationToken)
    {
        var candidates = new List<(PageSortCandidate Sort, string Path, string Hash)>();
        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(sourcePath);
            if (!file.Exists) throw new FileNotFoundException("Image source was not found.", sourcePath);
            if (!IsSupported(file.Extension)) throw new NotSupportedException($"Unsupported image type: {file.Extension}");
            candidates.Add((new PageSortCandidate(
                file.Name,
                ImageTimestampReader.TryRead(file.FullName),
                new DateTimeOffset(file.CreationTimeUtc),
                new DateTimeOffset(file.LastWriteTimeUtc),
                PageOrdering.GetFileNameTimestamp(file.Name)),
                file.FullName,
                await HashAsync(file.FullName, cancellationToken)));
        }

        var order = PageOrdering.Sort(candidates.Select(x => x.Sort));
        return order.Select((sort, index) =>
        {
            var candidate = candidates.Single(x => ReferenceEquals(x.Sort, sort));
            return new ProjectPage(Guid.NewGuid(), sort.FileName, candidate.Path, candidate.Hash, index, true, 0);
        }).ToArray();
    }

    private static bool IsSupported(string extension) => extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(source, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
