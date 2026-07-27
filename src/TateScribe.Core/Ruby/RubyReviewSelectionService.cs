using System.Collections.ObjectModel;

namespace TateScribe.Core.Ruby;

public sealed record RubyReviewSelectionError(
    string Key,
    string Code,
    string Message,
    Guid AnnotationId,
    string ParagraphId,
    int Start,
    int Length);

public sealed record RubyReviewSelectionResult(
    IReadOnlyList<RubyAnnotationProposal> Items,
    IReadOnlyList<RubyReviewSelectionError> Errors)
{
    public bool IsSuccess => Errors.Count == 0;
}

public static class RubyReviewPendingEditBoundary
{
    public static bool TryCommit(
        Func<bool> commitCell,
        Func<bool> commitRow)
    {
        ArgumentNullException.ThrowIfNull(commitCell);
        ArgumentNullException.ThrowIfNull(commitRow);
        return commitCell() && commitRow();
    }

    public static bool TryRun(
        Func<bool> commitPendingEdits,
        Action action)
    {
        ArgumentNullException.ThrowIfNull(commitPendingEdits);
        ArgumentNullException.ThrowIfNull(action);
        if (!commitPendingEdits()) return false;
        action();
        return true;
    }
}

public static class RubyReviewSelectionService
{
    public static RubyReviewSelectionResult ApplyStatus(
        IReadOnlyList<RubyAnnotationProposal> selected,
        RubyAnnotationStatus status,
        Func<string, string?> paragraphTextFor)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(paragraphTextFor);

        var errors = new List<RubyReviewSelectionError>();
        foreach (var proposal in selected)
        {
            if (string.IsNullOrWhiteSpace(proposal.Reading))
                errors.Add(Error(proposal, "EMPTY_READING", "Ruby reading must not be empty."));

            var paragraphText = paragraphTextFor(proposal.ParagraphId);
            if (paragraphText is null)
            {
                errors.Add(Error(proposal, "PARAGRAPH_NOT_FOUND", "The source paragraph was not found."));
                continue;
            }

            if (proposal.Start < 0
                || proposal.Length < 1
                || (long)proposal.Start + proposal.Length > paragraphText.Length)
            {
                errors.Add(Error(proposal, "INVALID_RANGE", "Ruby range is outside the source paragraph."));
                continue;
            }

            var end = proposal.Start + proposal.Length;
            if (SplitsSurrogatePair(paragraphText, proposal.Start)
                || SplitsSurrogatePair(paragraphText, end))
            {
                errors.Add(Error(
                    proposal,
                    "INVALID_UTF16_BOUNDARY",
                    "Ruby range must not split a UTF-16 surrogate pair."));
                continue;
            }

            if (!string.Equals(
                    paragraphText.Substring(proposal.Start, proposal.Length),
                    proposal.BaseText,
                    StringComparison.Ordinal))
                errors.Add(Error(
                    proposal,
                    "BASE_TEXT_MISMATCH",
                    "Ruby base text does not match the source paragraph."));
        }

        var immutableItems = selected.ToArray();
        if (errors.Count > 0)
            return new RubyReviewSelectionResult(immutableItems, errors.ToArray());

        return new RubyReviewSelectionResult(
            immutableItems.Select(item => item with { Status = status }).ToArray(),
            []);
    }

    private static bool SplitsSurrogatePair(string text, int boundary) =>
        boundary > 0
        && boundary < text.Length
        && char.IsHighSurrogate(text[boundary - 1])
        && char.IsLowSurrogate(text[boundary]);

    private static RubyReviewSelectionError Error(
        RubyAnnotationProposal proposal,
        string code,
        string message) =>
        new(
            proposal.AnnotationId == Guid.Empty
                ? $"{proposal.ParagraphId}:{proposal.Start}:{proposal.Length}"
                : proposal.AnnotationId.ToString("D"),
            code,
            message,
            proposal.AnnotationId,
            proposal.ParagraphId,
            proposal.Start,
            proposal.Length);
}

public sealed record RubyBulkConfirmationSummary(
    int Examined,
    int NewlyConfirmed,
    int AlreadyConfirmed,
    int WrongSource,
    int Excluded,
    IReadOnlyDictionary<string, int> ExcludedByReason,
    IReadOnlyList<RubyValidationIssue> ValidationErrors)
{
    public static RubyBulkConfirmationSummary Create(
        IReadOnlyList<RubyAnnotationProposal> annotations,
        RubySource requestedSource,
        Func<RubyAnnotationProposal, IReadOnlyList<RubyValidationIssue>> issuesFor,
        IReadOnlyList<RubyValidationIssue>? validationErrors = null)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(issuesFor);

        var newlyConfirmed = 0;
        var alreadyConfirmed = 0;
        var wrongSource = 0;
        var excluded = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var immutableValidationErrors = (validationErrors ?? []).ToArray();

        foreach (var annotation in annotations)
        {
            if (annotation.Source != requestedSource)
            {
                wrongSource++;
                continue;
            }
            if (annotation.Status == RubyAnnotationStatus.Confirmed)
            {
                alreadyConfirmed++;
                continue;
            }
            if (immutableValidationErrors.Length > 0)
            {
                excluded++;
                reasons["VALIDATION_ABORTED"] = reasons.GetValueOrDefault("VALIDATION_ABORTED") + 1;
                continue;
            }

            var issues = issuesFor(annotation);
            if (RubyBulkConfirmationPolicy.CanConfirm(annotation, requestedSource, issues))
            {
                newlyConfirmed++;
                continue;
            }

            excluded++;
            var itemReasons = ExclusionReasons(annotation, issues);
            foreach (var reason in itemReasons)
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
        }

        return new RubyBulkConfirmationSummary(
            annotations.Count,
            newlyConfirmed,
            alreadyConfirmed,
            wrongSource,
            excluded,
            new ReadOnlyDictionary<string, int>(reasons),
            immutableValidationErrors);
    }

    private static IReadOnlySet<string> ExclusionReasons(
        RubyAnnotationProposal annotation,
        IReadOnlyList<RubyValidationIssue> issues)
    {
        var reasons = issues
            .Select(issue => issue.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);
        if (annotation.Confidence < RubyBulkConfirmationPolicy.MinBulkConfirmAnnotationConfidence)
            reasons.Add("LOW_ANNOTATION_CONFIDENCE");
        if (annotation.Status is RubyAnnotationStatus.Stale or RubyAnnotationStatus.Rejected)
            reasons.Add("STATUS_NOT_ELIGIBLE");
        if (annotation.EvidencePageMarkers.Count == 0)
            reasons.Add("MISSING_EVIDENCE_PAGE");
        if (string.IsNullOrWhiteSpace(annotation.Evidence))
            reasons.Add("MISSING_EVIDENCE");
        if (reasons.Count == 0)
            reasons.Add("POLICY_EXCLUDED");
        return reasons;
    }
}
