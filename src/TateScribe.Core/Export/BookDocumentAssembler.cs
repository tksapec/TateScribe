namespace TateScribe.Core.Export;

public static class BookDocumentAssembler
{
    public static ExportDocument Assemble(IEnumerable<string> pageTexts)
    {
        var text = string.Concat(pageTexts.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new ExportDocument([new ExportParagraph(ExportStyle.Normal, text)]);
    }
}
