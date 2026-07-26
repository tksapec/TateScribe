using TateScribe.Core.Projects;

namespace TateScribe.Core.Export;

public static class DocumentPageSelection
{
    public static bool IncludeInDocx(PageRole role, string text) =>
        role switch
        {
            PageRole.Illustration or PageRole.Blank => false,
            PageRole.Other => !string.IsNullOrWhiteSpace(text),
            _ => !string.IsNullOrWhiteSpace(text)
        };
}
