using TateScribe.Core.Layout;
using TateScribe.Core.Ocr;

namespace TateScribe.Tests;

public sealed class VerticalTextReconstructionTests
{
    [Fact]
    public void Reconstruct_reads_columns_right_to_left_without_inserting_column_breaks()
    {
        var result = VerticalTextReconstruction.Reconstruct([
            Word("う", 10, 10, 0.98), Word("い", 100, 30, 0.98), Word("あ", 100, 10, 0.98)
        ], columnTolerance: 8, lowConfidenceThreshold: 0.75);

        Assert.Equal("あいう", result.Text);
        Assert.Empty(result.ReviewItems);
    }

    [Fact]
    public void JoinPages_defaults_to_direct_join_and_keeps_low_confidence_as_review_item()
    {
        var first = VerticalTextReconstruction.Reconstruct([Word("前", 100, 10, 0.4)], 8, 0.75);
        var second = VerticalTextReconstruction.Reconstruct([Word("後", 100, 10, 0.98)], 8, 0.75);

        var text = VerticalTextReconstruction.JoinPages([first, second]);

        Assert.Equal("前後", text);
        Assert.Single(first.ReviewItems);
    }

    [Fact]
    public void Reconstruct_inserts_a_paragraph_break_when_the_next_column_has_a_first_line_indent()
    {
        var result = VerticalTextReconstruction.Reconstruct([
            Word("一", 100, 10, 0.98), Word("二", 90, 28, 0.98), Word("三", 80, 10, 0.98)
        ], columnTolerance: 8, lowConfidenceThreshold: 0.75);

        Assert.Equal("一\n二三", result.Text);
    }

    private static OcrWord Word(string text, double x, double y, double confidence) => new(text, confidence, x, y, x + 8, y + 12);
}
