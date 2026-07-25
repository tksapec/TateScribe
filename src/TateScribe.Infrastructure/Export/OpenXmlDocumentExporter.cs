using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TateScribe.Core.Export;

namespace TateScribe.Infrastructure.Export;

public sealed class OpenXmlDocumentExporter : IDocumentExporter
{
    public Task ExportAsync(ExportDocument document, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        using var word = WordprocessingDocument.Create(destinationPath, WordprocessingDocumentType.Document);
        var mainPart = word.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        foreach (var item in document.Paragraphs)
        {
            var properties = new ParagraphProperties(new ParagraphStyleId { Val = item.Style.ToString() });
            if (document.PageBreakBeforeChapters && item.Role == DocumentElementRole.ChapterTitle) properties.Append(new PageBreakBefore());
            var paragraph = new Paragraph(properties);
            if (item.Ruby is null)
            {
                paragraph.Append(new Run(new Text(item.Text) { Space = SpaceProcessingModeValues.Preserve }));
            }
            else
            {
                paragraph.Append(CreateRuby(item.Ruby));
            }
            mainPart.Document.Body!.Append(paragraph);
        }
        mainPart.Document.Body!.Append(new SectionProperties(
            new PageSize { Width = 11906U, Height = 16838U },
            new PageMargin { Top = 1134, Right = 1134U, Bottom = 1134, Left = 1134U, Header = 708U, Footer = 708U, Gutter = 0U }));
        mainPart.Document.Save();
        return Task.CompletedTask;
    }

    private static OpenXmlElement CreateRuby(RubyAnnotation ruby)
    {
        var element = new OpenXmlUnknownElement("w", "ruby", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        element.InnerXml = $"<w:rubyPr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/><w:rt xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:r><w:t>{Escape(ruby.RubyText)}</w:t></w:r></w:rt><w:rubyBase xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:r><w:t>{Escape(ruby.ParentText)}</w:t></w:r></w:rubyBase>";
        return element;
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
