using TateScribe.Core.Ruby;
using TateScribe.Core.Images;
using TateScribe.Core.Export;

namespace TateScribe.Core.Denden;

public sealed record DendenExportOptions(
    string Title,
    string Creator,
    string Language = "ja",
    bool VerticalWriting = true,
    bool GenerateTitlePage = true,
    bool GenerateTableOfContents = true,
    int TableOfContentsDepth = 2,
    bool AutoTcy = true,
    int TcyDigitCount = 2,
    bool SplitByChapter = false,
    IReadOnlyDictionary<string, string>? ApprovedGlobalRubies = null,
    string? CoverImagePath = null,
    IReadOnlyList<string>? IllustrationImagePaths = null,
    bool SkipCover = true,
    bool DisplayLandmarksNav = false,
    bool DisplayIllustrationList = false)
{
    public string EffectiveLanguage => string.IsNullOrWhiteSpace(Language) ? "ja" : Language.Trim();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            throw new ArgumentException("書名は必須です。", nameof(Title));
        if (string.IsNullOrWhiteSpace(Creator))
            throw new ArgumentException("著者は必須です。", nameof(Creator));
        if (TableOfContentsDepth is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(TableOfContentsDepth), "目次深度は1～6で指定してください。");
        if (TcyDigitCount < 2)
            throw new ArgumentOutOfRangeException(nameof(TcyDigitCount), "縦中横の桁数は2以上で指定してください。");
    }
}

public sealed record DendenIllustration(
    Guid PageId,
    int SortOrder,
    string SourcePath,
    string AltText,
    string? Caption = null,
    NormalizedCrop? Crop = null,
    int RotationDegrees = 0);

public sealed record DendenExportPlan(
    Guid PlanId,
    IReadOnlyList<ExportPreflightIssue> Issues)
{
    public bool CanExport => !Issues.Any(issue => issue.IsFatal);
}

public abstract record DendenContentBlock;

public sealed record DendenParagraphBlock(
    StructuredParagraph Paragraph) : DendenContentBlock;

public sealed record DendenIllustrationBlock(
    DendenIllustration Illustration,
    bool PlacementAdjusted = false) : DendenContentBlock;

public sealed record DendenExportWarning(
    string Code,
    string Message,
    Guid? PageId = null);

public sealed record DendenExportDocument(
    StructuredDocument Document,
    IReadOnlyList<DendenContentBlock> Blocks)
{
    public IReadOnlyList<DendenExportWarning> Warnings => Blocks
        .OfType<DendenIllustrationBlock>()
        .Where(block => block.PlacementAdjusted)
        .Select(block => new DendenExportWarning(
            "IllustrationPlacementAdjusted",
            $"挿絵「{block.Illustration.AltText}」は連結段落を分断しないよう段落の直後へ配置しました。",
            block.Illustration.PageId))
        .ToArray();
}

public interface IDendenExportService
{
    Task ExportAsync(
        StructuredDocument document,
        DendenExportOptions options,
        string destinationDirectory,
        CancellationToken cancellationToken);

    Task ExportAsync(
        DendenExportDocument document,
        DendenExportOptions options,
        string destinationDirectory,
        CancellationToken cancellationToken);
}
