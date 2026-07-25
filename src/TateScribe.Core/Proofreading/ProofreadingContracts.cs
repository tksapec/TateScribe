namespace TateScribe.Core.Proofreading;

public enum ProofreadingPackageFormat
{
    Zip,
    Directory
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
    IReadOnlyList<ProofreadingReviewItem>? ReviewItems = null);

public sealed record ProofreadingPackageRequest(
    Guid ProjectId,
    string ProjectName,
    Guid BatchId,
    string DestinationPath,
    ProofreadingPackageFormat Format,
    IReadOnlyList<ProofreadingPackagePage> Pages);

public sealed record ProofreadingImportPage(string PageMarker, string ConfirmedText);

public sealed record ProofreadingImportDocument(
    int FormatVersion,
    Guid ProjectId,
    Guid BatchId,
    IReadOnlyList<ProofreadingImportPage> Pages);

public sealed record ProofreadingImportIssue(string Code, string Message, string? PageMarker, bool IsError);

public sealed record ProofreadingImportCandidate(Guid PageId, string PageMarker, string ConfirmedText);

public sealed record ProofreadingImportPreview(
    ProofreadingImportDocument Document,
    IReadOnlyList<ProofreadingImportCandidate> Candidates,
    IReadOnlyList<ProofreadingImportIssue> Issues);
