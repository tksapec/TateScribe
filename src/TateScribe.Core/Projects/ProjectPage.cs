namespace TateScribe.Core.Projects;

public sealed record ProjectPage(
    Guid Id,
    string FileName,
    string SourcePath,
    string SourceHash,
    int SortOrder,
    bool IsIncluded,
    int RotationDegrees);
