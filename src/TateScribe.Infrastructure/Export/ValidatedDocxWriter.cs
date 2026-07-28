using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace TateScribe.Infrastructure.Export;

/// <summary>Creates and validates a DOCX before atomically publishing it.</summary>
public static class ValidatedDocxWriter
{
    public static async Task WriteAsync(
        string destinationPath,
        Func<string, CancellationToken, Task> writeTemporary,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeTemporary);
        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination)
            ?? throw new ArgumentException("The destination must have a parent directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(fullDestination)}.{Guid.NewGuid():N}.tmp.docx");
        try
        {
            await writeTemporary(temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using (var document = WordprocessingDocument.Open(temporaryPath, false))
            {
                var errors = new OpenXmlValidator().Validate(document).ToArray();
                if (errors.Length > 0)
                    throw new InvalidDataException("Generated DOCX failed Open XML validation: "
                        + string.Join(Environment.NewLine, errors.Select(error => error.Description)));
            }

            if (File.Exists(fullDestination))
                File.Replace(temporaryPath, fullDestination, null);
            else
                File.Move(temporaryPath, fullDestination);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
