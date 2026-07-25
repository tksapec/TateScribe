using TateScribe.Core.Layout;
using TateScribe.Core.Ocr;

namespace TateScribe.Tests;

public sealed class RubyFilterTests
{
    [Fact]
    public void ExcludeCandidates_removes_small_text_beside_a_body_column()
    {
        var body = Word("本文", 100, 10, 120, 50);
        var ruby = Word("ほんぶん", 88, 12, 96, 38);

        var result = RubyFilter.ExcludeCandidates([body, ruby]);

        Assert.Equal([body], result);
    }

    [Fact]
    public void ExcludeCandidates_keeps_small_punctuation()
    {
        var body = Word("本文", 100, 10, 120, 50);
        var punctuation = Word("、", 100, 55, 106, 61);

        var result = RubyFilter.ExcludeCandidates([body, punctuation]);

        Assert.Equal([body, punctuation], result);
    }

    [Fact]
    public void ExcludeCandidates_keeps_a_shorter_body_column_with_the_same_character_width()
    {
        var shorterBodyColumn = Word("本文列", 100, 10, 140, 1200);
        var longerBodyColumn = Word("隣接本文列", 60, 10, 100, 1900);

        var result = RubyFilter.ExcludeCandidates([shorterBodyColumn, longerBodyColumn]);

        Assert.Equal([shorterBodyColumn, longerBodyColumn], result);
    }

    [Fact]
    public void ExcludeCandidates_removes_a_ruby_column_that_is_slightly_narrower_than_body_text()
    {
        var body = Word("大津市", 533, 267, 573, 2195);
        var ruby = Word("おおつ", 566, 1180, 598, 1390);

        var result = RubyFilter.ExcludeCandidates([body, ruby]);

        Assert.Equal([body], result);
    }

    [Fact]
    public void ExcludeCandidates_uses_the_adjacent_body_column_when_it_is_narrower_than_the_page_median()
    {
        var adjacentBody = Word("大津市", 533, 267, 570, 2195);
        var ruby = Word("おおつ", 566, 1180, 598, 1390);
        var widerBodyElsewhere = Word("本文", 800, 267, 840, 2195);
        var secondWiderBodyElsewhere = Word("本文", 900, 267, 942, 2195);

        var result = RubyFilter.ExcludeCandidates([adjacentBody, ruby, widerBodyElsewhere, secondWiderBodyElsewhere]);

        Assert.Equal([adjacentBody, widerBodyElsewhere, secondWiderBodyElsewhere], result);
    }

    private static OcrWord Word(string text, double left, double top, double right, double bottom) => new(text, .99, left, top, right, bottom);
}
