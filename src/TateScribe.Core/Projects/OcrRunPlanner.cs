namespace TateScribe.Core.Projects;

public enum OcrRunMode
{
    Selected,
    ResumeIncomplete,
    ReprocessAll
}

public sealed record OcrRunPlan(
    IReadOnlyList<ProjectPage> Targets,
    int CompletedSkippedCount,
    int ReviewRequiredSkippedCount,
    int ExcludedSkippedCount,
    int FailedTargetCount,
    int ProcessingTargetCount,
    int NotProcessedTargetCount);

public static class OcrPageSelectionPolicy
{
    public static bool IsTarget(OcrRunMode mode, ProjectPage page) => mode switch
    {
        OcrRunMode.Selected => true,
        OcrRunMode.ResumeIncomplete => page.IsIncluded && page.OcrStatus is OcrStatus.NotProcessed or OcrStatus.Failed or OcrStatus.Processing,
        OcrRunMode.ReprocessAll => page.IsIncluded,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

public static class OcrRunPlanner
{
    public static OcrRunPlan Plan(OcrRunMode mode, IEnumerable<ProjectPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var source = pages.ToArray();
        var targets = source
            .Where(page => OcrPageSelectionPolicy.IsTarget(mode, page))
            .OrderBy(page => page.SortOrder)
            .ToArray();

        return new OcrRunPlan(
            targets,
            SkippedCount(OcrStatus.Completed),
            SkippedCount(OcrStatus.ReviewRequired),
            mode == OcrRunMode.Selected ? 0 : source.Count(page => !page.IsIncluded),
            targets.Count(page => page.OcrStatus == OcrStatus.Failed),
            targets.Count(page => page.OcrStatus == OcrStatus.Processing),
            targets.Count(page => page.OcrStatus == OcrStatus.NotProcessed));

        int SkippedCount(OcrStatus status) =>
            mode == OcrRunMode.ResumeIncomplete
                ? source.Count(page => page.IsIncluded && page.OcrStatus == status)
                : 0;
    }
}
