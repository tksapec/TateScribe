namespace TateScribe.Core.Projects;

public sealed record PageValidationIssue(Guid PageId, string Code, string Message);

public static class PageValidationService
{
    public static IReadOnlyList<PageValidationIssue> Validate(IEnumerable<ProjectPage> pages)
    {
        var fixedPages = pages
            .Where(page => page.IsIncluded && page.DisplayProfile == DisplayProfile.FixedPageVertical)
            .OrderBy(page => page.SortOrder)
            .ToArray();
        var issues = new List<PageValidationIssue>();
        var numeric = new List<(ProjectPage Page, int Number)>();
        foreach (var page in fixedPages)
        {
            if (string.IsNullOrWhiteSpace(page.PrintedPageNumber)) continue;
            if (!int.TryParse(page.PrintedPageNumber, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                issues.Add(new PageValidationIssue(
                    page.Id, "NonNumericPrintedPageNumber",
                    $"印刷ページ番号「{page.PrintedPageNumber}」は数値ではありません。"));
                continue;
            }
            numeric.Add((page, value));
        }

        foreach (var duplicate in numeric.GroupBy(item => item.Number).Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
                issues.Add(new PageValidationIssue(
                    item.Page.Id, "DuplicatePrintedPageNumber",
                    $"印刷ページ番号 {duplicate.Key} が重複しています。"));
        }

        for (var index = 1; index < numeric.Count; index++)
        {
            var previous = numeric[index - 1];
            var current = numeric[index];
            if (current.Number < previous.Number)
                issues.Add(new PageValidationIssue(
                    current.Page.Id, "PrintedPageReversal",
                    $"印刷ページ番号が {previous.Number} から {current.Number} へ逆行しています。"));
            else if (current.Number > previous.Number + 1)
                issues.Add(new PageValidationIssue(
                    current.Page.Id, "PrintedPageGap",
                    $"印刷ページ番号 {previous.Number + 1}～{current.Number - 1} が欠落している可能性があります。"));
        }
        return issues;
    }
}
