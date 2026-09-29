using FileCat.Recovery;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>
/// Reading drives on Linux and macOS (D-47): the D-Bus call UDisks2 answers, descriptors passed over local sockets, the
/// lists of disks and mounts, and reads of raw devices in whole sectors. What needs a real system runs where there is one.
/// </summary>
public sealed class UnixDeviceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static string AsciiHex(string text) => Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(text));

    [Fact]
    public void A_method_call_is_marshalled_as_the_bus_reads_it()
    {
        // The Hello every client sends first, byte for byte from the specification: header, four header fields each
        // aligned to 8, no body.
        var expected = Hex("6c010001 00000000 01000000 6e000000" +
                           "01016f00 15000000" + AsciiHex("/org/freedesktop/DBus") + "00 0000" +
                           "06017300 14000000" + AsciiHex("org.freedesktop.DBus") + "00 000000" +
                           "02017300 14000000" + AsciiHex("org.freedesktop.DBus") + "00 000000" +
                           "03017300 05000000" + AsciiHex("Hello") + "00 0000");
        Assert.Equal(expected, DBusConnection.MethodCallMessage(1, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", []));

        // OpenDevice's arguments (sa{sv}): a string, then a dictionary whose entries start at multiples of 8.
        var body = new DBusWriter();
        body.Value(new DBusValue.Str("r"));
        body.Value(new DBusValue.Dict([("auth.no_user_interaction", new DBusValue.Bool(true))]));
        Assert.Equal(Hex("01000000 72 00 0000 24000000 00000000 18000000" + AsciiHex("auth.no_user_interaction") + "00 016200 01000000"), body.Bytes);
        var empty = new DBusWriter();
        empty.Value(new DBusValue.Str("r"));
        empty.Value(new DBusValue.Dict([]));
        Assert.Equal(Hex("01000000 72 00 0000 00000000 00000000"), empty.Bytes); // an empty array still pads to its elements

        var reader = new DBusReader(body.Bytes, 0);
        Assert.Equal("r", reader.String());
        Assert.Equal(36u, reader.UInt32());
        reader.Pad(8);
        Assert.Equal("auth.no_user_interaction", reader.String());
        Assert.Equal("b", reader.Signature());
        Assert.Equal(1u, reader.UInt32());
    }

    [Fact]
    public void UDisks2_object_paths_escape_what_is_not_a_letter_or_digit()
    {
        Assert.Equal("sdb1", UDisks.Escape("sdb1"));
        Assert.Equal("nvme0n1p2", UDisks.Escape("nvme0n1p2"));
        Assert.Equal("dm_2d0", UDisks.Escape("dm-0"));
    }

    [Fact]
    public void Linux_disks_come_from_sysfs_with_their_mounts()
    {
        string sys = Path.Combine(_dir.Path, "sys");
        void Write(string relative, string content)
        {
            string path = Path.Combine(sys, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content + "\n");
        }
        Write("block/sdb/size", "15630336");
        Write("block/sdb/removable", "1");
        Write("block/sdb/dev", "8:16");
        Write("block/sdb/device/vendor", "SanDisk ");
        Write("block/sdb/device/model", "Cruzer   Blade");
        Write("block/sdb/sdb1/partition", "1");
        Write("block/sdb/sdb1/size", "15628288");
        Write("block/sdb/sdb1/dev", "8:17");
        Write("block/nvme0n1/size", "1953525168");
        Write("block/nvme0n1/dev", "259:0");
        Write("block/nvme0n1/nvme0n1p1/partition", "1");
        Write("block/nvme0n1/nvme0n1p1/size", "1048576");
        Write("block/nvme0n1/nvme0n1p1/dev", "259:1");
        Write("block/loop0/size", "0"); // unused
        Write("block/zram0/size", "8388608"); // memory
        string mountInfo = Path.Combine(_dir.Path, "mountinfo");
        File.WriteAllLines(mountInfo,
        [
            @"22 1 259:2 / / rw,relatime shared:1 - ext4 /dev/nvme0n1p2 rw",
            @"36 22 8:17 / /media/user/USB\040STICK rw,nosuid,nodev shared:30 - vfat /dev/sdb1 rw,fmask=0022",
            @"40 22 0:45 / /media/user/Data rw,nosuid shared:31 - fuseblk /dev/nvme0n1p1 rw,user_id=0",
        ]);
        var devices = UnixDisks.Linux(sys, mountInfo);
        Assert.Equal(["/dev/nvme0n1", "/dev/nvme0n1p1", "/dev/sdb", "/dev/sdb1"], devices.Select(d => d.Device));
        var stick = devices.Single(d => d.Device == "/dev/sdb");
        Assert.Equal((15630336L * 512, "SanDisk Cruzer Blade", true, (string?)null), (stick.Length, stick.Model, stick.Removable, stick.Disk));
        var usb = devices.Single(d => d.Device == "/dev/sdb1");
        Assert.Equal(("/dev/sdb", "vfat"), (usb.Disk, usb.FileSystem));
        Assert.Equal(["/media/user/USB STICK"], usb.MountPoints);
        var ntfs = devices.Single(d => d.Device == "/dev/nvme0n1p1");
        Assert.Equal(("NVMe", "fuseblk"), (ntfs.Bus, ntfs.FileSystem)); // a FUSE mount is found by its source
        Assert.Equal(["/media/user/Data"], ntfs.MountPoints);
    }

    [Fact]
    public void A_diskutil_property_list_is_read()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>AllDisksAndPartitions</key>
              <array>
                <dict>
                  <key>Content</key><string>FDisk_partition_scheme</string>
                  <key>DeviceIdentifier</key><string>disk4</string>
                  <key>Partitions</key>
                  <array>
                    <dict>
                      <key>DeviceIdentifier</key><string>disk4s1</string>
                      <key>MountPoint</key><string>/Volumes/STICK</string>
                      <key>Size</key><integer>8000000000</integer>
                    </dict>
                  </array>
                  <key>Size</key><integer>8004304896</integer>
                </dict>
              </array>
              <key>Internal</key><false/>
            </dict>
            </plist>
            """;
        var root = Assert.IsType<Dictionary<string, object?>>(UnixDisks.Plist(xml));
        Assert.Equal(false, root["Internal"]);
        var disk = Assert.IsType<Dictionary<string, object?>>(Assert.IsType<List<object?>>(root["AllDisksAndPartitions"])[0]);
        Assert.Equal(("disk4", 8004304896L), (disk["DeviceIdentifier"], disk["Size"]));
        var part = Assert.IsType<Dictionary<string, object?>>(Assert.IsType<List<object?>>(disk["Partitions"])[0]);
        Assert.Equal("/Volumes/STICK", part["MountPoint"]);
    }

    [Fact]
    public void A_raw_device_is_read_in_whole_sectors_and_gives_the_same_bytes()
    {
        var bytes = new byte[1 << 20];
        new Random(4).NextBytes(bytes);
        string image = Path.Combine(_dir.Path, "raw.img");
        File.WriteAllBytes(image, bytes);
        using var source = UnixDeviceSource.From(File.OpenHandle(image), "/dev/rdisk9", "raw", wholeSectors: true, sectorSize: 4096);
        Assert.Equal(bytes.Length, source.Length);
        var rng = new Random(5);
        for (int i = 0; i < 200; i++)
        {
            int offset = rng.Next(bytes.Length), length = rng.Next(1, 70_000);
            var buffer = new byte[length];
            int n = source.Read(offset, buffer);
            Assert.Equal(Math.Min(length, bytes.Length - offset), n);
            Assert.True(buffer.AsSpan(0, n).SequenceEqual(bytes.AsSpan(offset, n)), $"offset {offset}, length {length}");
        }
    }

    [Fact]
    public void A_descriptor_passed_over_a_local_socket_arrives_open()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Descriptors pass over local sockets on Linux and macOS.");
        string file = Path.Combine(_dir.Path, "passed.txt");
        File.WriteAllText(file, "handed over");
        var (a, b) = UnixNative.SocketPair();
        try
        {
            using (var handle = File.OpenHandle(file))
                UnixNative.SendFd(a, (int)handle.DangerousGetHandle());
            var fds = new List<int>();
            var buffer = new byte[16];
            Assert.Equal(1, UnixNative.Receive(b, buffer, fds));
            int fd = Assert.Single(fds);
            using var received = new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true);
            var text = new byte[11];
            Assert.Equal(11, RandomAccess.Read(received, text, 0));
            Assert.Equal("handed over", System.Text.Encoding.ASCII.GetString(text));
        }
        finally
        {
            UnixNative.Close(a);
            UnixNative.Close(b);
        }
    }

    [Fact]
    public void The_system_bus_answers_calls_and_errors()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/var/run/dbus/system_bus_socket") && !File.Exists("/run/dbus/system_bus_socket"))
            Assert.Skip("Needs Linux with a system bus.");
        using var bus = DBusConnection.System();
        Assert.StartsWith(":", bus.UniqueName);
        string id = bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetId", []).Body().String();
        Assert.Matches("^[0-9a-f]{32}$", id);
        var error = Assert.Throws<DBusException>(() => bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NoSuchMethod", []));
        Assert.Equal("org.freedesktop.DBus.Error.UnknownMethod", error.Name);
        // UDisks2, where it runs: a drive it does not know is refused with a reason, not a hang or a crash.
        bool udisks = bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", [new DBusValue.Str("org.freedesktop.UDisks2")])
            .Body().UInt32() != 0;
        if (udisks)
            Assert.ThrowsAny<Exception>(() => UDisks.Open("/dev/filecat-no-such-disk", TestContext.Current.CancellationToken, interactive: false));
    }

    [Fact]
    public void This_computers_disks_are_listed_and_the_root_folder_has_one()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Linux and macOS list their disks this way.");
        var devices = UnixDisks.List();
        foreach (var d in devices) TestContext.Current.TestOutputHelper?.WriteLine($"{d.Device} {d.Length:N0} {d.Model} {d.Bus} {d.FileSystem} {string.Join(",", d.MountPoints)} disk={d.Disk}");
        if (devices.Count == 0) Assert.Skip("No block devices are visible here (a container?).");
        Assert.All(devices, d => Assert.True(d.Length > 0));
        TestContext.Current.TestOutputHelper?.WriteLine("/ is on: " + string.Join(", ", UnixDisks.DisksOf("/") ?? ["unknown"]));
    }

    [Fact]
    public void A_block_device_is_scanned_like_its_image()
    {
        // CI attaches the FAT16 fixture as a loop device (Linux) or a disk image (macOS) this user may read, and names it here.
        string? device = Environment.GetEnvironmentVariable("FILECAT_TEST_BLOCK_DEVICE");
        if (string.IsNullOrEmpty(device)) Assert.Skip("Set FILECAT_TEST_BLOCK_DEVICE to a readable device holding the fat16 fixture.");
        using var source = UnixDeviceSource.Open(device, "test device", TestContext.Current.CancellationToken);
        var volume = Assert.Single(RecoveryScanner.Scan(source, TestContext.Current.CancellationToken));
        Assert.Equal("FAT16", volume.FileSystem);
        var report = RecoveryFixtures.Find(volume.Root, "docs/report.txt")!;
        using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), report);
        var data = new byte[content.Length];
        long done = 0;
        while (done < data.Length && content.Read(done, data.AsSpan((int)done)) is var n and > 0) done += n;
        Assert.Equal(RecoveryFixtures.Content("report.txt", 10000), data);
    }
}
