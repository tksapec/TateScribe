using TateScribe.Core.Export;

namespace TateScribe.Tests;

public sealed class WordRubyMetricsTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 16)]
    [InlineData(20, 50)]
    public void Offset_maps_to_provisional_raise(int offset, int expected) =>
        Assert.Equal(expected, WordRubyMetrics.CalculateRaiseHalfPoints(10, offset));

    [Theory]
    [InlineData("", false)]
    [InlineData("-1", false)]
    [InlineData("21", false)]
    [InlineData("3", true)]
    public void Options_validate_word_offset(string value, bool valid) =>
        Assert.Equal(valid, DocxRubyOptions.TryCreate(value, out _, out _));

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Options_constructor_rejects_offsets_outside_word_range(int wordOffsetPoints) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocxRubyOptions(wordOffsetPoints));

    [Fact]
    public void Documentation_names_the_manual_Word_visual_verification_boundary()
    {
        var root = FindRepositoryRoot();

        Assert.Contains("3pt default", File.ReadAllText(Path.Combine(root, "README.md")), StringComparison.Ordinal);
        Assert.Contains("0 through 20", File.ReadAllText(Path.Combine(root, "USER_GUIDE.md")), StringComparison.Ordinal);
        Assert.Contains("Ctrl/Shift", File.ReadAllText(Path.Combine(root, "USER_GUIDE.md")), StringComparison.Ordinal);
        Assert.Contains("Ctrl+Enter", File.ReadAllText(Path.Combine(root, "USER_GUIDE.md")), StringComparison.Ordinal);
        Assert.Contains("button-only rejection", File.ReadAllText(Path.Combine(root, "TEST_PLAN.md")), StringComparison.Ordinal);
        Assert.Contains("manual Word visual verification", File.ReadAllText(Path.Combine(root, "TEST_PLAN.md")), StringComparison.Ordinal);
        Assert.Contains("no SQLite schema migration", File.ReadAllText(Path.Combine(root, "SPEC.md")), StringComparison.Ordinal);
        Assert.Contains("provisional", File.ReadAllText(Path.Combine(root, "ARCHITECTURE.md")), StringComparison.Ordinal);
        Assert.Contains("manual Word visual verification", File.ReadAllText(Path.Combine(root, "docs", "ADR-0001-structured-ruby-boundary.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Documentation_explains_the_provisional_half_point_raise_formula()
    {
        var root = FindRepositoryRoot();

        foreach (var path in new[] { "README.md", "USER_GUIDE.md" })
        {
            var text = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains("hpsRaise = rubyFontSizeHalfPoints + wordOffsetPoints * 2", text, StringComparison.Ordinal);
            Assert.Contains("half-points", text, StringComparison.Ordinal);
            Assert.Contains("not a direct equivalence with Word's displayed offset", text, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TateScribe.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
