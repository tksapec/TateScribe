using TateScribe.Core.Layout;

namespace TateScribe.Tests;

public sealed class VerticalReadingOrderTests
{
    [Fact]
    public void Order_reads_rightmost_column_then_top_to_bottom()
    {
        var glyphs = new[]
        {
            new Glyph("左", 10, 10), new Glyph("右下", 100, 30),
            new Glyph("右上", 100, 10), new Glyph("左下", 10, 30)
        };

        var ordered = VerticalReadingOrder.Order(glyphs, columnTolerance: 5);

        Assert.Equal(["右上", "右下", "左", "左下"], ordered.Select(x => x.Text));
    }
}
