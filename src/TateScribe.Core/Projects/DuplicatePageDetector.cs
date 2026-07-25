namespace TateScribe.Core.Projects;

public sealed record DuplicatePageCandidate(ProjectPage First, ProjectPage Second, string Reason);

public static class DuplicatePageDetector
{
    public static IReadOnlyList<DuplicatePageCandidate> FindCandidates(IEnumerable<ProjectPage> pages) =>
        pages.Where(page => !string.IsNullOrWhiteSpace(page.SourceHash))
            .GroupBy(page => page.SourceHash, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(page => page.SortOrder).Pairwise())
            .Select(pair => new DuplicatePageCandidate(pair.First, pair.Second, "Source file SHA-256 is identical."))
            .ToArray();

    private static IEnumerable<(ProjectPage First, ProjectPage Second)> Pairwise(this IEnumerable<ProjectPage> pages)
    {
        using var iterator = pages.GetEnumerator();
        if (!iterator.MoveNext()) yield break;
        var previous = iterator.Current;
        while (iterator.MoveNext())
        {
            yield return (previous, iterator.Current);
            previous = iterator.Current;
        }
    }
}
