using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class PageImportMergerTests
{
    [Fact]
    public void Append_new_images_keeps_existing_pages_and_skips_reimported_hashes()
    {
        var existing = new ProjectPage(Guid.NewGuid(), "existing.png", "C:\\images\\existing.png", "same", 0, true, 0);
        var duplicate = new ProjectPage(Guid.NewGuid(), "duplicate.png", "C:\\images\\duplicate.png", "same", 0, true, 0);
        var added = new ProjectPage(Guid.NewGuid(), "added.png", "C:\\images\\added.png", "new", 1, true, 0);

        var result = PageImportMerger.AppendNew([existing], [duplicate, added]);

        Assert.Equal([existing.Id, added.Id], result.Select(page => page.Id));
        Assert.Equal([0, 1], result.Select(page => page.SortOrder));
    }
}
