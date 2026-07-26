using System.Globalization;
using System.Text;

namespace TateScribe.Core.Ruby;

public static class RubyTextNormalizer
{
    public static string NormalizeReading(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character)) continue;
            result.Append(character is >= '\u30a1' and <= '\u30f6'
                ? (char)(character - 0x60)
                : character);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
