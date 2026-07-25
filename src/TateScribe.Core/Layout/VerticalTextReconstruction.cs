using TateScribe.Core.Ocr;

namespace TateScribe.Core.Layout;

public sealed record ReviewItem(string Code, string Message, OcrWord Word);

public sealed record ReconstructedPage(string Text, IReadOnlyList<ReviewItem> ReviewItems);

public static class VerticalTextReconstruction
{
    public static ReconstructedPage Reconstruct(IReadOnlyList<OcrWord> words, double columnTolerance, double lowConfidenceThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lowConfidenceThreshold);
        var glyphs = words.Select(word => new Glyph(word.Text, (word.Left + word.Right) / 2, word.Top)).ToArray();
        var lookup = words.ToDictionary(word => (word.Text, (word.Left + word.Right) / 2, word.Top));
        var ordered = VerticalReadingOrder.Order(glyphs, columnTolerance)
            .Select(glyph => lookup[(glyph.Text, glyph.X, glyph.Y)])
            .ToArray();
        var reviewItems = ordered.Where(word => word.Confidence < lowConfidenceThreshold)
            .Select(word => new ReviewItem("LowConfidence", $"OCR confidence {word.Confidence:P0} requires review.", word))
            .ToArray();
        return new ReconstructedPage(string.Concat(ordered.Select(word => word.Text)), reviewItems);
    }

    public static string JoinPages(IEnumerable<ReconstructedPage> pages) => string.Concat(pages.Select(page => page.Text));
}
