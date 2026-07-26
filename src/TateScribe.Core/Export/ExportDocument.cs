namespace TateScribe.Core.Export;

using TateScribe.Core.Ruby;

public enum ExportStyle { Normal, Heading1, Heading2, Heading3 }

public enum DocumentElementRole
{
    ChapterTitle,
    SectionTitle,
    SectionNumber,
    BodyParagraph,
    SceneBreak
}

public sealed record RubyAnnotation(string ParentText, string RubyText);

public sealed record ExportParagraph(ExportStyle Style, string Text, RubyAnnotation? Ruby = null, DocumentElementRole Role = DocumentElementRole.BodyParagraph);

public sealed record ExportDocument(
    IReadOnlyList<ExportParagraph> Paragraphs,
    bool PageBreakBeforeChapters = false,
    string JapaneseFontName = "游明朝");

public interface IDocumentExporter
{
    Task ExportAsync(ExportDocument document, string destinationPath, CancellationToken cancellationToken);
}

public interface IStructuredDocumentExporter
{
    Task ExportAsync(
        StructuredDocument document,
        string destinationPath,
        bool pageBreakBeforeChapters,
        string japaneseFontName,
        CancellationToken cancellationToken);
}
