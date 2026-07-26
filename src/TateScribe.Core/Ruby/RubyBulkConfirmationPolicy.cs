namespace TateScribe.Core.Ruby;

public static class RubyBulkConfirmationPolicy
{
    public static bool CanConfirm(
        RubyAnnotationProposal annotation,
        RubySource requestedSource,
        IReadOnlyList<RubyValidationIssue> issues)
    {
        if (requestedSource is not (RubySource.ImageConfirmed or RubySource.TextConfirmed)
            || annotation.Source != requestedSource
            || annotation.Confidence < 0.7
            || annotation.Status is RubyAnnotationStatus.Stale or RubyAnnotationStatus.Rejected
            || annotation.EvidencePageMarkers.Count == 0
            || string.IsNullOrWhiteSpace(annotation.Evidence))
            return false;
        return !issues.Any(issue => Matches(issue, annotation));
    }

    public static bool Matches(
        RubyValidationIssue issue,
        RubyAnnotationProposal annotation)
    {
        if (issue.AnnotationId is not null
            && annotation.AnnotationId != Guid.Empty)
            return issue.AnnotationId == annotation.AnnotationId;
        return string.Equals(
                issue.ParagraphId,
                annotation.ParagraphId,
                StringComparison.OrdinalIgnoreCase)
            && issue.Start == annotation.Start
            && issue.Length == annotation.Length;
    }
}
