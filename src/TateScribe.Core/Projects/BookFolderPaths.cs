namespace TateScribe.Core.Projects;

public static class BookFolderPaths
{
    public static string GetDocumentPath(string bookFolder)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bookFolder));
        var bookName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(bookName)) throw new ArgumentException("Book folder must have a name.", nameof(bookFolder));
        return Path.Combine(normalized, $"{bookName}.docx");
    }
}
