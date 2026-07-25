namespace TateScribe.Core.Projects;

public static class PageOrderEditor
{
    public static IReadOnlyList<ProjectPage> Move(IReadOnlyList<ProjectPage> pages, Guid pageId, int offset)
    {
        var ordered = pages.OrderBy(page => page.SortOrder).ToList();
        var oldIndex = ordered.FindIndex(page => page.Id == pageId);
        if (oldIndex < 0) throw new ArgumentException("Page was not found.", nameof(pageId));
        var newIndex = Math.Clamp(oldIndex + offset, 0, ordered.Count - 1);
        if (newIndex != oldIndex) (ordered[oldIndex], ordered[newIndex]) = (ordered[newIndex], ordered[oldIndex]);
        return ordered.Select((page, index) => page with { SortOrder = index }).ToArray();
    }
}
