namespace TateScribe.Core.Projects;

using TateScribe.Core.Images;
using TateScribe.Core.Proofreading;

public enum DisplayProfile
{
    ReflowVertical,
    FixedPageVertical
}

public enum PageRole
{
    Body,
    ChapterTitle,
    MixedTitleAndBody,
    Illustration,
    Blank,
    Other
}

public enum ProofreadingStatus
{
    NotOcrProcessed,
    Draft,
    ManuallyEdited,
    ExportedForProofreading,
    Confirmed,
    ReviewRequired,
    Stale
}

public enum OcrStatus
{
    NotProcessed,
    Processing,
    Completed,
    Failed,
    ReviewRequired
}

public sealed record ProjectPage(
    Guid Id,
    string FileName,
    string SourcePath,
    string SourceHash,
    int SortOrder,
    bool IsIncluded,
    int RotationDegrees,
    NormalizedCrop? Crop = null,
    DisplayProfile DisplayProfile = DisplayProfile.ReflowVertical,
    PageRole PageRole = PageRole.Body,
    string? PrintedPageNumber = null,
    ProofreadingStatus ProofreadingStatus = ProofreadingStatus.NotOcrProcessed,
    int ReviewItemCount = 0,
    OcrStatus OcrStatus = OcrStatus.NotProcessed,
    BoundaryJoinType BoundaryJoinType = BoundaryJoinType.DirectJoin)
{
    public string DisplayLabel => $"{SortOrder + 1:0000} {FileName} | OCR:{OcrStatus} | 校正:{ProofreadingStatus} | {DisplayProfile} | {PageRole} | 要確認 {ReviewItemCount}";
}
