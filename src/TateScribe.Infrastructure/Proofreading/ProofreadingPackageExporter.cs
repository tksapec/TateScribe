using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TateScribe.Core.Proofreading;

namespace TateScribe.Infrastructure.Proofreading;

public sealed class ProofreadingPackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task ExportAsync(ProofreadingPackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        if (request.Pages.Count == 0) throw new ArgumentException("A proofreading package requires at least one page.", nameof(request));
        if (File.Exists(request.DestinationPath) || Directory.Exists(request.DestinationPath))
            throw new IOException($"The proofreading package destination already exists: {request.DestinationPath}");

        var parent = Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath)) ?? throw new InvalidOperationException("Package destination has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".tatescribe-proofreading-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            await WritePackageAsync(staging, request, cancellationToken);
            if (request.Format == ProofreadingPackageFormat.Zip)
            {
                ZipFile.CreateFromDirectory(staging, request.DestinationPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
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

    private static async Task WritePackageAsync(string root, ProofreadingPackageRequest request, CancellationToken cancellationToken)
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
            var originalRelativePath = $"images-original/PAGE-{marker}.png";
            var originalDestination = Path.Combine(root, originalRelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(page.OriginalImagePath, originalDestination, overwrite: false);
            string? croppedRelativePath = null;
            if (!string.IsNullOrWhiteSpace(page.CroppedImagePath))
            {
                croppedRelativePath = $"images-cropped/PAGE-{marker}.png";
                File.Copy(page.CroppedImagePath, Path.Combine(root, croppedRelativePath.Replace('/', Path.DirectorySeparatorChar)), overwrite: false);
            }
            manifestPages.Add(new ManifestPage(marker, page.ProjectPageId, page.SortOrder, page.SourceFileName, page.SourceFileHash,
                originalRelativePath, croppedRelativePath, page.DisplayProfile, page.PageRole,
                HashText(page.MachineText), page.SuggestedText is null ? null : HashText(page.SuggestedText), page.LowConfidenceCount));
        }

        var manifest = new Manifest(1, request.ProjectId, request.ProjectName, DateTimeOffset.UtcNow, request.BatchId, request.Pages.Count, manifestPages);
        await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "ocr.txt"), CreateOcrText(request, manifestPages), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "instructions.md"), Instructions, new UTF8Encoding(false), cancellationToken);
        var reviewItems = request.Pages.SelectMany((page, index) => (page.ReviewItems ?? [])
            .Select(item => new { pageMarker = manifestPages[index].PageMarker, item.Code, item.Message, item.Text })).ToArray();
        await File.WriteAllTextAsync(Path.Combine(root, "review-items.json"), JsonSerializer.Serialize(reviewItems, JsonOptions), new UTF8Encoding(false), cancellationToken);
    }

    private static string CreateOcrText(ProofreadingPackageRequest request, IReadOnlyList<ManifestPage> manifestPages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[[TATESCRIBE_FORMAT:1]]");
        builder.AppendLine($"[[PROJECT_ID:{request.ProjectId:D}]]");
        builder.AppendLine($"[[BATCH_ID:{request.BatchId:D}]]");
        builder.AppendLine();
        for (var index = 0; index < request.Pages.Count; index++)
        {
            var page = request.Pages[index];
            var manifest = manifestPages[index];
            builder.AppendLine($"[[PAGE:{manifest.PageMarker}]]");
            builder.AppendLine($"[[SOURCE_FILE:{page.SourceFileName}]]");
            builder.AppendLine($"[[PAGE_ROLE:{page.PageRole}]]");
            builder.AppendLine($"[[DISPLAY_PROFILE:{page.DisplayProfile}]]");
            builder.AppendLine();
            builder.AppendLine(page.SuggestedText ?? page.MachineText);
            if (index + 1 < request.Pages.Count) builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private const string Instructions = """
        # TateScribe 校正指示

        添付画像を正本としてOCR結果を校正してください。OCR本文だけから推測せず、原文にない文字や表現を追加したり、読みやすく書き換えたりしないでください。

        - 縦書きは右列から左列へ読み、列替わり・ページ切替わりだけの改行は本文に残さない。
        - 原文の段落、会話文、鉤括弧、句読点を保持し、ルビを本文へ重複して入れない。
        - ステータスバー、進捗率、ページ番号、柱見出し、写真、挿絵、画像内文字、キャプションは出力しない。
        - 章冒頭の章番号・章タイトルと本文途中の節番号は残す。
        - 判読不能箇所は［判読不能: PAGE-xxxx］とし、[[PAGE:xxxx]]を含む管理マーカーは削除しない。

        校正済み本文、判読不能箇所一覧、主な修正箇所一覧を返してください。
        """;

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
        int LowConfidenceCount);
}
