using TateScribe.Core.Ruby;

namespace TateScribe.Tests;

public sealed class RubyReviewSelectionServiceTests
{
    [Fact]
    public void Invalid_selected_item_leaves_every_selected_status_unchanged()
    {
        var valid = Proposal("p1", 0, 2, "本文", "ほんぶん");
        var invalid = Proposal("p2", 0, 2, "不一致", "ふいっち");

        var result = RubyReviewSelectionService.ApplyStatus(
            [valid, invalid],
            RubyAnnotationStatus.Confirmed,
            paragraphId => paragraphId == "p1" ? "本文です" : "対象です");

        Assert.False(result.IsSuccess);
        Assert.All(result.Items, item => Assert.Equal(RubyAnnotationStatus.Proposed, item.Status));
        Assert.Single(result.Errors);
        Assert.Equal("p2:0:2", result.Errors[0].Key);
        Assert.Equal("BASE_TEXT_MISMATCH", result.Errors[0].Code);
    }

    [Fact]
    public void Explicit_selection_can_confirm_warning_candidate()
    {
        var warningCandidate = Proposal("p1", 0, 2, "本文", "ほんぶん");

        var result = RubyReviewSelectionService.ApplyStatus(
            [warningCandidate],
            RubyAnnotationStatus.Confirmed,
            _ => "本文です");

        Assert.True(result.IsSuccess);
        Assert.Equal(RubyAnnotationStatus.Confirmed, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void Noncontiguous_selection_changes_only_selected_snapshots()
    {
        var first = Proposal("p1", 0, 1, "甲", "こう");
        var unselected = Proposal("p1", 1, 1, "乙", "おつ");
        var third = Proposal("p1", 2, 1, "丙", "へい");

        var result = RubyReviewSelectionService.ApplyStatus(
            [first, third],
            RubyAnnotationStatus.Confirmed,
            _ => "甲乙丙");

        Assert.True(result.IsSuccess);
        Assert.All(result.Items, item => Assert.Equal(RubyAnnotationStatus.Confirmed, item.Status));
        Assert.Equal(RubyAnnotationStatus.Proposed, unselected.Status);
    }

    [Fact]
    public void Selected_items_can_be_rejected_atomically()
    {
        var first = Proposal("p1", 0, 1, "甲", "こう");
        var second = Proposal("p1", 1, 1, "乙", "おつ");

        var result = RubyReviewSelectionService.ApplyStatus(
            [first, second],
            RubyAnnotationStatus.Rejected,
            _ => "甲乙");

        Assert.True(result.IsSuccess);
        Assert.All(result.Items, item => Assert.Equal(RubyAnnotationStatus.Rejected, item.Status));
    }

    [Fact]
    public void Zero_selection_is_a_successful_no_op()
    {
        var result = RubyReviewSelectionService.ApplyStatus(
            [],
            RubyAnnotationStatus.Confirmed,
            _ => throw new InvalidOperationException("No paragraph lookup is expected."));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void Range_cannot_split_a_utf16_surrogate_pair(int start, int length)
    {
        var proposal = Proposal("p1", start, length, start == 1 ? "\ud83d" : "\ude00", "えがお");

        var result = RubyReviewSelectionService.ApplyStatus(
            [proposal],
            RubyAnnotationStatus.Confirmed,
            _ => "A😀B");

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_UTF16_BOUNDARY", Assert.Single(result.Errors).Code);
        Assert.Equal(RubyAnnotationStatus.Proposed, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void Empty_or_whitespace_reading_is_invalid()
    {
        var proposal = Proposal("p1", 0, 1, "甲", "  ");

        var result = RubyReviewSelectionService.ApplyStatus(
            [proposal],
            RubyAnnotationStatus.Confirmed,
            _ => "甲");

        Assert.False(result.IsSuccess);
        Assert.Equal("EMPTY_READING", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Missing_paragraph_and_invalid_range_are_reported_by_stable_keys()
    {
        var annotationId = Guid.NewGuid();
        var missing = Proposal("missing", 0, 1, "甲", "こう") with { AnnotationId = annotationId };
        var invalidRange = Proposal("p1", -1, 1, "乙", "おつ");

        var result = RubyReviewSelectionService.ApplyStatus(
            [missing, invalidRange],
            RubyAnnotationStatus.Confirmed,
            paragraphId => paragraphId == "p1" ? "乙" : null);

        Assert.False(result.IsSuccess);
        Assert.Collection(
            result.Errors,
            error =>
            {
                Assert.Equal(annotationId.ToString("D"), error.Key);
                Assert.Equal("PARAGRAPH_NOT_FOUND", error.Code);
            },
            error =>
            {
                Assert.Equal("p1:-1:1", error.Key);
                Assert.Equal("INVALID_RANGE", error.Code);
            });
    }

    [Fact]
    public void Bulk_summary_groups_exclusions_by_warning_code_without_relaxing_policy()
    {
        var eligible = Proposal("p1", 0, 1, "甲", "こう", RubySource.ImageConfirmed);
        var already = Proposal("p1", 1, 1, "乙", "おつ", RubySource.ImageConfirmed)
            with { Status = RubyAnnotationStatus.Confirmed };
        var wrongSource = Proposal("p1", 2, 1, "丙", "へい", RubySource.TextConfirmed);
        var lowConfidence = Proposal("p1", 3, 1, "丁", "てい", RubySource.ImageConfirmed)
            with { Confidence = RubyBulkConfirmationPolicy.MinBulkConfirmAnnotationConfidence - .01 };
        var warningOne = Proposal("p1", 4, 1, "戊", "ぼ", RubySource.ImageConfirmed);
        var warningTwo = Proposal("p1", 5, 1, "己", "き", RubySource.ImageConfirmed);
        var warnings = new Dictionary<int, IReadOnlyList<RubyValidationIssue>>
        {
            [4] =
            [
                new RubyValidationIssue(
                    "RUBY_WARNING", "Review this candidate.", false, "p1", 4, 1),
            ],
            [5] =
            [
                new RubyValidationIssue(
                    "RUBY_WARNING", "Review this candidate too.", false, "p1", 5, 1),
                new RubyValidationIssue(
                    "SECOND_WARNING", "Another reason.", false, "p1", 5, 1),
            ],
        };

        var summary = RubyBulkConfirmationSummary.Create(
            [eligible, already, wrongSource, lowConfidence, warningOne, warningTwo],
            RubySource.ImageConfirmed,
            proposal => warnings.GetValueOrDefault(proposal.Start, []));

        Assert.Equal(6, summary.Examined);
        Assert.Equal(1, summary.NewlyConfirmed);
        Assert.Equal(1, summary.AlreadyConfirmed);
        Assert.Equal(1, summary.WrongSource);
        Assert.Equal(3, summary.Excluded);
        Assert.Equal(1, summary.ExcludedByReason["LOW_ANNOTATION_CONFIDENCE"]);
        Assert.Equal(2, summary.ExcludedByReason["RUBY_WARNING"]);
        Assert.Equal(1, summary.ExcludedByReason["SECOND_WARNING"]);
        Assert.Empty(summary.ValidationErrors);
    }

    [Fact]
    public void Validation_errors_report_an_aborted_bulk_attempt_without_claiming_confirmation()
    {
        var eligible = Proposal("p1", 0, 1, "甲", "こう", RubySource.ImageConfirmed);
        var validationError = new RubyValidationIssue("DOCUMENT_ERROR", "Document is invalid.", true);

        var summary = RubyBulkConfirmationSummary.Create(
            [eligible],
            RubySource.ImageConfirmed,
            _ => [],
            [validationError]);

        Assert.Equal(1, summary.Examined);
        Assert.Equal(0, summary.NewlyConfirmed);
        Assert.Equal(1, summary.Excluded);
        Assert.Equal(1, summary.ExcludedByReason["VALIDATION_ABORTED"]);
        Assert.Equal([validationError], summary.ValidationErrors);
    }

    private static RubyAnnotationProposal Proposal(
        string paragraphId,
        int start,
        int length,
        string baseText,
        string reading,
        RubySource source = RubySource.UserConfirmed) =>
        new(
            paragraphId,
            start,
            length,
            baseText,
            reading,
            source,
            .90,
            ["p1"],
            "evidence");
}
