namespace TateScribe.Core.Projects;

using TateScribe.Core.Images;

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
    int ReviewItemCount = 0)
{
    public string DisplayLabel => $"{SortOrder + 1:0000} {FileName} | {ProofreadingStatus} | {DisplayProfile} | {PageRole} | 要確認 {ReviewItemCount}";
}
