using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class DuplicatePageDetectorTests
{
    [Fact]
    public void FindCandidates_reports_equal_source_hash_without_removing_pages()
    {
        var pages = new[]
        {
            Page("first", "same", 0), Page("second", "same", 1), Page("third", "other", 2)
        };

        var candidates = DuplicatePageDetector.FindCandidates(pages);

        var candidate = Assert.Single(candidates);
        Assert.Equal("first", candidate.First.FileName);
        Assert.Equal("second", candidate.Second.FileName);
        Assert.Equal(3, pages.Length);
    }

    private static ProjectPage Page(string name, string hash, int order) => new(Guid.NewGuid(), name, name, hash, order, true, 0);
}
