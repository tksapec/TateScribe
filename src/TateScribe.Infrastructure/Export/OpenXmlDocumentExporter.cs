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
        AddStyles(mainPart, document.JapaneseFontName);
        foreach (var item in document.Paragraphs)
        {
            var styleId = item.Role switch
            {
                DocumentElementRole.SectionNumber => "SectionNumber",
                DocumentElementRole.SceneBreak => "SceneBreak",
                _ => item.Style.ToString()
            };
            var properties = new ParagraphProperties(new ParagraphStyleId { Val = styleId });
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

    private static void AddStyles(MainDocumentPart mainPart, string japaneseFontName)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = japaneseFontName, HighAnsi = japaneseFontName, EastAsia = japaneseFontName },
                    new FontSize { Val = "21" },
                    new FontSizeComplexScript { Val = "21" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "0", Line = "360", LineRule = LineSpacingRuleValues.Auto }))),
            ParagraphStyle(
                "Normal", "Normal", isDefault: true,
                paragraphProperties: new ParagraphProperties(
                    new SpacingBetweenLines { After = "0", Line = "360", LineRule = LineSpacingRuleValues.Auto },
                    new Indentation { FirstLineChars = 100 })),
            ParagraphStyle(
                "Heading1", "heading 1", basedOn: "Normal",
                paragraphProperties: new ParagraphProperties(new KeepNext(), new Indentation { FirstLineChars = 0 }),
                runProperties: new RunProperties(new Bold(), new FontSize { Val = "32" }, new FontSizeComplexScript { Val = "32" })),
            ParagraphStyle(
                "Heading2", "heading 2", basedOn: "Normal",
                paragraphProperties: new ParagraphProperties(new KeepNext(), new Indentation { FirstLineChars = 0 }),
                runProperties: new RunProperties(new Bold(), new FontSize { Val = "28" }, new FontSizeComplexScript { Val = "28" })),
            ParagraphStyle(
                "Heading3", "heading 3", basedOn: "Normal",
                paragraphProperties: new ParagraphProperties(new KeepNext(), new Indentation { FirstLineChars = 0 }),
                runProperties: new RunProperties(new Bold(), new FontSize { Val = "24" }, new FontSizeComplexScript { Val = "24" })),
            ParagraphStyle(
                "SectionNumber", "Section Number", basedOn: "Normal",
                paragraphProperties: new ParagraphProperties(new Indentation { FirstLineChars = 0 }, new Justification { Val = JustificationValues.Center })),
            ParagraphStyle(
                "SceneBreak", "Scene Break", basedOn: "Normal",
                paragraphProperties: new ParagraphProperties(new Indentation { FirstLineChars = 0 }, new Justification { Val = JustificationValues.Center })));
        stylesPart.Styles.Save();
    }

    private static Style ParagraphStyle(
        string id,
        string name,
        string? basedOn = null,
        bool isDefault = false,
        ParagraphProperties? paragraphProperties = null,
        RunProperties? runProperties = null)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id, Default = isDefault };
        style.Append(new StyleName { Val = name });
        if (basedOn is not null) style.Append(new BasedOn { Val = basedOn });
        if (paragraphProperties is not null) style.Append(paragraphProperties);
        if (runProperties is not null) style.Append(runProperties);
        return style;
    }

    private static OpenXmlElement CreateRuby(RubyAnnotation ruby)
    {
        var element = new OpenXmlUnknownElement("w", "ruby", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        element.InnerXml = $"<w:rubyPr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/><w:rt xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:r><w:t>{Escape(ruby.RubyText)}</w:t></w:r></w:rt><w:rubyBase xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:r><w:t>{Escape(ruby.ParentText)}</w:t></w:r></w:rubyBase>";
        return element;
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
