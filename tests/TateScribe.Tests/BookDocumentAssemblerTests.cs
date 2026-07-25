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

    [Fact]
    public void Assemble_turns_detected_paragraph_breaks_into_document_paragraphs()
    {
        var document = BookDocumentAssembler.Assemble(["甲\n乙", "丙"]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal("甲", paragraph.Text),
            paragraph => Assert.Equal("乙丙", paragraph.Text));
    }
}
