using TateScribe.Core.Ocr;

namespace TateScribe.Core.Layout;

public static class RubyFilter
{
    public static IReadOnlyList<OcrWord> ExcludeCandidates(IReadOnlyList<OcrWord> words)
    {
        if (words.Count < 2) return words;
        var medianHeight = words.Select(Height).OrderBy(value => value).ElementAt(words.Count / 2);
        return words.Where(word => IsPunctuation(word.Text) || !IsRubyCandidate(word, words, medianHeight)).ToArray();
    }

    private static bool IsRubyCandidate(OcrWord candidate, IReadOnlyList<OcrWord> words, double medianHeight) =>
        Height(candidate) < medianHeight * .7 &&
        words.Any(body => body != candidate && Height(body) >= medianHeight &&
            candidate.Bottom > body.Top && candidate.Top < body.Bottom &&
            Math.Abs(candidate.Right - body.Left) <= medianHeight);

    private static double Height(OcrWord word) => word.Bottom - word.Top;

    private static bool IsPunctuation(string text) => text.All(character => "、。！？…・「」『』（）()［］[]".Contains(character));
}
