using System.Diagnostics;
using System.Text.Json;
using TateScribe.Core.Ocr;

namespace TateScribe.Infrastructure.Ocr;

public sealed class JsonLinesOcrWorker(string pythonExecutable, string workerScript) : IOcrWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pythonExecutable = pythonExecutable;
    private readonly string _workerScript = workerScript;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;

    public async Task<OcrPageResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var process = EnsureProcess();
            try
            {
                await process.StandardInput.WriteLineAsync(
                    JsonSerializer.Serialize(new WorkerRequest(1, request.RequestId, request.Engine, request.ImagePath), JsonOptions)
                        .AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } responseLine)
                {
                    WorkerResponse? response;
                    try
                    {
                        response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                    if (response?.ProtocolVersion != 1 || response.RequestId != request.RequestId) continue;
                    if (!string.Equals(response.Status, "ok", StringComparison.Ordinal))
                        throw new OcrWorkerException(
                            response.Error ?? "OCR worker failed.",
                            response.Retryable ?? true,
                            response.Stage,
                            response.ExceptionType);
                    return new OcrPageResult(
                        response.RequestId,
                        response.Engine ?? request.Engine,
                        response.ModelVersion ?? "unknown",
                        response.Words ?? []);
                }

                var exitDetail = process.HasExited ? $" Exit code: {process.ExitCode}." : string.Empty;
                StopProcess();
                throw new OcrWorkerException($"OCR worker returned no matching response.{exitDetail}", true, request.Engine);
            }
            catch (OperationCanceledException)
            {
                StopProcess();
                throw;
            }
            catch (IOException)
            {
                StopProcess();
                throw;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private Process EnsureProcess()
    {
        if (!File.Exists(_workerScript))
            throw new OcrWorkerException("OCR worker script is missing.", true, "WorkerStart", nameof(FileNotFoundException));
        if (_process is { HasExited: false }) return _process;
        StopProcess();
        var startInfo = new ProcessStartInfo(_pythonExecutable, $"\"{_workerScript}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _process = Process.Start(startInfo)
            ?? throw new OcrWorkerException("Could not start OCR worker.", true, "WorkerStart");
        return _process;
    }

    private void StopProcess()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        StopProcess();
        _requestLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record WorkerRequest(int ProtocolVersion, string RequestId, string Engine, string ImagePath);

    private sealed record WorkerResponse(
        int ProtocolVersion,
        string RequestId,
        string Status,
        string? Engine,
        string? ModelVersion,
        IReadOnlyList<OcrWord>? Words,
        string? Error,
        string? Stage,
        string? ExceptionType,
        bool? Retryable);
}
