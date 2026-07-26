using System.Security.Cryptography;
using System.Text;
using OpenCvSharp;
using TateScribe.Core.Images;

namespace TateScribe.Infrastructure.Images;

public sealed class ScreenshotPreprocessor
{
    public async Task<PreparedImage> PrepareAsync(string sourcePath, string cacheDirectory, NormalizedCrop crop, int rotationDegrees, CancellationToken cancellationToken)
    {
        crop.Validate();
        if (rotationDegrees is not (0 or 90 or 180 or 270)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        Directory.CreateDirectory(cacheDirectory);
        await using var sourceStream = File.OpenRead(sourcePath);
        var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(sourceStream, cancellationToken));
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceHash}|{crop.Left:R}|{crop.Top:R}|{crop.Right:R}|{crop.Bottom:R}|{rotationDegrees}")));
        var cachePath = Path.Combine(cacheDirectory, $"{cacheKey}.png");
        if (File.Exists(cachePath))
        {
            using var cached = Cv2.ImRead(cachePath, ImreadModes.Grayscale);
            return new PreparedImage(cachePath, cached.Width, cached.Height, cacheKey);
        }

        await Task.Run(() => CreateCache(sourcePath, cachePath, crop, rotationDegrees), cancellationToken);
        using var image = Cv2.ImRead(cachePath, ImreadModes.Grayscale);
        return new PreparedImage(cachePath, image.Width, image.Height, cacheKey);
    }

    private static void CreateCache(string sourcePath, string cachePath, NormalizedCrop crop, int rotationDegrees)
    {
        using var source = Cv2.ImRead(sourcePath, ImreadModes.Color);
        if (source.Empty()) throw new InvalidDataException("The image could not be decoded.");
        using var rotated = new Mat();
        Rotate(source, rotated, rotationDegrees);
        var rectangle = new Rect(
            (int)Math.Floor(rotated.Width * crop.Left),
            (int)Math.Floor(rotated.Height * crop.Top),
            Math.Max(1, (int)Math.Ceiling(rotated.Width * (crop.Right - crop.Left))),
            Math.Max(1, (int)Math.Ceiling(rotated.Height * (crop.Bottom - crop.Top))));
        rectangle.Width = Math.Min(rectangle.Width, rotated.Width - rectangle.X);
        rectangle.Height = Math.Min(rectangle.Height, rotated.Height - rectangle.Y);
        using var cropped = new Mat(rotated, rectangle);
        using var gray = new Mat();
        Cv2.CvtColor(cropped, gray, ColorConversionCodes.BGR2GRAY);
        if (Cv2.Mean(gray).Val0 < 128) Cv2.BitwiseNot(gray, gray);
        Cv2.ImWrite(cachePath, gray);
    }

    private static void Rotate(Mat source, Mat destination, int rotationDegrees)
    {
        switch (rotationDegrees)
        {
            case 0: source.CopyTo(destination); break;
            case 90: Cv2.Rotate(source, destination, RotateFlags.Rotate90Clockwise); break;
            case 180: Cv2.Rotate(source, destination, RotateFlags.Rotate180); break;
            case 270: Cv2.Rotate(source, destination, RotateFlags.Rotate90Counterclockwise); break;
            default: throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        }
    }
}
