using TateScribe.Core.Ocr;

namespace TateScribe.Core.Layout;

public static class RubyFilter
{
    public static IReadOnlyList<OcrWord> ExcludeCandidates(IReadOnlyList<OcrWord> words)
    {
        if (words.Count < 2) return words;
        var medianWidth = words.Select(Width).OrderBy(value => value).ElementAt(words.Count / 2);
        return words.Where(word => IsPunctuation(word.Text) || !IsRubyCandidate(word, words, medianWidth)).ToArray();
    }

    private static bool IsRubyCandidate(OcrWord candidate, IReadOnlyList<OcrWord> words, double medianWidth) =>
        Width(candidate) < medianWidth * .85 &&
        words.Any(body => body != candidate && Width(body) >= medianWidth &&
            candidate.Bottom > body.Top && candidate.Top < body.Bottom &&
            HorizontalGap(candidate, body) <= medianWidth);

    private static double Width(OcrWord word) => word.Right - word.Left;

    private static double HorizontalGap(OcrWord first, OcrWord second) =>
        first.Right < second.Left ? second.Left - first.Right :
        second.Right < first.Left ? first.Left - second.Right : 0;

    private static bool IsPunctuation(string text) => text.All(character => "、。！？…・「」『』（）()［］[]".Contains(character));
}
