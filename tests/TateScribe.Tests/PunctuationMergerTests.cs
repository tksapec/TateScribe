using TateScribe.Core.Layout;

namespace TateScribe.Tests;

public sealed class PunctuationMergerTests
{
    [Fact]
    public void Merge_inserts_only_punctuation_supported_by_matching_neighbours()
    {
        var result = PunctuationMerger.Merge("私は学生です", "私は、学生です。", 16);

        Assert.Equal("私は、学生です。", result);
    }

    [Fact]
    public void Merge_does_not_insert_punctuation_when_auxiliary_text_does_not_align()
    {
        var result = PunctuationMerger.Merge("私は学生です", "彼は、先生です。", 16);

        Assert.Equal("私は学生です", result);
    }
}
