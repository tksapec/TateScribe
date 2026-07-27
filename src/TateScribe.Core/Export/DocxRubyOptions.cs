using System.Globalization;

namespace TateScribe.Core.Export;

public sealed record DocxRubyOptions
{
    public DocxRubyOptions(int WordOffsetPoints = 3, int RubyFontSizeHalfPoints = 10)
    {
        if (WordOffsetPoints is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(WordOffsetPoints), WordOffsetPoints, "Word ruby offset must be from 0 through 20.");

        this.WordOffsetPoints = WordOffsetPoints;
        this.RubyFontSizeHalfPoints = RubyFontSizeHalfPoints;
    }

    public int WordOffsetPoints { get; }

    public int RubyFontSizeHalfPoints { get; }

    public static DocxRubyOptions Default { get; } = new();

    public static bool TryCreate(string? value, out DocxRubyOptions options, out string error)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wordOffsetPoints)
            || wordOffsetPoints is < 0 or > 20)
        {
            options = Default;
            error = "Word ruby offset must be a whole number from 0 through 20.";
            return false;
        }

        options = new DocxRubyOptions(wordOffsetPoints);
        error = string.Empty;
        return true;
    }
}
