namespace TateScribe.Core.Projects;

public static class PageRotationEditor
{
    public static ProjectPage Rotate(ProjectPage page, int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees));
        var rotation = ((page.RotationDegrees + degrees) % 360 + 360) % 360;
        return page with { RotationDegrees = rotation };
    }
}
