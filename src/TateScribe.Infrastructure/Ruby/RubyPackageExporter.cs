using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TateScribe.Core.ChatGpt;
using TateScribe.Core.Ruby;

namespace TateScribe.Infrastructure.Ruby;

public sealed class RubyPackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
    private readonly IChatGptPromptTemplateProvider promptTemplates;

    public RubyPackageExporter(IChatGptPromptTemplateProvider? promptTemplates = null)
    {
        this.promptTemplates = promptTemplates ?? new ChatGptPromptTemplateProvider();
    }

    public async Task ExportAsync(RubyPackageRequest request, CancellationToken cancellationToken)
    {
        if (Directory.Exists(request.DestinationPath) || File.Exists(request.DestinationPath))
            throw new IOException($"出力先は既に存在します: {request.DestinationPath}");
        Directory.CreateDirectory(request.DestinationPath);
        try
        {
            var originalDirectory = Path.Combine(request.DestinationPath, "images-original");
            Directory.CreateDirectory(originalDirectory);
            var hasCropped = request.Pages.Any(page => !string.IsNullOrWhiteSpace(page.CroppedImagePath));
            var croppedDirectory = Path.Combine(request.DestinationPath, "images-cropped");
            if (hasCropped) Directory.CreateDirectory(croppedDirectory);
            foreach (var page in request.Pages)
            {
                var extension = Path.GetExtension(page.OriginalImagePath);
                if (string.IsNullOrEmpty(extension)) extension = ".png";
                File.Copy(page.OriginalImagePath,
                    Path.Combine(originalDirectory, $"PAGE-{page.PageMarker}{extension.ToLowerInvariant()}"));
                if (!string.IsNullOrWhiteSpace(page.CroppedImagePath))
                    File.Copy(page.CroppedImagePath, Path.Combine(croppedDirectory, $"PAGE-{page.PageMarker}.png"));
            }

            await WriteJsonAsync("manifest.json", new
            {
                formatVersion = 1,
                request.ProjectId,
                request.BatchId,
                request.Document.DocumentTextHash,
                request.RubyPolicy,
                pages = request.Pages.Select(page => new { page.PageId, page.PageMarker }),
            });
            await WriteJsonAsync("confirmed-document.json", new
            {
                formatVersion = 1,
                request.ProjectId,
                request.BatchId,
                request.Document.DocumentTextHash,
                request.RubyPolicy,
                paragraphs = request.Document.Paragraphs.Select(paragraph => new
                {
                    paragraphId = paragraph.ParagraphId.ToString("D"),
                    paragraph.Role,
                    text = paragraph.PlainText,
                    paragraph.TextHash,
                    sourceSpans = paragraph.SourceSpans.Select(span => new
                    {
                        span.PageMarker,
                        start = span.Start,
                        span.Length,
                    }),
                }),
            });
            await WriteJsonAsync("ruby-candidates.json", request.Candidates);
            await File.WriteAllTextAsync(
                Path.Combine(request.DestinationPath, "output-schema.json"),
                OutputSchema, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(request.DestinationPath, "instructions.md"),
                promptTemplates.GetTemplate(ChatGptTaskType.RubyAnnotation),
                new UTF8Encoding(false), cancellationToken);

            async Task WriteJsonAsync(string fileName, object value)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(request.DestinationPath, fileName),
                    JsonSerializer.Serialize(value, JsonOptions),
                    new UTF8Encoding(false), cancellationToken);
            }
        }
        catch
        {
            if (Directory.Exists(request.DestinationPath))
                Directory.Delete(request.DestinationPath, recursive: true);
            throw;
        }
    }

    private const string OutputSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["formatVersion", "projectId", "batchId", "documentTextHash", "annotations", "unresolved"],
          "properties": {
            "formatVersion": { "const": 1 },
            "projectId": { "type": "string", "format": "uuid" },
            "batchId": { "type": "string", "format": "uuid" },
            "documentTextHash": { "type": "string", "pattern": "^[0-9A-F]{64}$" },
            "annotations": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["paragraphId", "start", "length", "baseText", "reading", "source", "confidence", "evidencePageMarkers", "evidence"],
                "properties": {
                  "paragraphId": { "type": "string" },
                  "start": { "type": "integer", "minimum": 0 },
                  "length": { "type": "integer", "minimum": 1 },
                  "baseText": { "type": "string", "minLength": 1 },
                  "reading": { "type": "string", "minLength": 1 },
                  "source": { "enum": ["ImageConfirmed", "TextConfirmed", "UserConfirmed", "DictionarySuggested", "ContextSuggested"] },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                  "evidencePageMarkers": { "type": "array", "items": { "type": "string" } },
                  "evidence": { "type": "string" }
                }
              }
            },
            "unresolved": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["paragraphId", "start", "length", "baseText", "evidencePageMarkers", "reason"],
                "properties": {
                  "paragraphId": { "type": "string" },
                  "start": { "type": "integer", "minimum": 0 },
                  "length": { "type": "integer", "minimum": 1 },
                  "baseText": { "type": "string", "minLength": 1 },
                  "evidencePageMarkers": { "type": "array", "items": { "type": "string" } },
                  "reason": { "type": "string", "minLength": 1 }
                }
              }
            }
          }
        }
        """;
}
