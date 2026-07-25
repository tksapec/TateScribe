using TateScribe.Core.Ocr;

namespace TateScribe.Core.Projects;

public sealed record PageTextState(
    Guid PageId,
    string? ManualText,
    string Engine,
    string ModelVersion,
    IReadOnlyList<OcrWord> MachineWords,
    string? RawTesseractText = null,
    string? SuggestedText = null,
    string? ConfirmedText = null,
    DateTimeOffset? ConfirmedAt = null,
    string? ConfirmedSource = null,
    bool RawPaddleCoordinatesKnown = true,
    string? LegacyMergedText = null)
{
    public IReadOnlyList<OcrWord> RawPaddleWords => MachineWords;

    public string SelectedText => ConfirmedText ?? ManualText ?? SuggestedText ?? string.Empty;
}
