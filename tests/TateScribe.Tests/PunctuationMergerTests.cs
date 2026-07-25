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

    [Fact]
    public void Merge_inserts_a_missing_long_vowel_mark_between_matching_characters()
    {
        var result = PunctuationMerger.Merge("ロカル", "ローカル", 16);

        Assert.Equal("ローカル", result);
    }

    [Fact]
    public void Merge_treats_an_ocr_square_bracket_as_an_opening_quote()
    {
        var result = PunctuationMerger.Merge("番組ぐるりん", "番組[ぐるりん", 16);

        Assert.Equal("番組「ぐるりん", result);
    }

    [Fact]
    public void Merge_keeps_an_opening_quote_when_the_following_body_character_differs()
    {
        var result = PunctuationMerger.Merge("芸人に糊の", "芸人に「着の", 16);

        Assert.Equal("芸人に「糊の", result);
    }

    [Fact]
    public void Merge_inserts_a_trailing_long_vowel_mark_after_an_otherwise_matching_word()
    {
        var result = PunctuationMerger.Merge("レポタ", "レポーター", 16);

        Assert.Equal("レポーター", result);
    }

    [Fact]
    public void Merge_restores_a_long_vowel_mark_and_opening_quote_in_a_program_title()
    {
        var result = PunctuationMerger.Merge("成瀬は夕方のロカル番組ぐるりんワイド」に出演する", "成瀬は夕方のローカル番組[ぐるりんワイド」に出演する", 16);

        Assert.Equal("成瀬は夕方のローカル番組「ぐるりんワイド」に出演する", result);
    }

    [Fact]
    public void Merge_treats_a_tesseract_horizontal_stroke_as_an_opening_quote_when_a_closing_quote_follows()
    {
        var result = PunctuationMerger.Merge("成瀬は島崎、わたしはシャボン玉を極めようと思うんだ」と言った", "成瀬は一島崎、わたしはシャボン玉を極めようと思うんだ」と言った", 16);

        Assert.Equal("成瀬は「島崎、わたしはシャボン玉を極めようと思うんだ」と言った", result);
    }

    [Fact]
    public void Merge_recovers_a_quote_marker_after_a_nearby_missing_comma()
    {
        var result = PunctuationMerger.Merge("ところ成瀬は島崎、わたしはシャボン玉を極めようと思うんだ」と", "ところ、成瀬は一島崎、わたしはシャボン玉を極めようと思うんだ」と", 16);

        Assert.Equal("ところ、成瀬は「島崎、わたしはシャボン玉を極めようと思うんだ」と", result);
    }

    [Fact]
    public void Merge_recovers_both_quotes_when_the_primary_ocr_missed_them()
    {
        var result = PunctuationMerger.Merge("成瀬は島崎、わたしはシャボン玉を極めようと思うんだと言った", "成瀬は一島崎、わたしはシャボン玉を極めようと思うんだ」と言った", 16);

        Assert.Equal("成瀬は「島崎、わたしはシャボン玉を極めようと思うんだ」と言った", result);
    }
}
