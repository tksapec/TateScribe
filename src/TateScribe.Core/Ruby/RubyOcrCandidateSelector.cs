using TateScribe.Core.Ocr;

namespace TateScribe.Core.Ruby;

public static class RubyOcrCandidateSelector
{
    public static IReadOnlyList<RubyOcrCandidate> Select(
        string pageMarker,
        string adjacentBodyText,
        IReadOnlyList<OcrWordReviewState> reviewedWords)
    {
        return reviewedWords
            .Where(word => word.Role == "RubyCandidate"
                || (word.Role == "Body"
                    && word.IsManualOverride
                    && word.AutomaticRole == "RubyCandidate"))
            .Select(word => new RubyOcrCandidate(
                pageMarker,
                word.Word.Text,
                word.Word.Left,
                word.Word.Top,
                word.Word.Right,
                word.Word.Bottom,
                word.Word.Confidence,
                adjacentBodyText,
                word.RunId,
                word.Role == "Body",
                word.IncludedInDraft))
            .ToArray();
    }
}
