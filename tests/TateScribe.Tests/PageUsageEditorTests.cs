using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class PageUsageEditorTests
{
    [Fact]
    public void Toggle_flips_usage_without_changing_page_identity_or_order()
    {
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "page.png", "hash", 3, true, 0);

        var updated = PageUsageEditor.Toggle(page);

        Assert.False(updated.IsIncluded);
        Assert.Equal(page.Id, updated.Id);
        Assert.Equal(3, updated.SortOrder);
    }
}
