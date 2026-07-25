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

    private static OcrWord Word(string text, double left, double top, double right, double bottom) => new(text, .99, left, top, right, bottom);
}
