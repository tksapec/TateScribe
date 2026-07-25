using System.Globalization;
using System.Text.RegularExpressions;

namespace TateScribe.Core.Pages;

public sealed record PageSortCandidate(
    string FileName,
    DateTimeOffset? ExifTimestamp,
    DateTimeOffset? CreatedTimestamp,
    DateTimeOffset? ModifiedTimestamp,
    DateTimeOffset? FileNameTimestamp);

public static partial class PageOrdering
{
    public static IReadOnlyList<PageSortCandidate> Sort(IEnumerable<PageSortCandidate> pages) =>
        pages.OrderBy(PrimaryTimestamp)
            .ThenBy(x => x.FileName, NaturalFileNameComparer.Instance)
            .ToArray();

    public static DateTimeOffset? GetFileNameTimestamp(string fileName)
    {
        var match = ScreenshotTimestampRegex().Match(fileName);
        return match.Success && DateTimeOffset.TryParseExact(
            match.Groups[1].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var timestamp)
            ? timestamp
            : null;
    }

    private static DateTimeOffset? PrimaryTimestamp(PageSortCandidate page) =>
        page.FileNameTimestamp ?? GetFileNameTimestamp(page.FileName) ?? page.ExifTimestamp ??
        page.CreatedTimestamp ?? page.ModifiedTimestamp;

    [GeneratedRegex("(\\d{8}_\\d{6})", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenshotTimestampRegex();

    private sealed class NaturalFileNameComparer : IComparer<string>
    {
        public static readonly NaturalFileNameComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var xParts = Regex.Split(x, "(\\d+)");
            var yParts = Regex.Split(y, "(\\d+)");
            for (var i = 0; i < Math.Min(xParts.Length, yParts.Length); i++)
            {
                if (long.TryParse(xParts[i], out var xNumber) && long.TryParse(yParts[i], out var yNumber))
                {
                    var numericResult = xNumber.CompareTo(yNumber);
                    if (numericResult != 0) return numericResult;
                }
                else
                {
                    var textResult = StringComparer.OrdinalIgnoreCase.Compare(xParts[i], yParts[i]);
                    if (textResult != 0) return textResult;
                }
            }
            return xParts.Length.CompareTo(yParts.Length);
        }
    }
}
