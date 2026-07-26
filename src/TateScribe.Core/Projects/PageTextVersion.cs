namespace TateScribe.Core.Projects;

public sealed record PageTextVersion(
    Guid PageId,
    string Kind,
    string Text,
    DateTimeOffset CreatedAt,
    string Source);
