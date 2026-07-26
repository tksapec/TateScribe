namespace TateScribe.Core.Ruby;

public sealed record RubyPackagePage(
    Guid PageId,
    string PageMarker,
    string OriginalImagePath,
    string? CroppedImagePath);

public sealed record RubyOcrCandidate(
    string PageMarker,
    string OcrText,
    double Left,
    double Top,
    double Right,
    double Bottom,
    double Confidence,
    string AdjacentBodyText,
    Guid OcrRunId,
    bool ReturnedToBody,
    bool IncludedInDraft);

public sealed record RubyPackageRequest(
    Guid ProjectId,
    Guid BatchId,
    RubyPolicy RubyPolicy,
    StructuredDocument Document,
    IReadOnlyList<RubyPackagePage> Pages,
    IReadOnlyList<RubyOcrCandidate> Candidates,
    string DestinationPath);
