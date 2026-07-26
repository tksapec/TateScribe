using TateScribe.Core.Ocr;

namespace TateScribe.Core.Ruby;

public static class RubyOcrCandidateSelector
{
    public static IReadOnlyList<RubyOcrCandidate> Select(
        string pageMarker,
        string adjacentBodyText,
        IReadOnlyList<OcrWordReviewState> reviewedWords) =>
        Select(pageMarker, reviewedWords);

    public static IReadOnlyList<RubyOcrCandidate> Select(
        string pageMarker,
        IReadOnlyList<OcrWordReviewState> reviewedWords)
    {
        var bodyWords = reviewedWords
            .Where(word => word.Role == "Body" && word.AutomaticRole == "Body")
            .ToArray();
        return reviewedWords
            .Where(word => word.Role == "RubyCandidate"
                || (word.Role == "Body"
                    && word.IsManualOverride
                    && word.AutomaticRole == "RubyCandidate"))
            .Select(word =>
            {
                var link = RubyCandidateLinker.Link(word, bodyWords);
                return new RubyOcrCandidate(
                    pageMarker,
                    word.Word.Text,
                    link?.BaseTextCandidate,
                    word.Word.Left,
                    word.Word.Top,
                    word.Word.Right,
                    word.Word.Bottom,
                    word.Word.Confidence,
                    word.RunId,
                    word.Role == "Body",
                    word.IncludedInDraft,
                    link?.Confidence,
                    2,
                    string.Empty);
            })
            .ToArray();
    }
}
