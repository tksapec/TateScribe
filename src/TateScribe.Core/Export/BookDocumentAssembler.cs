namespace TateScribe.Core.Export;

public static class BookDocumentAssembler
{
    public static string CreateChapterPageText(string text)
    {
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var titleIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (titleIndex < 0) return string.Empty;
        var title = lines[titleIndex].Trim();
        var body = lines[(titleIndex + 1)..];
        return body.Length == 0
            ? $"[[CHAPTER:{title}]]"
            : $"[[CHAPTER:{title}]]\n{string.Join("\n", body)}";
    }

    public static ExportDocument Assemble(IEnumerable<string> pageTexts)
    {
        var text = string.Concat(pageTexts.Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(pageText => IsStandaloneStructureMarker(pageText) ? $"{pageText}\n" : pageText));
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

    private static bool IsStandaloneStructureMarker(string text)
    {
        var line = text.Trim();
        return !line.Contains('\n')
            && line.EndsWith("]]", StringComparison.Ordinal)
            && (line.StartsWith("[[CHAPTER:", StringComparison.Ordinal)
                || line.StartsWith("[[TITLE:", StringComparison.Ordinal)
                || line.StartsWith("[[SECTION_TITLE:", StringComparison.Ordinal)
                || line.StartsWith("[[SECTION:", StringComparison.Ordinal));
    }
}
