namespace TateScribe.Core.Export;

public static class BookDocumentAssembler
{
    public static ExportDocument Assemble(IEnumerable<string> pageTexts)
    {
        var text = string.Concat(pageTexts.Where(text => !string.IsNullOrWhiteSpace(text)));
        var paragraphs = new List<ExportParagraph>();
        foreach (var sourceLine in text.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = sourceLine.Trim();
            if (line.StartsWith("[[PAGE:", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal)) continue;
            if (TryCreateStructuredParagraph(line, "[[CHAPTER:", ExportStyle.Heading1, DocumentElementRole.ChapterTitle, out var chapter)
                || TryCreateStructuredParagraph(line, "[[TITLE:", ExportStyle.Heading1, DocumentElementRole.ChapterTitle, out chapter)
                || TryCreateStructuredParagraph(line, "[[SECTION_TITLE:", ExportStyle.Heading2, DocumentElementRole.SectionTitle, out chapter)
                || TryCreateStructuredParagraph(line, "[[SECTION:", ExportStyle.Normal, DocumentElementRole.SectionNumber, out chapter))
            {
                paragraphs.Add(chapter!);
                continue;
            }
            var role = line is "＊" or "***" or "---" ? DocumentElementRole.SceneBreak : DocumentElementRole.BodyParagraph;
            paragraphs.Add(new ExportParagraph(ExportStyle.Normal, line, null, role));
        }
        return new ExportDocument(paragraphs);
    }

    private static bool TryCreateStructuredParagraph(string line, string prefix, ExportStyle style, DocumentElementRole role, out ExportParagraph? paragraph)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
        {
            paragraph = new ExportParagraph(style, line[prefix.Length..^2], null, role);
            return true;
        }
        paragraph = null;
        return false;
    }
}
