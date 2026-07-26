using TateScribe.Core.Ocr;
using TateScribe.Core.Layout;

namespace TateScribe.Core.Projects;

public sealed record PageTextSelection(string Text, string Source);

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

    public string SelectedText => SelectForProofreading().Text;

    public PageTextSelection SelectForProofreading()
    {
        if (ConfirmedText is not null) return new PageTextSelection(ConfirmedText, "Confirmed");
        if (ManualText is not null) return new PageTextSelection(ManualText, "Manual");
        if (SuggestedText is not null) return new PageTextSelection(SuggestedText, "Suggested");
        var reconstructed = VerticalTextReconstruction.Reconstruct(RawPaddleWords, 20, .75).Text;
        return new PageTextSelection(reconstructed, "RawPaddle");
    }
}
