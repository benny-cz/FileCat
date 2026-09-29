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
public sealed class UnixDeviceSource : IBlockSource
{
    private const int MaxChunk = 4 * 1024 * 1024;
    private readonly SafeFileHandle _handle;
    private readonly int _sector;
    private readonly bool _wholeSectors;

    private UnixDeviceSource(SafeFileHandle handle, string description, long length, int sector, bool wholeSectors)
    {
        _handle = handle;
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
    public static UnixDeviceSource Open(string device, string description, CancellationToken ct)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = File.OpenHandle(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException)
        {
        }
        handle ??= OperatingSystem.IsMacOS() ? AuthOpen.Open(device, ct) : UDisks.Open(device, ct);
        try
        {
            return From(handle, device, description);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
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
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>
    /// Linux: the whole disks a block device (sdb1, dm-0) lies on, from sysfs: a partition's parent, a mapped device's
    /// underlying ones.
    /// </summary>
    internal static List<string> LinuxDisksOf(string sys, string name, int depth = 0)
    {
        string dir = Path.Combine(sys, "class", "block", name);
        if (depth > 8 || !Directory.Exists(dir)) return [];
        string real = RealPath(dir);
        if (File.Exists(Path.Combine(dir, "partition"))) return [Path.GetFileName(Path.GetDirectoryName(real)!)];
        string slaves = Path.Combine(dir, "slaves");
        if (Directory.Exists(slaves) && Directory.EnumerateFileSystemEntries(slaves).Any())
            return Directory.EnumerateFileSystemEntries(slaves).SelectMany(s => LinuxDisksOf(sys, Path.GetFileName(s), depth + 1)).Distinct().ToList();
        return [name];
    }

    /// <summary>File systems that live in memory or on another computer: writing there never touches a local disk.</summary>
    private static readonly string[] NotOnDisk = ["tmpfs", "ramfs", "nfs", "nfs4", "cifs", "smb3", "fuse.sshfs", "9p", "virtiofs", "fuse.rclone"];

    /// <summary>
    /// The whole disks a device or a folder lies on (sdb; disk0 on macOS); empty when a folder is on no local disk
    /// (memory, network); null when unknown.
    /// </summary>
    public static IReadOnlyList<string>? DisksOf(string pathOrDevice)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                if (pathOrDevice.StartsWith("/dev/", StringComparison.Ordinal)) return LinuxDisksOf("/sys", Path.GetFileName(RealPath(pathOrDevice)));
                var mounts = ParseMountInfo(File.ReadLines("/proc/self/mountinfo"));
                string full = Path.GetFullPath(pathOrDevice);
                var mount = mounts.Where(m => full == m.Point || full.StartsWith(m.Point.TrimEnd('/') + "/", StringComparison.Ordinal))
                    .OrderByDescending(m => m.Point.Length).FirstOrDefault();
                if (mount is null) return null;
                if (NotOnDisk.Contains(mount.Type)) return [];
                if (mount.Source.StartsWith("/dev/", StringComparison.Ordinal)) return LinuxDisksOf("/sys", Path.GetFileName(RealPath(mount.Source)));
                string byNumbers = Path.Combine("/sys/dev/block", mount.MajorMinor);
                return Directory.Exists(byNumbers) ? LinuxDisksOf("/sys", Path.GetFileName(RealPath(byNumbers))) : null;
            }
            if (OperatingSystem.IsMacOS())
            {
                string target = pathOrDevice.StartsWith("/dev/", StringComparison.Ordinal) ? pathOrDevice.Replace("/dev/rdisk", "/dev/disk", StringComparison.Ordinal)
                    : UnixFiles.MountOf(Path.GetFullPath(pathOrDevice))?.Name ?? pathOrDevice;
                if (Plist(Run("/usr/sbin/diskutil", "info", "-plist", target)) is not Dictionary<string, object?> info) return null;
                if (info.GetValueOrDefault("APFSPhysicalStores") is List<object?> stores)
                    return stores.OfType<Dictionary<string, object?>>().Select(s => WholeDisk(s.GetValueOrDefault("APFSPhysicalStore") as string)).OfType<string>().Distinct().ToList();
                return info.GetValueOrDefault("ParentWholeDisk") is string whole ? [whole] : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
        {
        }
        return null;
    }

    /// <summary>"disk0" of "disk0s2".</summary>
    private static string? WholeDisk(string? identifier)
    {
        if (identifier is null) return null;
        int s = identifier.IndexOf('s', 4);
        return s > 0 ? identifier[..s] : identifier;
    }

    /// <summary>Whether writing into <paramref name="folder"/> would write to a disk <paramref name="device"/> lies on: true, false, or null when unknown.</summary>
    public static bool? SharesDisk(string device, string folder)
    {
        var source = DisksOf(device);
        var target = DisksOf(folder);
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
