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

    [Fact]
    public void Merge_inserts_matching_opening_and_closing_quotes()
    {
        var result = PunctuationMerger.Merge("島崎わたしは言った", "「島崎わたしは言った」", 16);

        Assert.Equal("「島崎わたしは言った」", result);
    }

    [Fact]
    public void Merge_inserts_a_missing_small_kana_between_matching_characters()
    {
        var result = PunctuationMerger.Merge("きっと", "きゃっと", 16);

        Assert.Equal("きゃっと", result);
    }

    [Fact]
    public void Merge_does_not_insert_a_small_kana_across_unmatched_body_text()
    {
        var result = PunctuationMerger.Merge("な荷物を持た成瀬", "なった成瀬", 16);

        Assert.Equal("な荷物を持た成瀬", result);
    }
}
