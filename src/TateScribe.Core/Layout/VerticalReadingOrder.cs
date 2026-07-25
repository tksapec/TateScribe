namespace TateScribe.Core.Layout;

public sealed record Glyph(string Text, double X, double Y);

public static class VerticalReadingOrder
{
    public static IReadOnlyList<Glyph> Order(IEnumerable<Glyph> glyphs, double columnTolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnTolerance);
        var columns = new List<List<Glyph>>();
        foreach (var glyph in glyphs.OrderByDescending(x => x.X))
        {
            var column = columns.FirstOrDefault(x => Math.Abs(x[0].X - glyph.X) <= columnTolerance);
            if (column is null)
            {
                column = [];
                columns.Add(column);
            }
            column.Add(glyph);
        }

        return columns.OrderByDescending(x => x.Average(g => g.X))
            .SelectMany(x => x.OrderBy(g => g.Y))
            .ToArray();
    }
}
