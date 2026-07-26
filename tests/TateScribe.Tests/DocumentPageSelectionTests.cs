using TateScribe.Core.Export;
using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class DocumentPageSelectionTests
{
    [Theory]
    [InlineData(PageRole.Body, "本文", true)]
    [InlineData(PageRole.MixedTitleAndBody, "題\n本文", true)]
    [InlineData(PageRole.Other, "本文かもしれない", true)]
    [InlineData(PageRole.Other, "", false)]
    [InlineData(PageRole.Illustration, "画像内文字", false)]
    [InlineData(PageRole.Blank, "誤検出", false)]
    public void IncludeInDocx_never_silently_discards_text_from_other_pages(
        PageRole role, string text, bool expected)
    {
        Assert.Equal(expected, DocumentPageSelection.IncludeInDocx(role, text));
    }
}
