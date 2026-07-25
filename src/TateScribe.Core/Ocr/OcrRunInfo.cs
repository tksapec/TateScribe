namespace TateScribe.Core.Ocr;

public sealed record OcrRunInfo(Guid Id, Guid PageId, string Engine, string ModelVersion, DateTimeOffset ExecutedAt, int WordCount);
