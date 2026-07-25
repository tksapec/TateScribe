namespace TateScribe.Core.Projects;

public static class PageUsageEditor
{
    public static ProjectPage Toggle(ProjectPage page) => page with { IsIncluded = !page.IsIncluded };
}
