using TateScribe.Core.Ocr;
using System.Text;

namespace TateScribe.Core.Layout;

public sealed record ReviewItem(string Code, string Message, OcrWord? Word);

public sealed record ReconstructedPage(string Text, IReadOnlyList<ReviewItem> ReviewItems);

public static class VerticalTextReconstruction
{
    public static ReconstructedPage Reconstruct(IReadOnlyList<OcrWord> words, double columnTolerance, double lowConfidenceThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lowConfidenceThreshold);
        var filteredWords = RubyFilter.ExcludeCandidates(words);
        var glyphs = filteredWords.Select(word => new Glyph(word.Text, (word.Left + word.Right) / 2, word.Top)).ToArray();
        var lookup = filteredWords.ToDictionary(word => (word.Text, (word.Left + word.Right) / 2, word.Top));
        var columns = VerticalReadingOrder.OrderColumns(glyphs, columnTolerance)
            .Select(column => column.Select(glyph => lookup[(glyph.Text, glyph.X, glyph.Y)]).ToArray())
            .ToArray();
        var ordered = columns.SelectMany(column => column).ToArray();
        var reviewItems = ordered.Where(word => word.Confidence < lowConfidenceThreshold)
            .Select(word => new ReviewItem("LowConfidence", $"OCR confidence {word.Confidence:P0} requires review.", word))
            .ToArray();
        return new ReconstructedPage(JoinColumnsWithParagraphBreaks(columns), reviewItems);
    }

    public static string JoinPages(IEnumerable<ReconstructedPage> pages) => string.Concat(pages.Select(page => page.Text));

    private static string JoinColumnsWithParagraphBreaks(IReadOnlyList<OcrWord[]> columns)
    {
        if (columns.Count == 0) return string.Empty;
        var characterWidth = columns.SelectMany(column => column)
            .Select(word => word.Right - word.Left)
            .Order()
            .Skip(Math.Max(0, columns.Sum(column => column.Length) / 2))
            .FirstOrDefault();
        var indentThreshold = Math.Max(8, characterWidth * .6);
        var text = new StringBuilder();
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (column.Length == 0) continue;
            if (index > 0 && columns[index - 1].Length > 0 && column[0].Top - columns[index - 1][0].Top >= indentThreshold)
                text.Append('\n');
            foreach (var word in column) text.Append(word.Text);
        }
        return text.ToString();
    }
}
