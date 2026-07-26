namespace TateScribe.Core.Proofreading;

public enum ProofreadingPackageFormat
{
    Zip,
    Directory
}

public enum BoundaryJoinType
{
    DirectJoin,
    SpaceJoin,
    ParagraphBreak,
    SceneBreak,
    Uncertain
}

public sealed record ProofreadingReviewItem(string Code, string Message, string Text);

public sealed record ProofreadingPackagePage(
    Guid ProjectPageId,
    int SortOrder,
    string SourceFileName,
    string SourceFileHash,
    string OriginalImagePath,
    string? CroppedImagePath,
    string MachineText,
    string? SuggestedText,
    int LowConfidenceCount,
    string PageRole,
    string DisplayProfile,
    IReadOnlyList<ProofreadingReviewItem>? ReviewItems = null,
    string? ManualText = null,
    string? ConfirmedText = null,
    BoundaryJoinType JoinToNext = BoundaryJoinType.DirectJoin)
{
    public (string Text, string Source) SelectText() =>
        ConfirmedText is not null ? (ConfirmedText, "Confirmed")
        : ManualText is not null ? (ManualText, "Manual")
        : SuggestedText is not null ? (SuggestedText, "Suggested")
        : (MachineText, "RawPaddle");
}

public sealed record ProofreadingPackageRequest(
    Guid ProjectId,
    string ProjectName,
    Guid BatchId,
    string DestinationPath,
    ProofreadingPackageFormat Format,
    IReadOnlyList<ProofreadingPackagePage> Pages);

public sealed record ProofreadingImportPage(
    string PageMarker,
    string ConfirmedText,
    BoundaryJoinType JoinToNext = BoundaryJoinType.DirectJoin);

public sealed record ProofreadingImportDocument(
    int FormatVersion,
    Guid ProjectId,
    Guid BatchId,
    IReadOnlyList<ProofreadingImportPage> Pages,
    string Report = "");

public sealed record ProofreadingImportIssue(string Code, string Message, string? PageMarker, bool IsError);

public sealed record ProofreadingImportCandidate(
    Guid PageId,
    string PageMarker,
    string ConfirmedText,
    string BaselineText = "",
    ProofreadingDiffResult? Diff = null,
    BoundaryJoinType JoinToNext = BoundaryJoinType.DirectJoin);

public sealed record ProofreadingImportPreview(
    ProofreadingImportDocument Document,
    IReadOnlyList<ProofreadingImportCandidate> Candidates,
    IReadOnlyList<ProofreadingImportIssue> Issues);
