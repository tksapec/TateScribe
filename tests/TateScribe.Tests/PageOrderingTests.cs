using TateScribe.Core.Pages;

namespace TateScribe.Tests;

public sealed class PageOrderingTests
{
    [Fact]
    public void Sort_uses_embedded_filename_timestamp_before_other_metadata()
    {
        var laterModified = new PageSortCandidate("IMG_20260725_083202.png", null, null, new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero), null);
        var earlierEmbedded = new PageSortCandidate("IMG_20260725_083157.png", null, null, new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero), null);

        var ordered = PageOrdering.Sort([laterModified, earlierEmbedded]);

        Assert.Equal("IMG_20260725_083157.png", ordered[0].FileName);
    }

    [Fact]
    public void Sort_falls_back_to_natural_filename_order()
    {
        var ordered = PageOrdering.Sort([
            new PageSortCandidate("page-10.png", null, null, null, null),
            new PageSortCandidate("page-2.png", null, null, null, null)
        ]);

        Assert.Equal(["page-2.png", "page-10.png"], ordered.Select(x => x.FileName));
    }
}
