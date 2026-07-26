namespace TateScribe.Tests;

internal static class TestFileCleanup
{
    public static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }
}
