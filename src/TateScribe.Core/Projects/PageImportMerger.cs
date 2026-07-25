namespace TateScribe.Core.Projects;

public static class PageImportMerger
{
    public static IReadOnlyList<ProjectPage> AppendNew(
        IReadOnlyList<ProjectPage> existingPages,
        IReadOnlyList<ProjectPage> importedPages)
    {
        var knownHashes = existingPages
            .Select(page => page.SourceHash)
            .ToHashSet(StringComparer.Ordinal);
        var result = existingPages.OrderBy(page => page.SortOrder).ToList();

        foreach (var page in importedPages.OrderBy(page => page.SortOrder))
        {
            if (knownHashes.Add(page.SourceHash)) result.Add(page);
        }

        return result.Select((page, index) => page with { SortOrder = index }).ToArray();
    }
}
