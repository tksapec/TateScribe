namespace TateScribe.Core.Ocr;

public sealed record OcrWordReviewState(
    Guid RunId,
    int Ordinal,
    OcrWord Word,
    string Role,
    bool IncludedInDraft,
    bool IsManualOverride,
    string AutomaticRole);
