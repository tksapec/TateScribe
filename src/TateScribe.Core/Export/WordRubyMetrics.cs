namespace TateScribe.Core.Export;

public static class WordRubyMetrics
{
    // Word's UI offset is a user-facing point value; w:hpsRaise is not that value.
    // Keep this provisional conversion until Word-saved A/B/C reference files are available.
    public static int CalculateRaiseHalfPoints(int rubyFontSizeHalfPoints, int wordOffsetPoints) =>
        checked(rubyFontSizeHalfPoints + wordOffsetPoints * 2);
}
