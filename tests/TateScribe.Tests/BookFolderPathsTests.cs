using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class BookFolderPathsTests
{
    [Fact]
    public void Document_path_uses_book_folder_name_inside_that_folder()
    {
        var path = BookFolderPaths.GetDocumentPath("C:\\Books\\7つの会議");

        Assert.Equal("C:\\Books\\7つの会議\\7つの会議.docx", path);
    }
}
