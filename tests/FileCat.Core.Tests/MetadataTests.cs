using System.Buffers.Binary;
using System.Diagnostics;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public class MetadataTests
{
    [Fact]
    public void Refreshed_folder_retires_a_same_size_restored_time_row_claim_only_there()
    {
        using var dir = new TempDir();
        string firstFolder = dir.Dir("first"), secondFolder = dir.Dir("second");
        string first = Path.Combine(firstFolder, "claim.txt"), second = Path.Combine(secondFolder, "claim.txt");
        File.WriteAllText(first, "good");
        File.WriteAllText(second, "keep");
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        metadata.Register(new MetadataField("claim", "Claim", MetadataCost.Cheap, _ => true,
            (path, _) => File.ReadAllText(path), value => (string)value!));
        var firstEntry = new EntryData("claim.txt", EntryKind.File, 4, File.GetLastWriteTimeUtc(first).Ticks);
        var secondEntry = new EntryData("claim.txt", EntryKind.File, 4, File.GetLastWriteTimeUtc(second).Ticks);
        Assert.Equal("good", metadata.Compute("claim", first, firstEntry, TestContext.Current.CancellationToken).Value);
        Assert.Equal("keep", metadata.Compute("claim", second, secondEntry, TestContext.Current.CancellationToken).Value);
        DateTime stamp = File.GetLastWriteTimeUtc(first);
        File.WriteAllText(first, "evil");
        File.SetLastWriteTimeUtc(first, stamp);
        Assert.Equal("good", metadata.Compute("claim", first, firstEntry, TestContext.Current.CancellationToken).Value);
        metadata.ForgetFolder("claim", firstFolder);
        Assert.Equal("evil", metadata.Compute("claim", first, firstEntry, TestContext.Current.CancellationToken).Value);
        Assert.Equal("keep", metadata.Peek("claim", second, secondEntry).Value);
    }

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

    [Fact]
    public void Link_target_is_available_for_directory_links_and_absent_for_plain_directories()
    {
        using var dir = new TempDir();
        var target = dir.Dir("target");
        var link = Path.Combine(dir.Path, "link");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Assert.Skip("This Windows account cannot create symbolic links.");
            return;
        }

        using var io = new DeviceIoScheduler();
        var service = new MetadataService(io);
        var linked = service.Compute("linkTarget", link, new EntryData("link", EntryKind.Directory), TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, linked.State);
        Assert.Equal(target, linked.Value);
        Assert.Equal(MetadataState.Absent,
            service.Compute("linkTarget", target, new EntryData("target", EntryKind.Directory), TestContext.Current.CancellationToken).State);
    }

    [Fact]
    public void Windows_junction_target_is_available_as_directory_metadata()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var target = dir.Dir("target");
        var link = Path.Combine(dir.Path, "junction");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
               { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            Assert.True(mklink.WaitForExit(10_000), "Junction creation timed out.");
            Assert.Equal(0, mklink.ExitCode);
        }

        using var io = new DeviceIoScheduler();
        var service = new MetadataService(io);
        var value = service.Compute("linkTarget", link, new EntryData("junction", EntryKind.Directory), TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, value.State);
        Assert.Equal(target, value.Value);
    }

    [Fact]
    public void Windows_origin_column_binds_allocation_for_oversized_zone_stream()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows NTFS alternate streams only.");
            return;
        }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "owned.bin");
        File.WriteAllText(path, "owned");
        string ads = path + ":Zone.Identifier";
        File.WriteAllText(ads, "[ZoneTransfer]\r\n" + new string('x', 8 * 1024 * 1024) + "\r\nZoneId=3\r\n");

        using var io = new DeviceIoScheduler();
        var service = new MetadataService(io);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var value = service.Compute("zone", path, new EntryData("owned.bin", EntryKind.File), TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 2 * 1024 * 1024, $"Automatic Origin metadata allocated {allocated:N0} bytes for one oversized stream.");
        Assert.Equal(MetadataState.Available, value.State);
        Assert.Equal("Marked", value.Value);
    }

    [Fact]
    public void Windows_origin_column_reads_a_normal_zone_stream()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows NTFS alternate streams only.");
            return;
        }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "owned.bin");
        File.WriteAllText(path, "owned");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        using var io = new DeviceIoScheduler();
        var value = new MetadataService(io).Compute("zone", path,
            new EntryData("owned.bin", EntryKind.File), TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, value.State);
        Assert.Equal("Internet", value.Value);
    }

    [Fact]
    public void Windows_origin_column_keeps_an_unmarked_file_absent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows NTFS alternate streams only.");
            return;
        }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "owned.bin");
        File.WriteAllText(path, "owned");

        using var io = new DeviceIoScheduler();
        var value = new MetadataService(io).Compute("zone", path,
            new EntryData("owned.bin", EntryKind.File), TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Absent, value.State);
    }
}
