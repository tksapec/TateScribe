using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class PageOrderEditorTests
{
    [Fact]
    public void Move_moves_selected_page_and_reassigns_contiguous_orders()
    {
        var pages = new[]
        {
            Page("first", 0), Page("second", 1), Page("third", 2)
        };

        var reordered = PageOrderEditor.Move(pages, pages[2].Id, -1);

        Assert.Equal(["first", "third", "second"], reordered.Select(x => x.FileName));
        Assert.Equal([0, 1, 2], reordered.Select(x => x.SortOrder));
    }

    private static ProjectPage Page(string name, int order) => new(Guid.NewGuid(), name, name, name, order, true, 0);
}
