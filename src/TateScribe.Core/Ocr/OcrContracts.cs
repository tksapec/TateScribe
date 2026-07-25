namespace TateScribe.Core.Ocr;

public sealed record OcrRequest(string RequestId, string Engine, string ImagePath);

public sealed record OcrWord(string Text, double Confidence, double Left, double Top, double Right, double Bottom);

public sealed record OcrPageResult(string RequestId, string Engine, string ModelVersion, IReadOnlyList<OcrWord> Words);

public interface IOcrWorker : IAsyncDisposable
{
    Task<OcrPageResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);
}

public sealed class OcrWorkerException(string message, bool canRetry) : Exception(message)
{
    public bool CanRetry { get; } = canRetry;
}
