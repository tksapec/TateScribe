using TateScribe.Core.Projects;

namespace TateScribe.Tests;

public sealed class PageRotationEditorTests
{
    [Theory]
    [InlineData(0, 90, 90)]
    [InlineData(270, 90, 0)]
    [InlineData(0, -90, 270)]
    public void Rotate_normalizes_to_clockwise_quarter_turns(int current, int adjustment, int expected)
    {
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, current);

        var result = PageRotationEditor.Rotate(page, adjustment);

        Assert.Equal(expected, result.RotationDegrees);
    }
}
