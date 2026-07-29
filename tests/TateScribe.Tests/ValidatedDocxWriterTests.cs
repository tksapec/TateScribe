using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TateScribe.Infrastructure.Export;

namespace TateScribe.Tests;

public sealed class ValidatedDocxWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"TateScribe-{Guid.NewGuid():N}");

    [Fact]
    public async Task Successful_write_replaces_existing_document_and_removes_temporary_file()
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "book.docx");
        await CreateDocumentAsync(destination, "old", CancellationToken.None);

        await ValidatedDocxWriter.WriteAsync(destination,
            (path, token) => CreateDocumentAsync(path, "new", token), CancellationToken.None);

        Assert.Equal("new", ReadText(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp.docx"));
    }

    [Fact]
    public async Task Generator_failure_preserves_existing_document_and_removes_temporary_file()
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "book.docx");
        await CreateDocumentAsync(destination, "old", CancellationToken.None);
        var original = await File.ReadAllBytesAsync(destination);

        await Assert.ThrowsAsync<IOException>(() => ValidatedDocxWriter.WriteAsync(destination,
            async (path, token) =>
            {
                await File.WriteAllTextAsync(path, "partial", token);
                throw new IOException("generation failed");
            }, CancellationToken.None));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp.docx"));
    }

    private static Task CreateDocumentAsync(string path, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
        main.Document.Save();
        return Task.CompletedTask;
    }

    private static string ReadText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart!.Document.Body!.InnerText;
    }

    public void Dispose() => TestFileCleanup.DeleteDirectory(directory);
}
