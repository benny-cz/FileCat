using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Recovery.Unix;

/// <summary>
/// A disk or partition on Linux or macOS read through a descriptor that can only read (D-47). It is opened directly
/// when this user may (root, Linux's disk group, a disk image the user attached on macOS); otherwise the system's own
/// authorization hands one over: UDisks2 asks polkit on Linux, authopen asks for an administrator on macOS. Nothing runs
/// with more rights than that descriptor, and there is no write member.
/// </summary>
public sealed class UnixDeviceSource : IBlockSource, IDevicePathGuard
{
    private const int MaxChunk = 4 * 1024 * 1024;
    private readonly SafeFileHandle _handle;
    private readonly UnixStat? _identity;
    private readonly int _sector;
    private readonly bool _wholeSectors;

    private UnixDeviceSource(SafeFileHandle handle, string description, long length, int sector, bool wholeSectors)
    {
        _handle = handle;
        _identity = UnixFiles.Stat(handle);
        Description = description;
        Length = length;
        _sector = sector;
        _wholeSectors = wholeSectors;
    }

    public string Description { get; }
    public long Length { get; }

    /// <summary>
    /// Opens <paramref name="device"/> for reading, asking the system when this user may not. Throws
    /// <see cref="OperationCanceledException"/> when the approval is declined or dismissed,
    /// <see cref="UnauthorizedAccessException"/> when it is refused, and <see cref="NotSupportedException"/> when there
    /// is no way to ask (no UDisks2).
    /// </summary>
    public static UnixDeviceSource Open(string device, string description, CancellationToken ct) =>
        Open(device, description, ct, OpenHandle);

    // Authorization can wait while a device disappears and another takes its path, even with the same size.
    // Bind both the received descriptor and the current path to the entry observed before asking for access.
    internal static UnixDeviceSource Open(string device, string description, CancellationToken ct,
        Func<string, CancellationToken, SafeFileHandle> openHandle)
    {
        ct.ThrowIfCancellationRequested();
        if (UnixFiles.Stat(device, followLinks: true) is not { Inode: > 0 } selected)
            throw new IOException($"The identity of {device} could not be verified. Select the device again.");
        SafeFileHandle handle;
        try
        {
            handle = openHandle(device, ct);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && !SameEntry(UnixFiles.Stat(device, followLinks: true), selected))
        {
            // authopen can fail to return a descriptor after approval if the selected device vanished.
            // Preserve actual cancellation, but report a changed source instead of blaming the approval.
            throw new IOException("The selected device changed or was removed while opening. Select it again.", ex);
        }
        try
        {
            if (!SameEntry(UnixFiles.Stat(handle), selected) || !SameEntry(UnixFiles.Stat(device, followLinks: true), selected))
                throw new IOException("The selected device changed or was removed while opening. Select it again.");
            return From(handle, device, description);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static bool SameEntry(UnixStat? actual, UnixStat selected) =>
        actual is { } entry && entry.Device == selected.Device && entry.Inode == selected.Inode;

    /// <summary>Recheck the held descriptor and current path before recovery destination admission.</summary>
    public bool IsCurrentDevicePath(string path)
    {
        if (_identity is not { Inode: > 0 } selected || _handle.IsClosed) return false;
        try
        {
            return SameEntry(UnixFiles.Stat(_handle), selected) && SameEntry(UnixFiles.Stat(path, followLinks: true), selected);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static SafeFileHandle OpenHandle(string device, CancellationToken ct)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = File.OpenHandle(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException)
        {
        }
        return handle ?? (OperatingSystem.IsMacOS() ? AuthOpen.Open(device, ct) : UDisks.Open(device, ct));
    }

    /// <summary>
    /// A source over an open descriptor (a device, or an image file standing in for one in tests). macOS's raw devices
    /// (/dev/rdisk…) read whole sectors only; Linux's block devices read any range (<paramref name="wholeSectors"/>
    /// overrides that for tests).
    /// </summary>
    internal static UnixDeviceSource From(SafeFileHandle handle, string device, string description, bool? wholeSectors = null, int? sectorSize = null)
    {
        long length = UnixNative.DeviceLength(handle, out int sector);
        if (length <= 0) length = RandomAccess.GetLength(handle); // a regular file
        if (length <= 0) throw new IOException($"{device} reports no size: is a medium in it?");
        bool whole = wholeSectors ?? (OperatingSystem.IsMacOS() && Path.GetFileName(device).StartsWith("rdisk", StringComparison.Ordinal));
        return new UnixDeviceSource(handle, description, length, sectorSize ?? sector, whole);
    }

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= Length) return 0;
        if (buffer.Length > Length - offset) buffer = buffer[..(int)(Length - offset)];
        if (!_wholeSectors) return RandomAccess.Read(_handle, buffer, offset);
        // Whole sectors around the range, a bounded piece at a time.
        int done = 0;
        byte[] chunk = ArrayPool<byte>.Shared.Rent(MaxChunk + 2 * _sector);
        try
        {
            while (done < buffer.Length)
            {
                long at = offset + done;
                long start = at / _sector * _sector;
                int want = (int)Math.Min(buffer.Length - done, MaxChunk);
                long end = Math.Min(Length, (at + want + _sector - 1) / _sector * _sector);
                int n = RandomAccess.Read(_handle, chunk.AsSpan(0, (int)(end - start)), start);
                int usable = (int)Math.Min(want, n - (at - start));
                if (usable <= 0) break;
                chunk.AsSpan((int)(at - start), usable).CopyTo(buffer[done..]);
                done += usable;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
        return done;
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>Linux: UDisks2 opens a drive for reading after polkit asks the user, and passes FileCat the descriptor.</summary>
internal static class UDisks
{
    public static SafeFileHandle Open(string device, CancellationToken ct, bool interactive = true)
    {
        string name = Path.GetFileName(device);
        DBusConnection bus;
        try
        {
            bus = DBusConnection.System();
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            throw new NotSupportedException(NoWay(device, "there is no system bus to ask"), ex);
        }
        using (bus)
        using (ct.Register(bus.Dispose))
        {
            try
            {
                DBusValue.Dict options = interactive ? new([]) : new([("auth.no_user_interaction", new DBusValue.Bool(true))]);
                var reply = bus.Call("org.freedesktop.UDisks2", "/org/freedesktop/UDisks2/block_devices/" + Escape(name), "org.freedesktop.UDisks2.Block",
                    "OpenDevice", [new DBusValue.Str("r"), options], DBusConnection.AllowInteractiveAuthorization);
                int index = (int)reply.Body().UInt32();
                for (int i = 0; i < reply.Fds.Count; i++)
                    if (i != index) UnixNative.Close(reply.Fds[i]);
                if (index >= reply.Fds.Count) throw new IOException("UDisks2 answered without handing over the drive.");
                return new SafeFileHandle(reply.Fds[index], ownsHandle: true);
            }
            catch (DBusException ex)
            {
                throw ex.Name switch
                {
                    "org.freedesktop.UDisks2.Error.NotAuthorizedDismissed" => new OperationCanceledException("The request to read the drive was dismissed.", ex),
                    "org.freedesktop.UDisks2.Error.NotAuthorized" or "org.freedesktop.UDisks2.Error.NotAuthorizedCanObtain" =>
                        new UnauthorizedAccessException($"Reading {device} was not authorized: {ex.Message}", ex),
                    "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner" =>
                        new NotSupportedException(NoWay(device, "UDisks2 is not running here"), ex),
                    "org.freedesktop.DBus.Error.UnknownMethod" => new NotSupportedException(NoWay(device, "this UDisks2 is too old to hand out drives (2.7.3 or later can)"), ex),
                    "org.freedesktop.DBus.Error.UnknownObject" => new IOException($"UDisks2 does not know {device}.", ex),
                    _ => new IOException($"UDisks2 did not open {device}: {ex.Message}", ex),
                };
            }
            catch (Exception ex) when (ct.IsCancellationRequested && ex is IOException or ObjectDisposedException or System.Net.Sockets.SocketException)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }

    private static string NoWay(string device, string why) =>
        $"FileCat cannot ask for access to {device}: {why}. Run FileCat as root, add your user to the disk group, or recover from an image of the drive (for example made with dd).";

    /// <summary>UDisks2's object path element for a kernel name: letters and digits as they are, anything else as _ and its hex code.</summary>
    internal static string Escape(string name) =>
        string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c.ToString() : "_" + ((int)c).ToString("x2", CultureInfo.InvariantCulture)));
}

/// <summary>macOS: authopen asks for an administrator's approval and passes FileCat a descriptor that can only read.</summary>
internal static class AuthOpen
{
    public static SafeFileHandle Open(string device, CancellationToken ct)
    {
        var (ours, theirs) = UnixNative.SocketPair();
        int pid;
        try
        {
            pid = UnixNative.Spawn("/usr/libexec/authopen", ["authopen", "-stdoutpipe", device], theirs, ours);
        }
        catch
        {
            UnixNative.Close(ours);
            throw;
        }
        finally
        {
            UnixNative.Close(theirs);
        }
        var fds = new List<int>();
        using (ct.Register(() => UnixNative.Terminate(pid)))
        {
            try
            {
                var buffer = new byte[64];
                while (fds.Count == 0 && UnixNative.Receive(ours, buffer, fds) > 0) { }
            }
            finally
            {
                UnixNative.Close(ours);
            }
            UnixNative.Wait(pid);
        }
        foreach (int extra in fds.Skip(1)) UnixNative.Close(extra);
        if (fds.Count == 0)
        {
            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException($"Reading {device} was not approved.");
        }
        return new SafeFileHandle(fds[0], ownsHandle: true);
    }
}

/// <summary>A disk or partition on Linux or macOS, as recovery offers it (D-47).</summary>
/// <param name="Device">The device to read (/dev/sdb; on macOS the raw /dev/rdisk4).</param>
/// <param name="Disk">The whole disk a partition belongs to; null for a whole disk.</param>
/// <param name="FileSystem">The mounted file system's type, when mounted (vfat, exfat, ntfs3; msdos on macOS).</param>
public sealed record UnixBlockDevice(string Device, long Length, string? Model, string Bus, bool Removable, string? Disk,
    IReadOnlyList<string> MountPoints, string? FileSystem)
{
    public string Name => Path.GetFileName(Device);
}

/// <summary>
/// The disks and partitions of a Linux or macOS computer (D-47), and which disk a folder is on, so recovery never writes
/// to the disk it reads (plan §17.2). Linux: /sys/block and /proc/self/mountinfo; macOS: diskutil.
/// </summary>
public static class UnixDisks
{
    /// <summary>Mounted file systems whose deleted files a scan finds.</summary>
    public static readonly string[] RecoverableTypes = ["vfat", "msdos", "exfat", "ntfs", "ntfs3", "fuseblk"];

    public static IReadOnlyList<UnixBlockDevice> List() =>
        OperatingSystem.IsLinux() ? Linux("/sys", "/proc/self/mountinfo") : OperatingSystem.IsMacOS() ? Mac() : [];

    // ---- Linux ------------------------------------------------------------------------------------------------

    /// <summary>A mounted file system: where, of what type, from which device.</summary>
    internal sealed record Mount(string MajorMinor, string Point, string Type, string Source);

    /// <summary>/proc/self/mountinfo: mount ID, parent, major:minor, root, mount point, options, optional fields, "-", type, source, options.</summary>
    internal static List<Mount> ParseMountInfo(IEnumerable<string> lines)
    {
        var mounts = new List<Mount>();
        foreach (var line in lines)
        {
            var parts = line.Split(' ');
            int dash = Array.IndexOf(parts, "-");
            if (parts.Length < 7 || dash < 6 || dash + 2 >= parts.Length) continue;
            mounts.Add(new Mount(parts[2], Unescape(parts[4]), parts[dash + 1], Unescape(parts[dash + 2])));
        }
        return mounts;
    }

    /// <summary>mountinfo writes spaces, tabs, newlines, and backslashes in paths as three octal digits after a backslash.</summary>
    private static string Unescape(string field)
    {
        if (!field.Contains('\\')) return field;
        var text = new System.Text.StringBuilder(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && IsOctal(field, i + 1))
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8)); // only ASCII characters are escaped
                i += 3;
            }
            else text.Append(field[i]);
        }
        return text.ToString();
    }

    private static bool IsOctal(string s, int at) => at + 3 <= s.Length && s.Substring(at, 3).All(c => c is >= '0' and <= '7');

    private static string? Read(string dir, string file)
    {
        try
        {
            string path = Path.Combine(dir, file);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static List<UnixBlockDevice> Linux(string sys, string mountInfo)
    {
        var mounts = File.Exists(mountInfo) ? ParseMountInfo(File.ReadLines(mountInfo)) : [];
        var result = new List<UnixBlockDevice>();
        string blocks = Path.Combine(sys, "block");
        if (!Directory.Exists(blocks)) return result;
        foreach (var dir in Directory.EnumerateFileSystemEntries(blocks).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            // RAM disks, optical drives, floppies, and the device mapper's and RAID's layers (their disks are listed).
            if (name.StartsWith("ram", StringComparison.Ordinal) || name.StartsWith("zram", StringComparison.Ordinal) || name.StartsWith("sr", StringComparison.Ordinal) ||
                name.StartsWith("fd", StringComparison.Ordinal) || name.StartsWith("dm-", StringComparison.Ordinal) || name.StartsWith("md", StringComparison.Ordinal)) continue;
            long length = Sectors(dir);
            if (length <= 0) continue; // no medium, or an unused loop device
            string real = RealPath(dir);
            string bus = name.StartsWith("nvme", StringComparison.Ordinal) ? "NVMe"
                : name.StartsWith("mmcblk", StringComparison.Ordinal) ? "SD card"
                : name.StartsWith("loop", StringComparison.Ordinal) ? "loop device"
                : real.Contains("/usb", StringComparison.Ordinal) ? "USB"
                : name.StartsWith("vd", StringComparison.Ordinal) || real.Contains("/virtio", StringComparison.Ordinal) ? "virtual"
                : real.Contains("/ata", StringComparison.Ordinal) ? "SATA" : "";
            string? model = string.Join(' ', new[] { Read(dir, "device/vendor"), Read(dir, "device/model") ?? Read(dir, "device/name") }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => string.Join(' ', s!.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
            if (model.Length == 0) model = null;
            bool removable = Read(dir, "removable") == "1";
            result.Add(new UnixBlockDevice("/dev/" + name, length, model, bus, removable, null, MountsOf(mounts, dir, name).Select(m => m.Point).ToList(),
                MountsOf(mounts, dir, name).FirstOrDefault()?.Type));
            IEnumerable<string> partitions;
            try
            {
                partitions = Directory.EnumerateDirectories(dir).Where(d => File.Exists(Path.Combine(d, "partition"))).Order(StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                partitions = [];
            }
            foreach (var part in partitions)
            {
                string partName = Path.GetFileName(part);
                var partMounts = MountsOf(mounts, part, partName).ToList();
                result.Add(new UnixBlockDevice("/dev/" + partName, Sectors(part), model, bus, removable, "/dev/" + name, partMounts.Select(m => m.Point).ToList(),
                    partMounts.FirstOrDefault()?.Type));
            }
        }
        return result;
    }

    private static long Sectors(string dir) => long.TryParse(Read(dir, "size"), NumberStyles.None, CultureInfo.InvariantCulture, out long sectors) ? sectors * 512 : 0;

    /// <summary>Mounts of a block device: by its device numbers, or by its path (FUSE mounts such as ntfs-3g have numbers of their own).</summary>
    private static IEnumerable<Mount> MountsOf(List<Mount> mounts, string dir, string name) =>
        mounts.Where(m => m.MajorMinor == Read(dir, "dev") || m.Source == "/dev/" + name);

    private static string RealPath(string path)
    {
        try
        {
            FileSystemInfo item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return item.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>File systems in memory: writing there touches no disk.</summary>
    private static readonly string[] InMemory = ["tmpfs", "ramfs"];

    /// <summary>File systems a virtual machine's host shares with it: stored on the host's disks, none of this system's.</summary>
    private static readonly string[] HostShares = ["9p", "virtiofs", "vboxsf", "fuse.vmhgfs-fuse"];

    /// <summary>File systems on another computer, named in the mount's source: on its disks, unless that computer is this one.</summary>
    private static readonly string[] Network = ["nfs", "nfs4", "cifs", "smb3", "fuse.sshfs", "smbfs", "afpfs", "webdav"];

    /// <summary>
    /// Linux: which whole disks a device or a folder lies on, from sysfs and the mount table (tests pass their own of each,
    /// with how a folder's real path is found and which servers are this computer). Null whenever that cannot be told (no
    /// sysfs entry, a device served over the network), which never counts as another disk (release plan V09).
    /// </summary>
    internal sealed class LinuxTopology(string sys, IReadOnlyList<Mount> mounts, Func<string, string?> resolve, Func<string, bool> thisComputer)
    {
        /// <summary>A device that is read (/dev/sdb1, /dev/mapper/…): the disks it lies on.</summary>
        public IReadOnlyList<string>? DeviceDisks(string device) => BlockDisks(Path.GetFileName(RealPath(device)), written: false);

        /// <summary>
        /// A folder that is written to, placed by its real path (every link resolved) on the mount that holds it; empty when
        /// that is memory or another computer's storage. A folder under /dev (/dev/shm) is a folder like any other.
        /// </summary>
        public IReadOnlyList<string>? FolderDisks(string folder, int depth = 0)
        {
            if (depth > 8) return null;
            if (resolve(folder) is not { } full) return null;
            // The mount over a folder is the last one listed at the longest point that holds it (a later mount hides an earlier).
            var mount = mounts.Select((m, i) => (Mount: m, Order: i))
                .Where(x => full == x.Mount.Point || full.StartsWith(x.Mount.Point.TrimEnd('/') + "/", StringComparison.Ordinal))
                .OrderByDescending(x => x.Mount.Point.Length).ThenByDescending(x => x.Order).Select(x => x.Mount).FirstOrDefault();
            if (mount is null) return null;
            if (InMemory.Contains(mount.Type) || HostShares.Contains(mount.Type)) return [];
            if (Network.Contains(mount.Type)) return ServerOf(mount.Source) is { } server && !thisComputer(server) ? [] : null;
            if (mount.Type == "fuse.gvfsd-fuse") return GvfsDisks(mount.Point, full, thisComputer);
            if (mount.Type == "btrfs") return BtrfsMembers(mount, depth);
            // bcachefs names all its devices, joined by colons.
            if (mount.Source.StartsWith("/dev/", StringComparison.Ordinal))
            {
                var disks = new List<string>();
                foreach (var device in mount.Source.Split(':'))
                {
                    if (!device.StartsWith("/dev/", StringComparison.Ordinal) ||
                        BlockDisks(Path.GetFileName(RealPath(device)), written: true, depth + 1) is not { } under) return null;
                    disks.AddRange(under);
                }
                return disks.Distinct().ToList();
            }
            string byNumbers = Path.Combine(sys, "dev", "block", mount.MajorMinor);
            return Directory.Exists(byNumbers) ? BlockDisks(Path.GetFileName(RealPath(byNumbers)), written: true, depth + 1) : null;
        }

        /// <summary>
        /// A block device (sdb1, dm-0, loop0): a partition's disk, a mapped device's underlying ones. Writing to a loop
        /// device writes its backing file, so where something is written (<paramref name="written"/>) a loop device lies on
        /// itself and on that file's disks; read, it is a disk of its own (writing next to an image file never changes what
        /// the image holds, but writing into the image does).
        /// </summary>
        public IReadOnlyList<string>? BlockDisks(string name, bool written, int depth = 0)
        {
            string dir = Path.Combine(sys, "class", "block", name);
            if (depth > 8 || !Directory.Exists(dir)) return null;
            if (File.Exists(Path.Combine(dir, "partition")))
                return BlockDisks(Path.GetFileName(Path.GetDirectoryName(RealPath(dir))!), written, depth + 1);
            if (name.StartsWith("loop", StringComparison.Ordinal))
            {
                if (!written) return [name];
                // One whose file was deleted, or that has none, cannot be placed.
                string? backing = Read(dir, "loop/backing_file");
                return backing is null || !backing.StartsWith('/') || backing.EndsWith(" (deleted)", StringComparison.Ordinal) ||
                       FolderDisks(backing, depth + 1) is not { } file ? null : [name, .. file];
            }
            // Userspace/network exports (qemu-nbd, Ceph) do not expose their backing disks here. A local server may
            // export a device that also holds FileCat's write folders, so a source is unknown just like a write.
            if (name.StartsWith("nbd", StringComparison.Ordinal) || name.StartsWith("rbd", StringComparison.Ordinal)) return null;
            string slaves = Path.Combine(dir, "slaves");
            // An unavailable dependency directory is incomplete topology, not proof of an independent leaf.
            // It can disappear while a mapped device is being removed; unknown must never establish another disk.
            if (!Directory.Exists(slaves)) return null;
            var members = Directory.EnumerateFileSystemEntries(slaves).Select(Path.GetFileName).OfType<string>().ToList();
            if (members.Count == 0) return [name];
            var disks = new List<string>();
            foreach (var member in members)
            {
                if (BlockDisks(member, written, depth + 1) is not { } under) return null;
                disks.AddRange(under);
            }
            return disks.Distinct().ToList();
        }

        /// <summary>A btrfs file system's disks: every device it spans (sysfs lists them by its id), not only the one mounted.</summary>
        private IReadOnlyList<string>? BtrfsMembers(Mount mount, int depth)
        {
            string root = Path.Combine(sys, "fs", "btrfs");
            if (!mount.Source.StartsWith("/dev/", StringComparison.Ordinal) || !Directory.Exists(root)) return null;
            string name = Path.GetFileName(RealPath(mount.Source));
            foreach (var fs in Directory.EnumerateDirectories(root))
            {
                string devices = Path.Combine(fs, "devices");
                if (!Directory.Exists(devices) || !Directory.EnumerateFileSystemEntries(devices).Any(d => Path.GetFileName(d) == name)) continue;
                var disks = new List<string>();
                foreach (var device in Directory.EnumerateFileSystemEntries(devices))
                {
                    if (BlockDisks(Path.GetFileName(device), written: true, depth + 1) is not { } under) return null;
                    disks.AddRange(under);
                }
                return disks.Distinct().ToList();
            }
            return null;
        }
    }

    /// <summary>
    /// GVFS's folders (/run/user/ID/gvfs/smb-share:server=…,share=…): another computer's storage when the folder names a
    /// server that is not this one; unknown for everything else GVFS serves (archives, which are local files; trash).
    /// </summary>
    private static IReadOnlyList<string>? GvfsDisks(string point, string full, Func<string, bool> thisComputer)
    {
        if (full.Length <= point.TrimEnd('/').Length + 1) return null;
        string first = full[(point.TrimEnd('/').Length + 1)..].Split('/')[0];
        int colon = first.IndexOf(':');
        if (colon < 0) return null;
        string scheme = first[..colon];
        if (scheme is not ("smb-share" or "sftp" or "ftp" or "ftps" or "dav" or "davs" or "afp-volume" or "nfs" or "mtp" or "gphoto2" or "afc")) return null;
        // A phone or a camera is a device of its own.
        if (scheme is "mtp" or "gphoto2" or "afc") return [];
        foreach (var setting in first[(colon + 1)..].Split(','))
            if (setting.StartsWith("server=", StringComparison.Ordinal) || setting.StartsWith("host=", StringComparison.Ordinal))
            {
                string server = Uri.UnescapeDataString(setting[(setting.IndexOf('=') + 1)..]);
                return thisComputer(server) ? null : [];
            }
        return null;
    }

    /// <summary>The server a network mount's source names: //server/share, server:/export, [v6]:/export, user@server:path, a URL.</summary>
    internal static string? ServerOf(string source)
    {
        string s = source;
        if (Uri.TryCreate(s, UriKind.Absolute, out var url) && url.Scheme is "http" or "https") return url.Host;
        if (s.StartsWith("//", StringComparison.Ordinal))
        {
            s = s[2..];
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s[..slash];
            int at = s.LastIndexOf('@');
            return at >= 0 ? s[(at + 1)..] : s;
        }
        int user = s.IndexOf('@');
        if (user >= 0 && user < s.IndexOf(':')) s = s[(user + 1)..];
        if (s.StartsWith('['))
        {
            int close = s.IndexOf(']');
            return close > 1 ? s[1..close] : null;
        }
        int separator = s.IndexOf(':');
        return separator > 0 ? s[..separator] : null;
    }

    /// <summary>A folder's real path: the nearest part of it that exists with every link resolved, then the rest as written.</summary>
    internal static string? ResolveExisting(string path)
    {
        string full = Path.GetFullPath(path);
        var rest = new Stack<string>();
        string? at = full;
        while (at is not null)
        {
            if (Directory.Exists(at) || File.Exists(at))
            {
                if (UnixFiles.RealPath(at) is not { } real) return null;
                foreach (var part in rest) real = Path.Combine(real, part);
                return real;
            }
            rest.Push(Path.GetFileName(at));
            at = Path.GetDirectoryName(at);
        }
        return null;
    }

    /// <summary>The whole disks a device that is read lies on (sdb; disk0 on macOS); null when unknown.</summary>
    public static IReadOnlyList<string>? DeviceDisks(string device) => Guarded(() =>
        OperatingSystem.IsLinux() ? new LinuxTopology("/sys", [], ResolveExisting, ThisComputerLookup).DeviceDisks(device)
        : OperatingSystem.IsMacOS() ? MacDisksOf(device, device: true, 0) : null);

    /// <summary>
    /// The whole disks writing into a folder writes to; empty when it is on no local disk (memory, another computer's
    /// storage); null when unknown, which never counts as another disk.
    /// </summary>
    public static IReadOnlyList<string>? FolderDisks(string folder) => Guarded(() =>
        OperatingSystem.IsLinux() ? new LinuxTopology("/sys", ParseMountInfo(File.ReadLines("/proc/self/mountinfo")), ResolveExisting, ThisComputerLookup).FolderDisks(folder)
        : OperatingSystem.IsMacOS() ? MacDisksOf(folder, device: false, 0) : null);

    private static IReadOnlyList<string>? Guarded(Func<IReadOnlyList<string>?> find)
    {
        try
        {
            return find();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>A device node (/dev/sdb1, /dev/mapper/…, /dev/disk/by-id/…), as opposed to a folder that happens to be under /dev (/dev/shm).</summary>
    internal static bool IsDevicePath(string path) =>
        path.StartsWith("/dev/", StringComparison.Ordinal) && !Directory.Exists(path) &&
        (path.IndexOf('/', 5) < 0 || path.StartsWith("/dev/mapper/", StringComparison.Ordinal) || path.StartsWith("/dev/disk/", StringComparison.Ordinal) ||
         path.StartsWith("/dev/md/", StringComparison.Ordinal));

    /// <summary>
    /// The whole disks a device or a folder lies on (sdb; disk0 on macOS); empty when a folder is on no local disk
    /// (memory, another computer's storage); null when unknown, which never counts as another disk.
    /// </summary>
    public static IReadOnlyList<string>? DisksOf(string pathOrDevice) => IsDevicePath(pathOrDevice) ? DeviceDisks(pathOrDevice) : FolderDisks(pathOrDevice);

    private static bool ThisComputerLookup(string server) => FileCat.Core.Network.ThisComputer.Is(server, TimeSpan.FromSeconds(2));

    /// <summary>
    /// macOS: a device (read), or a folder (written to) by its real path on the volume that holds it; an APFS volume's
    /// physical stores. Written to, an attached disk image lies where its image file is; read, it is a disk of its own.
    /// </summary>
    private static IReadOnlyList<string>? MacDisksOf(string pathOrDevice, bool device, int depth)
    {
        if (depth > 8) return null;
        string target;
        bool written = !device;
        if (device) target = pathOrDevice.Replace("/dev/rdisk", "/dev/disk", StringComparison.Ordinal);
        else
        {
            if (ResolveExisting(pathOrDevice) is not { } full || UnixFiles.MountOf(full) is not { } mount) return null;
            string type = mount.DriveFormat;
            if (InMemory.Contains(type) || HostShares.Contains(type)) return [];
            if (Network.Contains(type) || type is "nfs" or "ftp")
                return MacMountSource(mount.Name) is { } source && ServerOf(source) is { } server && !ThisComputerLookup(server) ? [] : null;
            target = mount.Name;
        }
        // A volume can be replaced at the same mount point, or a device path reused. A previous answer cannot
        // establish safe recovery placement: query the current backing disks whenever admission is checked.
        return MacWholeDisks(target, written, depth);
    }

    /// <summary>macOS: the whole disks under a device or a mounted volume (<paramref name="target"/>), from diskutil.</summary>
    private static IReadOnlyList<string>? MacWholeDisks(string target, bool written, int depth)
        => MacWholeDisks(target, written,
            name => Run("/usr/sbin/diskutil", "info", "-plist", name),
            whole => ImageFileOf("/dev/" + whole) is { } image ? MacDisksOf(image, device: false, depth + 1) : null);

    /// <summary>Resolve every backing member; incomplete replies cannot establish an independent disk.</summary>
    internal static IReadOnlyList<string>? MacWholeDisks(string target, bool written,
        Func<string, string> diskInfo, Func<string, IReadOnlyList<string>?> imageDisks)
    {
        if (Plist(diskInfo(target)) is not Dictionary<string, object?> info) return null;
        List<string> wholes;
        if (info.TryGetValue("APFSPhysicalStores", out var value))
        {
            if (value is not List<object?> stores) return null;
            wholes = [];
            foreach (var store in stores)
            {
                // Dropping an unavailable member would turn a partial answer into proof of another disk.
                if (store is not Dictionary<string, object?> member ||
                    WholeDisk(member.GetValueOrDefault("APFSPhysicalStore") as string) is not { Length: > 0 } disk) return null;
                wholes.Add(disk);
            }
        }
        else if (info.GetValueOrDefault("ParentWholeDisk") is string { Length: > 0 } whole) wholes = [whole];
        else return null;
        if (wholes.Count == 0) return null;
        var disks = new List<string>();
        foreach (var whole in wholes.Distinct())
        {
            if (!written)
            {
                disks.Add(whole);
                continue;
            }
            // A missing reply can be a disappearing image disk. It does not prove physical backing.
            if (Plist(diskInfo(whole)) is not Dictionary<string, object?> wholeInfo ||
                wholeInfo.GetValueOrDefault("BusProtocol") is not string bus || string.IsNullOrWhiteSpace(bus)) return null;
            if (bus != "Disk Image")
            {
                disks.Add(whole);
                continue;
            }
            // An attached disk image is itself, stored in its image file, wherever that is.
            if (imageDisks(whole) is not { } under) return null;
            disks.Add(whole);
            disks.AddRange(under);
        }
        return disks.Distinct().ToList();
    }

    /// <summary>The image file an attached disk image (/dev/diskN) is read from (hdiutil info), or null.</summary>
    private static string? ImageFileOf(string device)
    {
        if (Plist(Run("/usr/bin/hdiutil", "info", "-plist")) is not Dictionary<string, object?> info || info.GetValueOrDefault("images") is not List<object?> images) return null;
        foreach (var image in images.OfType<Dictionary<string, object?>>())
        {
            if (image.GetValueOrDefault("system-entities") is not List<object?> entities) continue;
            if (entities.OfType<Dictionary<string, object?>>().Any(e => e.GetValueOrDefault("dev-entry") as string == device))
                return image.GetValueOrDefault("image-path") is string path && path.StartsWith('/') ? path : null;
        }
        return null;
    }

    /// <summary>What a macOS mount point is mounted from (//user@server/share for a share), as mount(8) lists it.</summary>
    private static string? MacMountSource(string point)
    {
        string trimmed = point.Length > 1 ? point.TrimEnd('/') : point;
        foreach (var line in Run("/sbin/mount").Split('\n'))
        {
            int on = line.IndexOf(" on " + trimmed + " (", StringComparison.Ordinal);
            if (on > 0) return line[..on];
        }
        return null;
    }

    /// <summary>"disk0" of "disk0s2".</summary>
    private static string? WholeDisk(string? identifier)
    {
        if (identifier is null || identifier.Length < 5) return null;
        int s = identifier.IndexOf('s', 4);
        return s > 0 ? identifier[..s] : identifier;
    }

    /// <summary>Whether writing into <paramref name="folder"/> would write to a disk <paramref name="device"/> lies on: true, false, or null when unknown.</summary>
    public static bool? SharesDisk(string device, string folder)
    {
        var source = DeviceDisks(device);
        var target = FolderDisks(folder);
        if (source is null || target is null) return null;
        return source.Intersect(target).Any();
    }

    // ---- macOS ------------------------------------------------------------------------------------------------

    internal static List<UnixBlockDevice> Mac()
    {
        var result = new List<UnixBlockDevice>();
        if (Plist(Run("/usr/sbin/diskutil", "list", "-plist")) is not Dictionary<string, object?> list ||
            list.GetValueOrDefault("AllDisksAndPartitions") is not List<object?> disks) return result;
        foreach (var entry in disks.OfType<Dictionary<string, object?>>())
        {
            if (entry.GetValueOrDefault("DeviceIdentifier") is not string id) continue;
            var info = Plist(Run("/usr/sbin/diskutil", "info", "-plist", id)) as Dictionary<string, object?> ?? [];
            // APFS containers are made up from partitions of real disks (listed there).
            if (info.ContainsKey("APFSPhysicalStores") || entry.ContainsKey("APFSVolumes") && !entry.ContainsKey("Partitions")) continue;
            long size = Long(entry.GetValueOrDefault("Size"));
            string? model = info.GetValueOrDefault("MediaName") as string;
            string bus = info.GetValueOrDefault("BusProtocol") as string ?? "";
            bool removable = info.GetValueOrDefault("RemovableMedia") is true || info.GetValueOrDefault("Removable") is true || info.GetValueOrDefault("Internal") is false;
            result.Add(new UnixBlockDevice("/dev/r" + id, size, string.IsNullOrWhiteSpace(model) ? null : model.Trim(), bus, removable, null, [], null));
            foreach (var part in (entry.GetValueOrDefault("Partitions") as List<object?> ?? []).OfType<Dictionary<string, object?>>())
            {
                if (part.GetValueOrDefault("DeviceIdentifier") is not string partId) continue;
                string? mount = part.GetValueOrDefault("MountPoint") as string;
                string? type = null;
                if (mount is not null && Plist(Run("/usr/sbin/diskutil", "info", "-plist", partId)) is Dictionary<string, object?> partInfo)
                    type = partInfo.GetValueOrDefault("FilesystemType") as string;
                result.Add(new UnixBlockDevice("/dev/r" + partId, Long(part.GetValueOrDefault("Size")), model, bus, removable, "/dev/r" + id,
                    mount is null ? [] : [mount], type));
            }
        }
        return result;
    }

    private static long Long(object? value) => value is long l ? l : 0;

    private static string Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new IOException(program + " did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new IOException(program + " did not answer.");
        }
        return output.Result;
    }

    /// <summary>A property list's value: dictionaries, arrays, strings, integers (long), and booleans; other types as their text.</summary>
    internal static object? Plist(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        var root = XDocument.Parse(xml, LoadOptions.None).Root?.Elements().FirstOrDefault();
        return root is null ? null : Value(root);
        static object? Value(XElement e) => e.Name.LocalName switch
        {
            "dict" => Dict(e),
            "array" => e.Elements().Select(Value).ToList(),
            "integer" => long.TryParse(e.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0L,
            "true" => true,
            "false" => false,
            _ => e.Value,
        };
        static Dictionary<string, object?> Dict(XElement e)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            string? key = null;
            foreach (var child in e.Elements())
            {
                if (child.Name.LocalName == "key") key = child.Value;
                else if (key is not null)
                {
                    dict[key] = Value(child);
                    key = null;
                }
            }
            return dict;
        }
    }
}
