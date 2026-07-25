namespace TateScribe.Core.Export;

public static class BookDocumentAssembler
{
    public static ExportDocument Assemble(IEnumerable<string> pageTexts)
    {
        var text = string.Concat(pageTexts.Where(text => !string.IsNullOrWhiteSpace(text)));
        var paragraphs = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => new ExportParagraph(ExportStyle.Normal, paragraph))
            .ToArray();
        return new ExportDocument(paragraphs);
    }
}
