using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class PageValidationServiceTests
{
    [Fact]
    public void Validate_finds_duplicate_reversed_missing_and_non_numeric_printed_numbers()
    {
        var pages = new[]
        {
            Fixed(0, "10"),
            Fixed(1, "12"),
            Fixed(2, "12"),
            Fixed(3, "11"),
            Fixed(4, "十一")
        };

        var issues = PageValidationService.Validate(pages);

        Assert.Contains(issues, issue => issue.Code == "PrintedPageGap");
        Assert.Contains(issues, issue => issue.Code == "DuplicatePrintedPageNumber");
        Assert.Contains(issues, issue => issue.Code == "PrintedPageReversal");
        Assert.Contains(issues, issue => issue.Code == "NonNumericPrintedPageNumber");
    }

    [Fact]
    public void Validate_ignores_reflow_and_excluded_pages()
    {
        var reflow = Fixed(0, "x") with { DisplayProfile = DisplayProfile.ReflowVertical };
        var excluded = Fixed(1, "x") with { IsIncluded = false };

        Assert.Empty(PageValidationService.Validate([reflow, excluded]));
    }

    private static ProjectPage Fixed(int order, string? printed) =>
        new(Guid.NewGuid(), $"page-{order}.png", $"C:\\page-{order}.png", $"hash-{order}",
            order, true, 0, DisplayProfile: DisplayProfile.FixedPageVertical, PrintedPageNumber: printed);
}
