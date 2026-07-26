namespace TateScribe.Core.Projects;

public sealed record StoredReviewItem(
    Guid Id,
    Guid PageId,
    string Code,
    string Message,
    string Source,
    string? Text,
    DateTimeOffset CreatedAt);
