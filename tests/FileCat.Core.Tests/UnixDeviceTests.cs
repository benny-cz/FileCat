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
    public void Where_writing_goes_is_told_by_real_paths_backing_files_and_servers_and_unknown_never_counts_as_elsewhere()
    {
        // Release plan V09 (I09): a recovery's destination, and FileCat's own folders, must not be taken for another disk
        // when they are not on one. sysfs as Linux lays it out: class/block/NAME links to the device's folder, a partition's
        // folder inside its disk's.
        string sys = Path.Combine(_dir.Path, "sys");
        void Write(string relative, string content)
        {
            string path = Path.Combine(sys, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content + "\n");
        }
        void Device(string name, string folder)
        {
            string target = Path.Combine(sys, folder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.Combine(target, "slaves"));
            string link = Path.Combine(sys, "class", "block", name);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            try { Directory.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Symbolic links cannot be made here (Windows without Developer Mode or administrator rights).");
            }
        }
        Device("sdb", "devices/pci/block/sdb");
        Device("sdb1", "devices/pci/block/sdb/sdb1");
        Write("devices/pci/block/sdb/sdb1/partition", "1");
        Device("sdb2", "devices/pci/block/sdb/sdb2");
        Write("devices/pci/block/sdb/sdb2/partition", "2");
        Device("sdc", "devices/pci/block/sdc");
        Device("sdc1", "devices/pci/block/sdc/sdc1");
        Write("devices/pci/block/sdc/sdc1/partition", "1");
        Device("sdc2", "devices/pci/block/sdc/sdc2");
        Write("devices/pci/block/sdc/sdc2/partition", "2");
        Device("dm-0", "devices/virtual/block/dm-0");
        Directory.CreateDirectory(Path.Combine(sys, "devices", "virtual", "block", "dm-0", "slaves", "sdc1"));
        Device("dm-1", "devices/virtual/block/dm-1");
        Directory.CreateDirectory(Path.Combine(sys, "devices", "virtual", "block", "dm-1", "slaves", "sdq1")); // not in sysfs
        Device("loop0", "devices/virtual/block/loop0");
        Write("devices/virtual/block/loop0/loop/backing_file", "/home/u/stick.img");
        Device("loop1", "devices/virtual/block/loop1");
        Write("devices/virtual/block/loop1/loop/backing_file", "/home/u/gone.img (deleted)");
        Device("nbd0", "devices/virtual/block/nbd0");
        Directory.CreateDirectory(Path.Combine(sys, "fs", "btrfs", "5e1f", "devices", "sdb2"));
        Directory.CreateDirectory(Path.Combine(sys, "fs", "btrfs", "5e1f", "devices", "sdc2"));
        var mounts = UnixDisks.ParseMountInfo(
        [
            "22 1 8:17 / / rw - ext4 /dev/sdb1 rw",
            "30 22 253:0 / /data rw - ext4 /dev/dm-0 rw",
            "31 22 7:0 / /mnt/img rw - vfat /dev/loop0 rw",
            "32 22 7:1 / /mnt/old rw - vfat /dev/loop1 rw",
            "33 22 43:0 / /mnt/nbd rw - ext4 /dev/nbd0 rw",
            "34 22 0:50 / /mnt/nas rw - cifs //user@nas.example/share rw",
            "35 22 0:51 / /mnt/self rw - cifs //localhost/share rw",
            "36 22 0:52 / /mnt/nfs rw - nfs4 [fe80::1]:/export rw",
            "37 22 0:53 / /mnt/overlay rw - overlay overlay rw",
            "38 22 0:54 / /tmp rw - tmpfs tmpfs rw",
            "39 22 0:55 / /run/user/1000/gvfs rw - fuse.gvfsd-fuse gvfsd-fuse rw",
            "40 22 0:56 / /mnt/host rw - virtiofs share rw",
            "41 22 0:57 / /pool rw - btrfs /dev/sdb2 rw",
            "42 22 8:33 / /mnt/broken rw - ext4 /dev/dm-1 rw",
            "43 22 0:58 / /data/over rw - tmpfs tmpfs rw",
            "44 22 0:59 / /data/over rw - ext4 /dev/sdb1 rw", // mounted over the tmpfs: it is what is written to
        ]);
        // A link in the home folder leads to /data.
        string? Resolve(string path) => path.StartsWith("/home/u/to-data/", StringComparison.Ordinal) ? "/data/" + path["/home/u/to-data/".Length..] : path;
        var topology = new UnixDisks.LinuxTopology(sys, mounts, Resolve, server => server is "localhost" or "127.0.0.1");

        Assert.Null(topology.BlockDisks("sdz", written: true)); // no sysfs entry: unknown, never "no disk"
        Assert.Equal(["sdb"], topology.DeviceDisks("/dev/sdb1"));
        Assert.Equal(["sdb"], topology.FolderDisks("/home/u/out"));
        Assert.Equal(["sdc"], topology.FolderDisks("/data/out")); // a mapped device's disks
        Assert.Equal(["sdc"], topology.FolderDisks("/home/u/to-data/out")); // where the link leads, not where it is
        Assert.Null(topology.FolderDisks("/mnt/broken/out")); // a mapped device over something sysfs does not list
        // Written, a loop device is itself and its backing file (here on sdb); read, a disk of its own.
        Assert.Equal(["loop0", "sdb"], topology.FolderDisks("/mnt/img/out"));
        Assert.Equal(["loop0"], topology.DeviceDisks("/dev/loop0"));
        Assert.Null(topology.FolderDisks("/mnt/old/out")); // its file was deleted
        Assert.Null(topology.FolderDisks("/mnt/nbd/out")); // served by another program, from anywhere
        Assert.Null(topology.DeviceDisks("/dev/nbd0")); // its server can export a local device as well
        // Another computer's share is on no disk here; one this computer serves is on one of its own.
        Assert.Empty(topology.FolderDisks("/mnt/nas/out")!);
        Assert.Null(topology.FolderDisks("/mnt/self/out"));
        Assert.Empty(topology.FolderDisks("/mnt/nfs/out")!);
        Assert.Null(topology.FolderDisks("/mnt/overlay/out")); // no block device to follow
        Assert.Empty(topology.FolderDisks("/tmp/out")!);
        Assert.Empty(topology.FolderDisks("/mnt/host/out")!); // a virtual machine's host share
        Assert.Empty(topology.FolderDisks("/run/user/1000/gvfs/smb-share:server=nas,share=s/out")!);
        Assert.Null(topology.FolderDisks("/run/user/1000/gvfs/smb-share:server=localhost,share=s/out"));
        Assert.Null(topology.FolderDisks("/run/user/1000/gvfs/archive:host=file%253A%252F%252F%252Fhome%252Fu%252Fa.zip/out"));
        Assert.Empty(topology.FolderDisks("/run/user/1000/gvfs/mtp:host=Phone/out")!);
        Assert.Equal(["sdb", "sdc"], topology.FolderDisks("/pool/out")!.Order()); // every device the btrfs spans
        Assert.Equal(["sdb"], topology.FolderDisks("/data/over/out")); // the mount on top
        Assert.Null(new UnixDisks.LinuxTopology(sys, mounts, _ => null, _ => false).FolderDisks("/home/u/out")); // a path that cannot be resolved

        // A folder under /dev is a folder: memory here, and a link from it leads to a disk.
        var withShm = new UnixDisks.LinuxTopology(sys, [.. mounts, .. UnixDisks.ParseMountInfo(["45 22 0:60 / /dev/shm rw - tmpfs tmpfs rw"])],
            path => path == "/dev/shm/link/out" ? "/data/out" : path, _ => false);
        Assert.Empty(withShm.FolderDisks("/dev/shm/out")!);
        Assert.Equal(["sdc"], withShm.FolderDisks("/dev/shm/link/out"));
        Assert.True(UnixDisks.IsDevicePath("/dev/sdb1"));
        Assert.True(UnixDisks.IsDevicePath("/dev/mapper/luks-1"));
        Assert.False(UnixDisks.IsDevicePath("/dev/shm/out"));

        Assert.Equal("nas", UnixDisks.ServerOf("//user@nas/share"));
        Assert.Equal("nas", UnixDisks.ServerOf("nas:/export"));
        Assert.Equal("fe80::1", UnixDisks.ServerOf("[fe80::1]:/export"));
        Assert.Equal("host", UnixDisks.ServerOf("user@host:/home/user"));
        Assert.Equal("dav.example", UnixDisks.ServerOf("https://dav.example/files"));
    }

    [Fact]
    public void A_folder_reached_from_memory_through_a_link_is_on_the_disk_the_link_leads_to()
    {
        // Release plan V09 (I09): /dev/shm is memory, but a link there to a folder on a disk writes to that disk.
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/dev/shm")) Assert.Skip("Needs Linux with /dev/shm.");
        string home = _dir.Dir("target");
        var disks = UnixDisks.DisksOf(home);
        if (disks is null or { Count: 0 }) Assert.Skip("The test folder is on no disk this system can name (a container?).");
        string link = Path.Combine("/dev/shm", "filecat-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(link, home);
        try
        {
            Assert.Empty(UnixDisks.DisksOf("/dev/shm")!);
            Assert.Equal(disks, UnixDisks.DisksOf(Path.Combine(link, "out")));
            Assert.True(UnixDisks.SharesDisk("/dev/" + disks[0], Path.Combine(link, "out")));
        }
        finally
        {
            File.Delete(link);
        }
    }

    [Fact]
    public void Where_writing_goes_on_this_system_is_what_the_tester_expects()
    {
        // Release plan V09 (I09), checked by hand on real systems: FILECAT_TOPOLOGY_DEVICE (read), FILECAT_TOPOLOGY_FOLDER
        // (written to) and FILECAT_TOPOLOGY_EXPECT (true, false or unknown) name a case such as a loop device or a disk
        // image whose file lies on the device's disk.
        string? device = Environment.GetEnvironmentVariable("FILECAT_TOPOLOGY_DEVICE");
        string? folder = Environment.GetEnvironmentVariable("FILECAT_TOPOLOGY_FOLDER");
        string? expect = Environment.GetEnvironmentVariable("FILECAT_TOPOLOGY_EXPECT");
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(device) || string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(expect))
            Assert.Skip("Set FILECAT_TOPOLOGY_DEVICE, FILECAT_TOPOLOGY_FOLDER and FILECAT_TOPOLOGY_EXPECT (true, false, unknown) on Linux or macOS.");
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"{device} is on: {string.Join(", ", UnixDisks.DisksOf(device) ?? ["unknown"])}");
        output?.WriteLine($"{folder} is on: {string.Join(", ", UnixDisks.DisksOf(folder) ?? ["unknown"])}");
        bool? shares = UnixDisks.SharesDisk(device, folder);
        output?.WriteLine($"shares: {shares?.ToString() ?? "unknown"}");
        // What the scan's confirmation waits for: every folder FileCat writes in, checked against the device.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var own = FileCat.Core.State.AppPaths.Usual().WriteFolders.Select(f => UnixDisks.SharesDisk(device, f.Folder)).ToList();
        output?.WriteLine($"FileCat's {own.Count} folders checked in {clock.ElapsedMilliseconds} ms: {own.Count(s => s == true)} on that disk, {own.Count(s => s is null)} unknown");
        Assert.Equal(expect, shares switch { true => "true", false => "false", null => "unknown" });
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

    [Theory]
    [InlineData("replaced")]
    [InlineData("removed")]
    [InlineData("wrong-descriptor")]
    [InlineData("path-replaced-after-open")]
    public void A_device_changed_while_access_is_requested_is_refused_and_the_received_descriptor_is_closed(string change)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Native descriptor identities require Linux or macOS.");
        string selected = Path.Combine(_dir.Path, "selected.img"), other = Path.Combine(_dir.Path, "other.img");
        File.WriteAllBytes(selected, Enumerable.Repeat((byte)0x31, 512).ToArray());
        File.WriteAllBytes(other, Enumerable.Repeat((byte)0x72, 512).ToArray()); // Equal sizes cannot distinguish them.
        Microsoft.Win32.SafeHandles.SafeFileHandle? received = null;
        int requests = 0;
        var error = Assert.Throws<IOException>(() => UnixDeviceSource.Open(selected, "selected image", TestContext.Current.CancellationToken,
            (path, _) =>
            {
                requests++;
                if (change == "replaced") File.Move(other, path, overwrite: true);
                received = File.OpenHandle(change == "wrong-descriptor" ? other : path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (change == "removed") File.Delete(path);
                if (change == "path-replaced-after-open") File.Move(other, path, overwrite: true);
                return received;
            }));
        Assert.Contains("changed or was removed", error.Message);
        Assert.Equal(1, requests);
        Assert.NotNull(received);
        Assert.True(received.IsClosed); // Refusal owns disposal; no source object or content read is returned.
    }

    [Fact]
    public void An_unchanged_received_device_and_a_link_to_it_open_with_the_original_bytes()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Native descriptor identities require Linux or macOS.");
        string image = Path.Combine(_dir.Path, "unchanged.img"), link = Path.Combine(_dir.Path, "selected-link");
        var bytes = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(image, bytes);
        File.CreateSymbolicLink(link, image);
        using var source = UnixDeviceSource.Open(link, "unchanged image", TestContext.Current.CancellationToken);
        Assert.Equal(bytes.Length, source.Length);
        var actual = new byte[bytes.Length];
        Assert.Equal(actual.Length, source.Read(0, actual));
        Assert.Equal(bytes, actual);
    }

    [Fact]
    public void An_unavailable_device_identity_is_refused_before_requesting_access()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Native descriptor identities require Linux or macOS.");
        bool requested = false;
        Assert.Throws<IOException>(() => UnixDeviceSource.Open(Path.Combine(_dir.Path, "missing.img"), "missing image", TestContext.Current.CancellationToken,
            (_, _) => { requested = true; throw new InvalidOperationException("Access must not be requested."); }));
        Assert.False(requested);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("replaced")]
    public void An_authorization_failure_after_the_device_changes_reports_the_device_change(string change)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Native device identities require Linux or macOS.");
        string selected = Path.Combine(_dir.Path, "selected.img"), replacement = Path.Combine(_dir.Path, "replacement.img");
        File.WriteAllBytes(selected, new byte[512]);
        File.WriteAllBytes(replacement, Enumerable.Repeat((byte)0x72, 512).ToArray());
        var authorizationFailure = new OperationCanceledException("Reading the device was not approved.");
        int requests = 0;
        var error = Assert.Throws<IOException>(() => UnixDeviceSource.Open(selected, "selected image", CancellationToken.None,
            (path, _) =>
            {
                requests++;
                if (change == "removed") File.Delete(path);
                else File.Move(replacement, path, overwrite: true);
                throw authorizationFailure;
            }));
        Assert.Contains("changed or was removed", error.Message);
        Assert.Same(authorizationFailure, error.InnerException);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unchanged_device_refusal_or_explicit_cancellation_preserves_the_original_failure(bool cancel)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Native device identities require Linux or macOS.");
        string selected = Path.Combine(_dir.Path, "selected.img");
        File.WriteAllBytes(selected, new byte[512]);
        using var cancellation = new CancellationTokenSource();
        var original = new OperationCanceledException("Authorization did not return a descriptor.", cancellation.Token);
        int requests = 0;
        var error = Assert.Throws<OperationCanceledException>(() => UnixDeviceSource.Open(selected, "selected image", cancellation.Token,
            (path, _) =>
            {
                requests++;
                if (cancel)
                {
                    File.Delete(path);
                    cancellation.Cancel();
                }
                throw original;
            }));
        Assert.Same(original, error);
        Assert.Equal(1, requests);
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
