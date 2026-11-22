using System.Buffers.Binary;
using System.Diagnostics;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public class MetadataTests
{
    [Fact]
    public void An_explicit_sort_selector_can_leave_a_displayed_value_unknown()
    {
        Assert.Equal(new Version(2, 1), BuiltInFields.Version.CaptureKey(new(MetadataState.Available, "2.1")));
        Assert.Null(BuiltInFields.Version.CaptureKey(new(MetadataState.Available, "unparseable version")));
        Assert.Equal("target", BuiltInFields.LinkTarget.CaptureKey(new(MetadataState.Available, "target")));
    }

    [Fact]
    public void The_version_column_reads_the_fixed_version_numbers_and_tidies_version_strings()
    {
        // The fixed numbers, as Explorer's "File version" shows them, over the old Visual C++ template's string.
        Assert.Equal("1.0.0.1", BuiltInFields.FileVersionText(1, 0, 0, 1, "1, 0, 0, 1"));
        Assert.Equal("10.0.26100.1", BuiltInFields.FileVersionText(10, 0, 26100, 1, "10.0.26100.1 (WinBuild.160101.0800)"));
        Assert.Equal("6.2.9200.16384", BuiltInFields.FileVersionText(6, 2, 9200, 16384, "1, 0, 0, 1"));
        Assert.Equal("2.0.0.0", BuiltInFields.FileVersionText(2, 0, 0, 0, null));
        // Without them, the string: comma separators become dots and a trailing build tag goes.
        Assert.Equal("6.2.9200.16384", BuiltInFields.FileVersionText(0, 0, 0, 0, "6.2.9200.16384 (win8_rtm.120725-1247)"));
        Assert.Equal("1.0.0.1", BuiltInFields.FileVersionText(0, 0, 0, 0, "1, 0, 0, 1"));
        Assert.Equal("3.5.1", BuiltInFields.FileVersionText(0, 0, 0, 0, "3,5 ,1"));
        Assert.Null(BuiltInFields.FileVersionText(0, 0, 0, 0, null));
        Assert.Null(BuiltInFields.FileVersionText(0, 0, 0, 0, ""));
        Assert.Null(BuiltInFields.FileVersionText(0, 0, 0, 0, " "));
        // So such a file sorts by its version, not as unknown.
        Assert.Equal(new Version(1, 0, 0, 1), BuiltInFields.Version.CaptureKey(new(MetadataState.Available, BuiltInFields.FileVersionText(0, 0, 0, 0, "1, 0, 0, 1"))));
        // An installer package has no version resource: its version lives in the package's own database.
        Assert.False(BuiltInFields.Version.AppliesToName("Setup.MSI"));
        Assert.True(BuiltInFields.Version.AppliesToName("tool.exe"));
    }

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

    /// <summary>SOI, then each segment as its marker, big-endian length (counting itself) and payload.</summary>
    private static byte[] Jpeg(params (byte Marker, byte[] Payload)[] segments)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        foreach (var (marker, payload) in segments)
            bytes.AddRange([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload]);
        return [.. bytes];
    }

    /// <summary>A baseline frame header's payload: 8-bit samples, the height and width, three components.</summary>
    private static byte[] Frame(int width, int height) =>
        [8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1];

    /// <summary>The size read from the bytes as a file; the same walk over them in memory must agree.</summary>
    private static (int Width, int Height)? ReadJpeg(TempDir dir, byte[] bytes)
    {
        string path = Path.Combine(dir.Path, "photo.jpg");
        File.WriteAllBytes(path, bytes);
        var size = ImageHeader.TryRead(path, TestContext.Current.CancellationToken);
        Assert.Equal(size, ImageHeader.Parse(bytes, TestContext.Current.CancellationToken));
        return size;
    }

    [Fact]
    public void A_JPEG_frame_header_past_large_metadata_segments_is_read_from_the_file()
    {
        // A camera's EXIF block fills a whole APP1 segment (its thumbnail's own frame header inside) and an ICC profile
        // follows: the photo's frame header lies past the first 64 KiB, which a fixed-size head never reached.
        var exif = new byte[65533];
        "Exif\0\0"u8.CopyTo(exif);
        byte[] thumbnail = [0xFF, 0xD8, 0xFF, 0xC0, 0, 17, .. Frame(160, 120), 0xFF, 0xD9];
        thumbnail.CopyTo(exif, 4096);
        var icc = new byte[6000];
        "ICC_PROFILE\0"u8.CopyTo(icc);
        byte[] photo = [.. Jpeg((0xE1, exif), (0xE2, icc), (0xDB, new byte[65]), (0xC0, Frame(4032, 3024)), (0xC4, new byte[30]), (0xDA, new byte[10])),
            0x12, 0x34, 0xFF, 0xD9];
        Assert.True(photo.AsSpan().IndexOf(new byte[] { 0xFF, 0xC0, 0, 17, 8, 0x0B, 0xD0 }) > 64 * 1024);
        Assert.Null(ImageHeader.Parse(photo.AsSpan(0, 64 * 1024), TestContext.Current.CancellationToken));

        using var dir = new TempDir();
        Assert.Equal((4032, 3024), ReadJpeg(dir, photo));
        using var io = new DeviceIoScheduler();
        var value = new MetadataService(io).Compute("dimensions", Path.Combine(dir.Path, "photo.jpg"),
            new EntryData("photo.jpg", EntryKind.File), TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, value.State);
        Assert.Equal((4032, 3024), value.Value);
    }

    [Fact]
    public void A_JPEG_walk_steps_over_fill_bytes_standalone_markers_and_stray_bytes()
    {
        byte[] app0 = [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0];
        byte[] frame = Jpeg((0xC0, Frame(800, 600)))[2..];
        using var dir = new TempDir();
        // Fill bytes may come before any marker; TEM and RSTn stand alone, with no length.
        Assert.Equal((800, 600), ReadJpeg(dir, [0xFF, 0xD8, 0xFF, 0xFF, .. Jpeg((0xE0, app0))[2..], 0xFF, 0x01, 0xFF, 0xD3, 0xFF, 0xFF, 0xFF, .. frame]));
        // A run of fill bytes longer than one step looks at.
        Assert.Equal((800, 600), ReadJpeg(dir, [0xFF, 0xD8, .. Enumerable.Repeat((byte)0xFF, 300), .. frame]));
        // Padding some encoders leave after a segment, and FF 00, which is no marker.
        Assert.Equal((800, 600), ReadJpeg(dir, [.. Jpeg((0xE0, app0)), 0, 0, 0x12, 0xFF, 0x00, .. frame]));
        // A frame header without its height (a DNL marker gives it after the first scan) has no size to show.
        Assert.Null(ReadJpeg(dir, Jpeg((0xC0, Frame(800, 0)))));
    }

    [Fact]
    public void A_JPEG_walk_gives_no_size_for_truncated_or_malformed_segments()
    {
        using var dir = new TempDir();
        var exif = new byte[65533];
        byte[] photo = Jpeg((0xE1, exif), (0xC0, Frame(640, 480)));
        int frame = 2 + 4 + exif.Length;
        Assert.Equal((640, 480), ReadJpeg(dir, photo));
        Assert.Null(ReadJpeg(dir, photo[..40_000])); // cut short in the EXIF segment
        Assert.Null(ReadJpeg(dir, photo[..(frame + 8)])); // cut short in the frame header, inside its width
        // A segment length below 2 cannot even count its own two bytes.
        foreach (byte low in new byte[] { 0, 1 })
        {
            byte[] broken = (byte[])photo.Clone();
            (broken[4], broken[5]) = (0, low);
            Assert.Null(ReadJpeg(dir, broken));
        }
        // A frame header too short to hold the size, though more of the file follows.
        Assert.Null(ReadJpeg(dir, Jpeg((0xC0, [8, 0x01, 0xE0, 0x02]), (0xDB, new byte[65]))));
        // The image data, or the end of the image, before any frame header.
        Assert.Null(ReadJpeg(dir, Jpeg((0xDA, new byte[10]), (0xC0, Frame(640, 480)))));
        Assert.Null(ReadJpeg(dir, [0xFF, 0xD8, 0xFF, 0xD9, .. Jpeg((0xC0, Frame(640, 480)))[2..]]));
    }

    [Fact]
    public void A_JPEG_walk_is_bounded_in_steps_and_in_how_far_into_the_file_it_reads()
    {
        using var dir = new TempDir();
        (byte, byte[]) comment = (0xFE, []);
        Assert.Equal((640, 480), ReadJpeg(dir, Jpeg([.. Enumerable.Repeat(comment, 500), (0xC0, Frame(640, 480))])));
        Assert.Null(ReadJpeg(dir, Jpeg([.. Enumerable.Repeat(comment, 600), (0xC0, Frame(640, 480))])));

        // Whole APP1 segments: after 255 of them the frame header starts below 16 MiB; after 256, past it.
        string path = Path.Combine(dir.Path, "large.jpg");
        var segment = new byte[65537];
        (segment[0], segment[1], segment[2], segment[3]) = (0xFF, 0xE1, 0xFF, 0xFF);
        byte[] frame = Jpeg((0xC0, Frame(640, 480)))[2..];
        using (var file = File.Create(path))
        {
            file.Write([0xFF, 0xD8]);
            for (int i = 0; i < 255; i++) file.Write(segment);
            file.Write(frame);
        }
        Assert.Equal((640, 480), ImageHeader.TryRead(path, TestContext.Current.CancellationToken));
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            file.Position = 2 + 255L * segment.Length;
            file.Write(segment);
            file.Write(frame);
        }
        Assert.Null(ImageHeader.TryRead(path, TestContext.Current.CancellationToken));

        // A cancelled read stops before its next step.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ImageHeader.TryRead(path, cancelled.Token));
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
