using System.Text;
using System.Text.RegularExpressions;
using TateScribe.Core.Proofreading;

namespace TateScribe.Core.Export;

public sealed record ExportPageText(
    string Text,
    BoundaryJoinType JoinToNext = BoundaryJoinType.DirectJoin);

public static partial class BookDocumentAssembler
{
    public static string CreateChapterPageText(string text)
    {
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var titleIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (titleIndex < 0) return string.Empty;
        var chapter = lines[titleIndex].Trim();
        var bodyStart = titleIndex + 1;
        string? title = null;
        if (ChapterNumberPattern().IsMatch(chapter)
            && bodyStart < lines.Length
            && !string.IsNullOrWhiteSpace(lines[bodyStart])
            && lines[bodyStart].Trim().Length <= 40)
        {
            title = lines[bodyStart].Trim();
            bodyStart++;
        }
        var builder = new StringBuilder($"[[CHAPTER:{chapter}]]");
        if (title is not null) builder.Append($"\n[[TITLE:{title}]]");
        if (bodyStart < lines.Length) builder.Append('\n').Append(string.Join("\n", lines[bodyStart..]));
        return builder.ToString();
    }

    public static ExportDocument Assemble(IEnumerable<string> pageTexts) =>
        Assemble(pageTexts.Select(text => new ExportPageText(text)));

    public static ExportDocument Assemble(IEnumerable<ExportPageText> pageTexts)
    {
        var text = JoinPageTexts(pageTexts);
        var paragraphs = new List<ExportParagraph>();
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            if (rawLine.Length == 0)
            {
                if (lineIndex < lines.Length - 1)
                    paragraphs.Add(new ExportParagraph(
                        ExportStyle.Normal, string.Empty, null, DocumentElementRole.BodyParagraph));
                continue;
            }
            var marker = rawLine.Trim();
            if (marker.StartsWith("[[PAGE:", StringComparison.Ordinal) && marker.EndsWith("]]", StringComparison.Ordinal)) continue;
            if (TryCreateStructuredParagraph(marker, "[[CHAPTER:", ExportStyle.Heading1, DocumentElementRole.ChapterTitle, out var structured)
                || TryCreateStructuredParagraph(marker, "[[TITLE:", ExportStyle.Heading1, DocumentElementRole.ChapterTitle, out structured)
                || TryCreateStructuredParagraph(marker, "[[SECTION_TITLE:", ExportStyle.Heading2, DocumentElementRole.SectionTitle, out structured)
                || TryCreateStructuredParagraph(marker, "[[SECTION:", ExportStyle.Normal, DocumentElementRole.SectionNumber, out structured))
            {
                paragraphs.Add(structured!);
                continue;
            }
            var role = marker is "＊" or "***" or "---"
                ? DocumentElementRole.SceneBreak
                : DocumentElementRole.BodyParagraph;
            paragraphs.Add(new ExportParagraph(ExportStyle.Normal, rawLine, null, role));
        }
        return new ExportDocument(paragraphs);
    }

    private static string JoinPageTexts(IEnumerable<ExportPageText> pageTexts)
    {
        var pages = pageTexts.Where(page => page.Text.Length > 0).ToArray();
        var builder = new StringBuilder();
        for (var index = 0; index < pages.Length; index++)
        {
            var page = pages[index];
            var pageText = IsStandaloneStructureMarker(page.Text) ? $"{page.Text}\n" : page.Text;
            builder.Append(pageText);
            if (index + 1 >= pages.Length || pageText.EndsWith('\n') || pages[index + 1].Text.StartsWith('\n')) continue;
            switch (page.JoinToNext)
            {
                case BoundaryJoinType.SpaceJoin:
                    if (!char.IsWhiteSpace(builder[^1])) builder.Append(' ');
                    break;
                case BoundaryJoinType.ParagraphBreak:
                    builder.Append('\n');
                    break;
                case BoundaryJoinType.SceneBreak:
                    builder.Append("\n＊\n");
                    break;
                case BoundaryJoinType.DirectJoin:
                case BoundaryJoinType.Uncertain:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(page.JoinToNext));
            }
        }
        return builder.ToString();
    }

    private static bool TryCreateStructuredParagraph(
        string line,
        string prefix,
        ExportStyle style,
        DocumentElementRole role,
        out ExportParagraph? paragraph)
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

    [GeneratedRegex(@"^(第.{1,12}[章話部巻]|序章|終章|プロローグ|エピローグ)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChapterNumberPattern();
}
