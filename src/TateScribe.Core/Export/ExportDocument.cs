namespace TateScribe.Core.Export;

public enum ExportStyle { Normal, Heading1, Heading2, Heading3 }

public sealed record RubyAnnotation(string ParentText, string RubyText);

public sealed record ExportParagraph(ExportStyle Style, string Text, RubyAnnotation? Ruby = null);

public sealed record ExportDocument(IReadOnlyList<ExportParagraph> Paragraphs);

public interface IDocumentExporter
{
    Task ExportAsync(ExportDocument document, string destinationPath, CancellationToken cancellationToken);
}
