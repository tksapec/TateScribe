using TateScribe.Core.Ruby;

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
    IReadOnlyDictionary<string, string>? ApprovedGlobalRubies = null);

public interface IDendenExportService
{
    Task ExportAsync(
        StructuredDocument document,
        DendenExportOptions options,
        string destinationDirectory,
        CancellationToken cancellationToken);
}
