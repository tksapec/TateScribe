using TateScribe.Core.Export;

namespace TateScribe.Tests;

public sealed class BookDocumentAssemblerTests
{
    [Fact]
    public void Assemble_joins_page_text_without_screenshot_boundary_paragraphs()
    {
        var document = BookDocumentAssembler.Assemble(["前ページ", "次ページ"]);

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(ExportStyle.Normal, paragraph.Style);
        Assert.Equal("前ページ次ページ", paragraph.Text);
    }
}
