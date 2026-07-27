using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class OcrRunPlannerTests
{
    [Fact]
    public void Resume_incomplete_targets_only_included_incomplete_pages_in_sort_order()
    {
        var pages = new[]
        {
            Page("completed.png", 5, true, OcrStatus.Completed),
            Page("failed.png", 3, true, OcrStatus.Failed),
            Page("processing.png", 1, true, OcrStatus.Processing),
            Page("not-processed.png", 2, true, OcrStatus.NotProcessed),
            Page("review.png", 4, true, OcrStatus.ReviewRequired),
            Page("excluded.png", 0, false, OcrStatus.Failed),
        };

        var plan = OcrRunPlanner.Plan(OcrRunMode.ResumeIncomplete, pages);

        Assert.Equal(new[] { "processing.png", "not-processed.png", "failed.png" }, plan.Targets.Select(page => page.FileName));
        Assert.Equal(1, plan.CompletedSkippedCount);
        Assert.Equal(1, plan.ReviewRequiredSkippedCount);
        Assert.Equal(1, plan.ExcludedSkippedCount);
        Assert.Equal(1, plan.FailedTargetCount);
        Assert.Equal(1, plan.ProcessingTargetCount);
        Assert.Equal(1, plan.NotProcessedTargetCount);
    }

    [Fact]
    public void Selected_targets_the_selected_page_even_when_it_is_excluded_or_completed()
    {
        var selected = Page("selected.png", 2, false, OcrStatus.Completed);

        var plan = OcrRunPlanner.Plan(OcrRunMode.Selected, [selected]);

        Assert.Equal(new[] { selected }, plan.Targets);
        Assert.Equal(0, plan.ExcludedSkippedCount);
        Assert.Equal(0, plan.CompletedSkippedCount);
    }

    [Fact]
    public void Reprocess_all_preserves_the_original_all_page_behavior_in_sort_order()
    {
        var pages = new[]
        {
            Page("review.png", 3, true, OcrStatus.ReviewRequired),
            Page("excluded.png", 0, false, OcrStatus.NotProcessed),
            Page("completed.png", 2, true, OcrStatus.Completed),
            Page("failed.png", 1, true, OcrStatus.Failed),
        };

        var plan = OcrRunPlanner.Plan(OcrRunMode.ReprocessAll, pages);

        Assert.Equal(new[] { "excluded.png", "failed.png", "completed.png", "review.png" }, plan.Targets.Select(page => page.FileName));
        Assert.Equal(0, plan.ExcludedSkippedCount);
        Assert.Equal(0, plan.CompletedSkippedCount);
        Assert.Equal(0, plan.ReviewRequiredSkippedCount);
        Assert.Equal(1, plan.FailedTargetCount);
        Assert.Equal(0, plan.SkippedCount);
    }

    [Fact]
    public void Resume_incomplete_can_have_zero_targets_without_selecting_completed_or_review_pages()
    {
        var plan = OcrRunPlanner.Plan(
            OcrRunMode.ResumeIncomplete,
            [Page("done.png", 0, true, OcrStatus.Completed), Page("review.png", 1, true, OcrStatus.ReviewRequired)]);

        Assert.Empty(plan.Targets);
        Assert.Equal(1, plan.CompletedSkippedCount);
        Assert.Equal(1, plan.ReviewRequiredSkippedCount);
        Assert.Equal(2, plan.SkippedCount);
    }

    private static ProjectPage Page(string fileName, int sortOrder, bool isIncluded, OcrStatus status) =>
        new(Guid.NewGuid(), fileName, fileName, "hash", sortOrder, isIncluded, 0, OcrStatus: status);
}
