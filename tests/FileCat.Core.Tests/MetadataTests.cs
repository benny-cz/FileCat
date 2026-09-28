using System.Buffers.Binary;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public class MetadataTests
{
    [Fact]
    public void Image_headers_are_parsed_without_decoding()
    {
        var png = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 640);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 480);
        Assert.Equal((640, 480), ImageHeader.Parse(png, TestContext.Current.CancellationToken));

        var gif = "GIF89a"u8.ToArray().Concat(new byte[] { 0x20, 0x01, 0x10, 0x00 }).ToArray();
        Assert.Equal((288, 16), ImageHeader.Parse(gif, TestContext.Current.CancellationToken));

        var bmp = new byte[30];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 100);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -50);
        Assert.Equal((100, 50), ImageHeader.Parse(bmp, TestContext.Current.CancellationToken));

        // JPEG: SOI, APP0 (len 4), SOF0 with height 300 and width 400.
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x2C, 0x01, 0x90, 0x03, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal((400, 300), ImageHeader.Parse(jpg, TestContext.Current.CancellationToken));

        Assert.Null(ImageHeader.Parse("not an image"u8, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Values_go_from_pending_to_available_and_are_cached_by_revision()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "img.gif");
        File.WriteAllBytes(path, "GIF89a"u8.ToArray().Concat(new byte[] { 0x0A, 0x00, 0x14, 0x00 }).ToArray());
        using var io = new DeviceIoScheduler();
        var service = new MetadataService(io);
        var entry = new EntryData("img.gif", EntryKind.File, 10, File.GetLastWriteTimeUtc(path).Ticks);
        var first = service.Get("dimensions", path, entry, "dev", slowLocation: false);
        Assert.Equal(MetadataState.Pending, first.State);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        MetadataValue v;
        do
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            v = service.Get("dimensions", path, entry, "dev", slowLocation: false);
        } while (v.State == MetadataState.Pending && DateTime.UtcNow < deadline);
        Assert.Equal(MetadataState.Available, v.State);
        Assert.Equal((10, 20), v.Value);

        // Expensive fields are never computed automatically on slow locations.
        Assert.Equal(MetadataState.Unsupported, service.Get("dimensions", path, entry with { Modified = 1 }, "dev", slowLocation: true).State);
        // Items the field does not apply to are absent, not failed.
        Assert.Equal(MetadataState.Absent, service.Get("dimensions", path, new EntryData("a.txt", EntryKind.File, 1, 1), "dev", false).State);
    }
}
