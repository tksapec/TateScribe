using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace TateScribe.Infrastructure.Import;

public static class ImageTimestampReader
{
    private const ushort ExifIfdPointerTag = 0x8769;
    private const ushort DateTimeOriginalTag = 0x9003;
    private const ushort DateTimeDigitizedTag = 0x9004;

    public static DateTimeOffset? TryRead(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? ReadJpegExif(bytes)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ReadJpegExif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF) return null;
            var marker = bytes[offset + 1];
            offset += 2;
            if (marker is 0xD8 or 0xD9) continue;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || offset + length > bytes.Length) return null;
            var payload = bytes.Slice(offset + 2, length - 2);
            if (marker == 0xE1 && payload.Length >= 6 && payload[..6].SequenceEqual("Exif\0\0"u8))
                return ReadTiff(payload[6..]);
            offset += length;
        }
        return null;
    }

    private static DateTimeOffset? ReadTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        var littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!littleEndian && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M')) return null;
        if (ReadUInt16(tiff, 2, littleEndian) != 42) return null;
        var ifd0Offset = checked((int)ReadUInt32(tiff, 4, littleEndian));
        var exifOffset = FindLongTagValue(tiff, ifd0Offset, ExifIfdPointerTag, littleEndian);
        if (exifOffset is null) return null;
        return ReadAsciiTimestamp(tiff, checked((int)exifOffset.Value), DateTimeOriginalTag, littleEndian)
            ?? ReadAsciiTimestamp(tiff, checked((int)exifOffset.Value), DateTimeDigitizedTag, littleEndian);
    }

    private static uint? FindLongTagValue(ReadOnlySpan<byte> tiff, int ifdOffset, ushort tag, bool littleEndian)
    {
        foreach (var entryOffset in EnumerateEntries(tiff, ifdOffset, littleEndian))
        {
            if (ReadUInt16(tiff, entryOffset, littleEndian) == tag
                && ReadUInt16(tiff, entryOffset + 2, littleEndian) == 4
                && ReadUInt32(tiff, entryOffset + 4, littleEndian) == 1)
                return ReadUInt32(tiff, entryOffset + 8, littleEndian);
        }
        return null;
    }

    private static DateTimeOffset? ReadAsciiTimestamp(
        ReadOnlySpan<byte> tiff,
        int ifdOffset,
        ushort tag,
        bool littleEndian)
    {
        foreach (var entryOffset in EnumerateEntries(tiff, ifdOffset, littleEndian))
        {
            if (ReadUInt16(tiff, entryOffset, littleEndian) != tag
                || ReadUInt16(tiff, entryOffset + 2, littleEndian) != 2)
                continue;
            var count = checked((int)ReadUInt32(tiff, entryOffset + 4, littleEndian));
            var valueOffset = count <= 4 ? entryOffset + 8 : checked((int)ReadUInt32(tiff, entryOffset + 8, littleEndian));
            EnsureRange(tiff, valueOffset, count);
            var value = Encoding.ASCII.GetString(tiff.Slice(valueOffset, count)).TrimEnd('\0', ' ');
            return DateTimeOffset.TryParseExact(
                value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var timestamp)
                ? timestamp
                : null;
        }
        return null;
    }

    private static IReadOnlyList<int> EnumerateEntries(ReadOnlySpan<byte> tiff, int ifdOffset, bool littleEndian)
    {
        EnsureRange(tiff, ifdOffset, 2);
        var count = ReadUInt16(tiff, ifdOffset, littleEndian);
        EnsureRange(tiff, ifdOffset + 2, count * 12 + 4);
        return Enumerable.Range(0, count).Select(index => ifdOffset + 2 + index * 12).ToArray();
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        EnsureRange(data, offset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        EnsureRange(data, offset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("Invalid EXIF metadata offset.");
    }
}
