using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TateScribe.Core.ChatGpt;
using TateScribe.Core.Proofreading;

namespace TateScribe.Infrastructure.Proofreading;

public sealed class ProofreadingPackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IChatGptPromptTemplateProvider promptTemplates;

    public ProofreadingPackageExporter(IChatGptPromptTemplateProvider? promptTemplates = null)
    {
        this.promptTemplates = promptTemplates ?? new ChatGptPromptTemplateProvider();
    }

    public async Task ExportAsync(ProofreadingPackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        if (request.Pages.Count == 0) throw new ArgumentException("A proofreading package requires at least one page.", nameof(request));
        if (File.Exists(request.DestinationPath) || Directory.Exists(request.DestinationPath))
            throw new IOException($"The proofreading package destination already exists: {request.DestinationPath}");

        var parent = Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath))
            ?? throw new InvalidOperationException("Package destination has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".tatescribe-proofreading-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            await WritePackageAsync(staging, request, cancellationToken);
            if (request.Format == ProofreadingPackageFormat.Zip)
                ZipFile.CreateFromDirectory(staging, request.DestinationPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            else
            {
                Directory.Move(staging, request.DestinationPath);
                staging = string.Empty;
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(staging) && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private async Task WritePackageAsync(string root, ProofreadingPackageRequest request, CancellationToken cancellationToken)
    {
        var originalDirectory = Path.Combine(root, "images-original");
        Directory.CreateDirectory(originalDirectory);
        var croppedDirectory = Path.Combine(root, "images-cropped");
        var hasCroppedImages = request.Pages.Any(page => !string.IsNullOrWhiteSpace(page.CroppedImagePath));
        if (hasCroppedImages) Directory.CreateDirectory(croppedDirectory);

        var manifestPages = new List<ManifestPage>();
        for (var index = 0; index < request.Pages.Count; index++)
        {
            var page = request.Pages[index];
            var marker = (index + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
            var originalExtension = Path.GetExtension(page.OriginalImagePath);
            if (string.IsNullOrWhiteSpace(originalExtension)) originalExtension = ".png";
            var originalRelativePath = $"images-original/PAGE-{marker}{originalExtension.ToLowerInvariant()}";
            var originalDestination = Path.Combine(root, originalRelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(page.OriginalImagePath, originalDestination, overwrite: false);
            string? croppedRelativePath = null;
            if (!string.IsNullOrWhiteSpace(page.CroppedImagePath))
            {
                croppedRelativePath = $"images-cropped/PAGE-{marker}.png";
                File.Copy(page.CroppedImagePath, Path.Combine(root, croppedRelativePath.Replace('/', Path.DirectorySeparatorChar)), overwrite: false);
            }

            var selected = page.SelectText();
            manifestPages.Add(new ManifestPage(
                marker, page.ProjectPageId, page.SortOrder, page.SourceFileName, page.SourceFileHash,
                originalRelativePath, croppedRelativePath, page.DisplayProfile, page.PageRole,
                HashText(page.MachineText), page.SuggestedText is null ? null : HashText(page.SuggestedText),
                HashText(selected.Text), selected.Source, page.JoinToNext.ToString(), page.LowConfidenceCount));
        }

        var manifest = new Manifest(2, request.ProjectId, request.ProjectName, DateTimeOffset.UtcNow,
            request.BatchId, request.Pages.Count, manifestPages);
        await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "ocr.txt"),
            CreateOcrText(request, manifestPages), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "instructions.md"),
            promptTemplates.GetTemplate(ChatGptTaskType.TextProofreading), new UTF8Encoding(false), cancellationToken);
        var reviewItems = request.Pages.SelectMany((page, index) => (page.ReviewItems ?? [])
            .Select(item => new { pageMarker = manifestPages[index].PageMarker, item.Code, item.Message, item.Text })).ToArray();
        await File.WriteAllTextAsync(Path.Combine(root, "review-items.json"),
            JsonSerializer.Serialize(reviewItems, JsonOptions), new UTF8Encoding(false), cancellationToken);
    }

    private static string CreateOcrText(ProofreadingPackageRequest request, IReadOnlyList<ManifestPage> manifestPages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[[TATESCRIBE_FORMAT:2]]");
        builder.AppendLine($"[[PROJECT_ID:{request.ProjectId:D}]]");
        builder.AppendLine($"[[BATCH_ID:{request.BatchId:D}]]");
        builder.AppendLine();
        for (var index = 0; index < request.Pages.Count; index++)
        {
            var page = request.Pages[index];
            var selected = page.SelectText();
            builder.AppendLine($"[[PAGE:{manifestPages[index].PageMarker}]]");
            builder.AppendLine("[[TEXT_BEGIN]]");
            builder.Append(selected.Text);
            if (!selected.Text.EndsWith('\n')) builder.AppendLine();
            builder.AppendLine("[[TEXT_END]]");
            builder.AppendLine($"[[JOIN_TO_NEXT:{page.JoinToNext}]]");
            builder.AppendLine();
        }
        builder.AppendLine("[[REPORT_BEGIN]]");
        builder.AppendLine("[[REPORT_END]]");
        return builder.ToString();
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record Manifest(
        int FormatVersion,
        Guid ProjectId,
        string ProjectName,
        DateTimeOffset ExportedAt,
        Guid BatchId,
        int PageCount,
        IReadOnlyList<ManifestPage> Pages);

    private sealed record ManifestPage(
        string PageMarker,
        Guid ProjectPageId,
        int SortOrder,
        string SourceFileName,
        string SourceFileHash,
        string OriginalImage,
        string? CroppedImage,
        string DisplayProfile,
        string PageRole,
        string MachineTextHash,
        string? SuggestedTextHash,
        string BaselineTextHash,
        string TextSource,
        string JoinToNext,
        int LowConfidenceCount);
}
