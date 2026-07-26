using TateScribe.Core.Pages;
using TateScribe.Infrastructure.Import;

namespace TateScribe.Tests;

public sealed class PageOrderingTests
{
    [Fact]
    public void Sort_uses_embedded_filename_timestamp_before_other_metadata()
    {
        var laterModified = new PageSortCandidate("IMG_20260725_083202.png", null, null, new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero), null);
        var earlierEmbedded = new PageSortCandidate("IMG_20260725_083157.png", null, null, new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero), null);

        var ordered = PageOrdering.Sort([laterModified, earlierEmbedded]);

        Assert.Equal("IMG_20260725_083157.png", ordered[0].FileName);
    }

    [Fact]
    public void Sort_falls_back_to_natural_filename_order()
    {
        var ordered = PageOrdering.Sort([
            new PageSortCandidate("page-10.png", null, null, null, null),
            new PageSortCandidate("page-2.png", null, null, null, null)
        ]);

        Assert.Equal(["page-2.png", "page-10.png"], ordered.Select(x => x.FileName));
    }

    [Fact]
    public async Task Import_orders_jpeg_images_by_exif_original_timestamp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var later = Path.Combine(directory, "a.jpg");
            var earlier = Path.Combine(directory, "z.jpg");
            await File.WriteAllBytesAsync(later, CreateExifJpeg("2026:07:25 10:00:00", "2026:07:25 09:59:00"));
            await File.WriteAllBytesAsync(earlier, CreateExifJpeg("2026:07:25 08:00:00", "2026:07:25 07:59:00"));

            var pages = await new ImageImporter().ImportAsync([later, earlier], CancellationToken.None);

            Assert.Equal(["z.jpg", "a.jpg"], pages.Select(page => page.FileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Import_prefers_filename_timestamp_and_survives_broken_exif()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var filenameFirst = Path.Combine(directory, "IMG_20260725_070000.jpg");
            var exifFirst = Path.Combine(directory, "IMG_20260725_080000.jpg");
            await File.WriteAllBytesAsync(filenameFirst, CreateExifJpeg("2026:07:25 12:00:00", null));
            await File.WriteAllBytesAsync(exifFirst, [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xD9]);

            var pages = await new ImageImporter().ImportAsync([exifFirst, filenameFirst], CancellationToken.None);

            Assert.Equal(["IMG_20260725_070000.jpg", "IMG_20260725_080000.jpg"], pages.Select(page => page.FileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Image_timestamp_reader_falls_back_to_exif_digitized_timestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TateScribe-{Guid.NewGuid():N}.jpg");
        try
        {
            await File.WriteAllBytesAsync(path, CreateExifJpeg("invalid", "2026:07:25 06:30:00"));

            var timestamp = ImageTimestampReader.TryRead(path);

            Assert.NotNull(timestamp);
            Assert.Equal((2026, 7, 25, 6, 30), (timestamp.Value.Year, timestamp.Value.Month, timestamp.Value.Day, timestamp.Value.Hour, timestamp.Value.Minute));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static byte[] CreateExifJpeg(string original, string? digitized)
    {
        var values = digitized is null ? new[] { (Tag: (ushort)0x9003, Value: original) }
            : new[] { (Tag: (ushort)0x9003, Value: original), (Tag: (ushort)0x9004, Value: digitized) };
        var exifIfdOffset = 26;
        var valuesOffset = exifIfdOffset + 2 + values.Length * 12 + 4;
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange([0x2A, 0x00, 0x08, 0x00, 0x00, 0x00]);
        tiff.AddRange([0x01, 0x00]);
        tiff.AddRange([0x69, 0x87, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00]);
        tiff.AddRange(BitConverter.GetBytes(exifIfdOffset));
        tiff.AddRange([0x00, 0x00, 0x00, 0x00]);
        tiff.AddRange(BitConverter.GetBytes((ushort)values.Length));
        var strings = new List<byte>();
        foreach (var value in values)
        {
            var encoded = System.Text.Encoding.ASCII.GetBytes(value.Value + "\0");
            tiff.AddRange(BitConverter.GetBytes(value.Tag));
            tiff.AddRange([0x02, 0x00]);
            tiff.AddRange(BitConverter.GetBytes(encoded.Length));
            tiff.AddRange(BitConverter.GetBytes(valuesOffset + strings.Count));
            strings.AddRange(encoded);
        }
        tiff.AddRange([0x00, 0x00, 0x00, 0x00]);
        tiff.AddRange(strings);
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, 0xFF, 0xD9];
    }
}
