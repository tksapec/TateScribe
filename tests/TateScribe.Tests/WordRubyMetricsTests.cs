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
}
