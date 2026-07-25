namespace TateScribe.Core.Projects;

using TateScribe.Core.Images;

public sealed record ProjectPage(
    Guid Id,
    string FileName,
    string SourcePath,
    string SourceHash,
    int SortOrder,
    bool IsIncluded,
    int RotationDegrees,
    NormalizedCrop? Crop = null);
