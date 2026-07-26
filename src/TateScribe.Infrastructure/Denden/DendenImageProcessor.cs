using OpenCvSharp;

namespace TateScribe.Infrastructure.Denden;

internal sealed record PreparedDendenImage(string FileName, byte[] Bytes);

internal static class DendenImageProcessor
{
    internal const long MaximumImageBytes = 3L * 1024 * 1024;

    public static PreparedDendenImage Prepare(string sourcePath, string outputStem)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("でんでん用データへ出力する画像が見つかりません。", sourcePath);

        var source = File.ReadAllBytes(sourcePath);
        var format = DetectFormat(source);
        var prepared = format switch
        {
            DendenImageFormat.Png => PreserveDecoded(source, outputStem, "png", sourcePath),
            DendenImageFormat.Jpeg => PreserveDecoded(source, outputStem, "jpg", sourcePath),
            DendenImageFormat.Gif => PreserveGif(source, outputStem, sourcePath),
            _ => ConvertToPng(source, outputStem, sourcePath),
        };
        if (prepared.Bytes.LongLength > MaximumImageBytes)
        {
            var size = prepared.Bytes.LongLength / (1024d * 1024d);
            throw new InvalidOperationException(
                $"画像「{prepared.FileName}」は{size:0.00} MBで、上限3 MBを超えています。");
        }
        return prepared;
    }

    private static PreparedDendenImage PreserveDecoded(
        byte[] source,
        string outputStem,
        string extension,
        string sourcePath)
    {
        try
        {
            using var decoded = Cv2.ImDecode(source, ImreadModes.Unchanged);
            if (decoded.Empty())
                throw new InvalidDataException("画像をデコードできません。");
            return new PreparedDendenImage($"{outputStem}.{extension}", source);
        }
        catch (Exception exception) when (exception is OpenCVException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"画像「{Path.GetFileName(sourcePath)}」は有効な{extension.ToUpperInvariant()}画像ではありません。",
                exception);
        }
    }

    private static PreparedDendenImage PreserveGif(
        byte[] source,
        string outputStem,
        string sourcePath)
    {
        if (source.Length < 14
            || source[^1] != 0x3b
            || Array.IndexOf(source, (byte)0x2c, 13) < 0)
            throw new InvalidDataException(
                $"画像「{Path.GetFileName(sourcePath)}」は有効なGIF画像ではありません。");
        try
        {
            using var decoded = Cv2.ImDecode(source, ImreadModes.Unchanged);
            if (decoded.Empty())
                throw new InvalidDataException("GIF画像をデコードできません。");
            return new PreparedDendenImage($"{outputStem}.gif", source);
        }
        catch (Exception exception) when (exception is OpenCVException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"画像「{Path.GetFileName(sourcePath)}」は有効なGIF画像ではありません。",
                exception);
        }
    }

    private static PreparedDendenImage ConvertToPng(
        byte[] source,
        string outputStem,
        string sourcePath)
    {
        try
        {
            using var decoded = Cv2.ImDecode(source, ImreadModes.Unchanged);
            if (decoded.Empty())
                throw new InvalidDataException("画像をデコードできません。");
            Cv2.ImEncode(
                ".png",
                decoded,
                out var encoded,
                new ImageEncodingParam(ImwriteFlags.PngCompression, 9));
            return new PreparedDendenImage($"{outputStem}.png", encoded);
        }
        catch (Exception exception) when (exception is OpenCVException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"画像「{Path.GetFileName(sourcePath)}」をPNGへ変換できません。", exception);
        }
    }

    private static DendenImageFormat DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return DendenImageFormat.Png;
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return DendenImageFormat.Jpeg;
        if (bytes.Length >= 6
            && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
            return DendenImageFormat.Gif;
        return DendenImageFormat.Unsupported;
    }

    private enum DendenImageFormat
    {
        Unsupported,
        Png,
        Jpeg,
        Gif,
    }
}
