using System.Text;
using System.Text.RegularExpressions;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Ruby;

namespace TateScribe.Core.Export;

public sealed record ExportPageText(
    string Text,
    BoundaryJoinType JoinToNext = BoundaryJoinType.DirectJoin);

public sealed record ExportSourcePageText(
    Guid PageId,
    string PageMarker,
    string Text,
    BoundaryJoinType JoinToNext = BoundaryJoinType.DirectJoin);

public sealed record SourceAwareExportParagraph(
    ExportParagraph Paragraph,
    IReadOnlyList<SourceSpan> SourceSpans);

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
        return new ExportDocument(ParseParagraphs(text).Select(item => item.Paragraph).ToArray());
    }

    public static IReadOnlyList<SourceAwareExportParagraph> AssembleWithSourceSpans(
        IEnumerable<ExportSourcePageText> pageTexts)
    {
        var joined = JoinSourcePageTexts(pageTexts);
        return ParseParagraphs(joined.Text)
            .Select(parsed => new SourceAwareExportParagraph(
                parsed.Paragraph,
                CreateSourceSpans(parsed, joined.Owners)))
            .ToArray();
    }

    private static IReadOnlyList<ParsedParagraph> ParseParagraphs(string text)
    {
        var paragraphs = new List<ParsedParagraph>();
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var lineStart = 0;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var rawStart = lineStart;
            lineStart += rawLine.Length;
            if (lineStart < text.Length)
                lineStart += text[lineStart] == '\r' && lineStart + 1 < text.Length && text[lineStart + 1] == '\n'
                    ? 2
                    : 1;
            if (rawLine.Length == 0)
            {
                if (lineIndex < lines.Length - 1)
                    paragraphs.Add(new ParsedParagraph(
                        new ExportParagraph(
                            ExportStyle.Normal, string.Empty, null, DocumentElementRole.BodyParagraph),
                        rawStart, 0));
                continue;
            }
            var marker = rawLine.Trim();
            if (marker.StartsWith("[[PAGE:", StringComparison.Ordinal)
                && marker.EndsWith("]]", StringComparison.Ordinal))
                continue;
            var markerStart = rawStart + rawLine.IndexOf(marker, StringComparison.Ordinal);
            if (TryCreateStructuredParagraph(
                    marker, markerStart, "[[CHAPTER:", ExportStyle.Heading1,
                    DocumentElementRole.ChapterTitle, out var structured)
                || TryCreateStructuredParagraph(
                    marker, markerStart, "[[TITLE:", ExportStyle.Heading1,
                    DocumentElementRole.ChapterTitle, out structured)
                || TryCreateStructuredParagraph(
                    marker, markerStart, "[[SECTION_TITLE:", ExportStyle.Heading2,
                    DocumentElementRole.SectionTitle, out structured)
                || TryCreateStructuredParagraph(
                    marker, markerStart, "[[SECTION:", ExportStyle.Normal,
                    DocumentElementRole.SectionNumber, out structured))
            {
                paragraphs.Add(structured!);
                continue;
            }
            var role = marker is "※" or "***" or "---"
                ? DocumentElementRole.SceneBreak
                : DocumentElementRole.BodyParagraph;
            paragraphs.Add(new ParsedParagraph(
                new ExportParagraph(ExportStyle.Normal, rawLine, null, role),
                rawStart, rawLine.Length));
        }
        return paragraphs;
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
            if (index + 1 >= pages.Length
                || pageText.EndsWith('\n')
                || pages[index + 1].Text.StartsWith('\n'))
                continue;
            switch (page.JoinToNext)
            {
                case BoundaryJoinType.SpaceJoin:
                    if (!char.IsWhiteSpace(builder[^1])) builder.Append(' ');
                    break;
                case BoundaryJoinType.ParagraphBreak:
                    builder.Append('\n');
                    break;
                case BoundaryJoinType.SceneBreak:
                    builder.Append("\n※\n");
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

    private static JoinedSourceText JoinSourcePageTexts(IEnumerable<ExportSourcePageText> pageTexts)
    {
        var pages = pageTexts.Where(page => page.Text.Length > 0).ToArray();
        var builder = new StringBuilder();
        var owners = new List<SourceOwner>();
        for (var index = 0; index < pages.Length; index++)
        {
            var page = pages[index];
            var pageText = IsStandaloneStructureMarker(page.Text) ? $"{page.Text}\n" : page.Text;
            Append(pageText, page.PageId, page.PageMarker);
            if (index + 1 >= pages.Length
                || pageText.EndsWith('\n')
                || pages[index + 1].Text.StartsWith('\n'))
                continue;
            switch (page.JoinToNext)
            {
                case BoundaryJoinType.SpaceJoin:
                    if (!char.IsWhiteSpace(builder[^1]))
                        Append(" ", page.PageId, page.PageMarker);
                    break;
                case BoundaryJoinType.ParagraphBreak:
                    Append("\n", page.PageId, page.PageMarker);
                    break;
                case BoundaryJoinType.SceneBreak:
                    Append("\n※\n", page.PageId, page.PageMarker);
                    break;
                case BoundaryJoinType.DirectJoin:
                case BoundaryJoinType.Uncertain:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(page.JoinToNext));
            }
        }
        return new JoinedSourceText(builder.ToString(), owners);

        void Append(string value, Guid pageId, string marker)
        {
            builder.Append(value);
            for (var offset = 0; offset < value.Length; offset++)
                owners.Add(new SourceOwner(pageId, marker));
        }
    }

    private static bool TryCreateStructuredParagraph(
        string line,
        int lineStart,
        string prefix,
        ExportStyle style,
        DocumentElementRole role,
        out ParsedParagraph? paragraph)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal)
            && line.EndsWith("]]", StringComparison.Ordinal))
        {
            paragraph = new ParsedParagraph(
                new ExportParagraph(style, line[prefix.Length..^2], null, role),
                lineStart + prefix.Length,
                line.Length - prefix.Length - 2);
            return true;
        }
        paragraph = null;
        return false;
    }

    private static IReadOnlyList<SourceSpan> CreateSourceSpans(
        ParsedParagraph paragraph,
        IReadOnlyList<SourceOwner> owners)
    {
        if (owners.Count == 0) return [];
        if (paragraph.ContentLength == 0)
        {
            var ownerIndex = Math.Clamp(paragraph.ContentStart - 1, 0, owners.Count - 1);
            var owner = owners[ownerIndex];
            return [new SourceSpan(owner.PageId, owner.PageMarker, 0, 0)];
        }

        var spans = new List<SourceSpan>();
        var end = Math.Min(owners.Count, paragraph.ContentStart + paragraph.ContentLength);
        var segmentStart = paragraph.ContentStart;
        var current = owners[segmentStart];
        for (var index = segmentStart + 1; index <= end; index++)
        {
            if (index < end && owners[index] == current) continue;
            spans.Add(new SourceSpan(
                current.PageId,
                current.PageMarker,
                segmentStart - paragraph.ContentStart,
                index - segmentStart));
            if (index < end)
            {
                segmentStart = index;
                current = owners[index];
            }
        }
        return spans;
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

    [GeneratedRegex(
        @"^(第.{1,12}[章話部巻]|序章|終章|プロローグ|エピローグ)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ChapterNumberPattern();

    private sealed record ParsedParagraph(
        ExportParagraph Paragraph,
        int ContentStart,
        int ContentLength);

    private sealed record SourceOwner(Guid PageId, string PageMarker);

    private sealed record JoinedSourceText(
        string Text,
        IReadOnlyList<SourceOwner> Owners);
}
