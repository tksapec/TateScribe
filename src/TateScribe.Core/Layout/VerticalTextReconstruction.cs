using TateScribe.Core.Ocr;
using System.Text;

namespace TateScribe.Core.Layout;

public sealed record ReviewItem(string Code, string Message, OcrWord? Word);

public sealed record ReconstructedPage(string Text, IReadOnlyList<ReviewItem> ReviewItems);

public sealed record ReadingOrderOcrWord(int RawOrdinal, OcrWord Word);

public static class VerticalTextReconstruction
{
    public static IReadOnlyList<OcrWord> OrderWordsForReading(IReadOnlyList<OcrWord> words, double columnTolerance) =>
        OrderWordsForReadingWithRawOrdinals(words, columnTolerance).Select(word => word.Word).ToArray();

    public static IReadOnlyList<ReadingOrderOcrWord> OrderWordsForReadingWithRawOrdinals(IReadOnlyList<OcrWord> words, double columnTolerance)
    {
        var filteredWords = RubyFilter.ExcludeCandidates(words);
        var consumedRawOrdinals = new HashSet<int>();
        var indexedWords = filteredWords.Select(word =>
        {
            var rawOrdinal = Enumerable.Range(0, words.Count).First(ordinal => !consumedRawOrdinals.Contains(ordinal) && words[ordinal] == word);
            consumedRawOrdinals.Add(rawOrdinal);
            return new ReadingOrderOcrWord(rawOrdinal, word);
        }).ToArray();
        var glyphs = indexedWords.Select(indexed => new Glyph(indexed.Word.Text, (indexed.Word.Left + indexed.Word.Right) / 2, indexed.Word.Top)).ToArray();
        var lookup = indexedWords.GroupBy(indexed => new Glyph(indexed.Word.Text, (indexed.Word.Left + indexed.Word.Right) / 2, indexed.Word.Top))
            .ToDictionary(group => group.Key, group => new Queue<ReadingOrderOcrWord>(group));
        return VerticalReadingOrder.OrderColumns(glyphs, columnTolerance)
            .SelectMany(column => column.Select(glyph => lookup[glyph].Dequeue()))
            .ToArray();
    }

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

    public static ReconstructedPage ReconstructReviewed(
        IReadOnlyList<OcrWordReviewState> words,
        double columnTolerance,
        double lowConfidenceThreshold)
    {
        var included = words.Where(word => word.IncludedInDraft).Select(word => word.Word).ToArray();
        var glyphs = included.Select(word => new Glyph(word.Text, (word.Left + word.Right) / 2, word.Top)).ToArray();
        var lookup = included.GroupBy(word => new Glyph(word.Text, (word.Left + word.Right) / 2, word.Top))
            .ToDictionary(group => group.Key, group => new Queue<OcrWord>(group));
        var columns = VerticalReadingOrder.OrderColumns(glyphs, columnTolerance)
            .Select(column => column.Select(glyph => lookup[glyph].Dequeue()).ToArray())
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
