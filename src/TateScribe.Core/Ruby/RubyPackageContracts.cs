using System.Text.Json.Serialization;

namespace TateScribe.Core.Ruby;

public sealed record RubyPackagePage(
    Guid PageId,
    string PageMarker,
    string OriginalImagePath,
    string? CroppedImagePath);

public sealed record RubyOcrCandidate(
    string PageMarker,
    string ReadingCandidate,
    string? BaseTextCandidate,
    double Left,
    double Top,
    double Right,
    double Bottom,
    double Confidence,
    Guid OcrRunId,
    bool ReturnedToBody,
    bool IncludedInDraft,
    double? LinkConfidence = null,
    int CandidateVersion = 2,
    [property: JsonIgnore] string LegacyAdjacentBodyText = "")
{
    [JsonIgnore]
    public string OcrText => ReadingCandidate;

    [JsonIgnore]
    public string AdjacentBodyText => LegacyAdjacentBodyText;

    public RubyOcrCandidate(
        string pageMarker,
        string ocrText,
        double left,
        double top,
        double right,
        double bottom,
        double confidence,
        string adjacentBodyText,
        Guid ocrRunId,
        bool returnedToBody,
        bool includedInDraft)
        : this(
            pageMarker,
            ocrText,
            null,
            left,
            top,
            right,
            bottom,
            confidence,
            ocrRunId,
            returnedToBody,
            includedInDraft,
            null,
            1,
            adjacentBodyText)
    {
    }
}

public sealed record RubyPackageRequest(
    Guid ProjectId,
    Guid BatchId,
    RubyPolicy RubyPolicy,
    StructuredDocument Document,
    IReadOnlyList<RubyPackagePage> Pages,
    IReadOnlyList<RubyOcrCandidate> Candidates,
    string DestinationPath);
