namespace TateScribe.Core.Export;

public static class WordRubyMetrics
{
    public static int CalculateRaiseHalfPoints(int rubyFontSizeHalfPoints, int wordOffsetPoints) =>
        checked(rubyFontSizeHalfPoints + wordOffsetPoints * 2);
}
