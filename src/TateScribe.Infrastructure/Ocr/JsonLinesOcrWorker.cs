using System.Diagnostics;
using System.Text.Json;
using TateScribe.Core.Ocr;

namespace TateScribe.Infrastructure.Ocr;

public sealed class JsonLinesOcrWorker(string pythonExecutable, string workerScript) : IOcrWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pythonExecutable = pythonExecutable;
    private readonly string _workerScript = workerScript;

    public async Task<OcrPageResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(_workerScript)) throw new OcrWorkerException("OCR worker script is missing.", true);
        var startInfo = new ProcessStartInfo(_pythonExecutable, $"\"{_workerScript}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new OcrWorkerException("Could not start OCR worker.", true);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new WorkerRequest(1, request.RequestId, request.Engine, request.ImagePath), JsonOptions).AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            var responseLine = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new OcrWorkerException($"OCR worker returned no response. {standardError}".Trim(), true);
            }
            var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions) ?? throw new OcrWorkerException("OCR worker returned malformed JSON.", true);
            if (response.ProtocolVersion != 1 || response.RequestId != request.RequestId) throw new OcrWorkerException("OCR worker response did not match the request.", true);
            if (!string.Equals(response.Status, "ok", StringComparison.Ordinal)) throw new OcrWorkerException(response.Error ?? "OCR worker failed.", true);
            return new OcrPageResult(response.RequestId, response.Engine ?? request.Engine, response.ModelVersion ?? "unknown", response.Words ?? []);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record WorkerRequest(int ProtocolVersion, string RequestId, string Engine, string ImagePath);

    private sealed record WorkerResponse(int ProtocolVersion, string RequestId, string Status, string? Engine, string? ModelVersion, IReadOnlyList<OcrWord>? Words, string? Error);
}
