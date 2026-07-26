namespace TateScribe.Core.Ocr;

public enum OcrFailureStage
{
    Preprocess,
    PaddleOCR,
    Tesseract,
    Merge,
    DatabaseSave
}

public sealed record OcrFailure(
    Guid Id,
    Guid PageId,
    string FileName,
    OcrFailureStage Stage,
    string ExceptionType,
    string Message,
    bool Retryable,
    bool WasCancelled,
    DateTimeOffset OccurredAt);
