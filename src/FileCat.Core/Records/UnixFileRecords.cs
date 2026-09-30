using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

/// <summary>
/// What Linux and macOS file systems record about a file or folder (D-56), read without changing it. Linux: statx
/// (birth time, attribute flags, mount), the inode flags lsattr shows, generation, project ID, extents (FIEMAP, shared
/// ones included), the mount from mountinfo, and ext4's journal summary. macOS: getattrlist (birth, added, and backup
/// times, BSD flags as chflags sets them, clones, private size, document ID, generation count). Both: every time to the
/// nanosecond with timestamp checks, and permissions in words (mode, owner, POSIX ACLs or the macOS ACL, SELinux,
/// capabilities) with the weak ones flagged.
/// </summary>
public sealed unsafe partial class UnixFileRecords : IFileRecords
{
    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public InspectionReport Read(string path, CancellationToken ct)
    {
        if (!IsSupported) throw new NotSupportedException("File-system records are read on Linux and macOS here.");
        string full = Path.GetFullPath(path);
        if (full.Length > 1) full = full.TrimEnd('/');
        return new Reader(full, ct).Run();
    }

    private const int TypeMask = 0xF000;

    private sealed record Times(UnixTime? Birth, UnixTime Modified, UnixTime Changed, UnixTime Accessed);

    private sealed class Reader(string path, CancellationToken ct)
    {
        private readonly List<InspectionSection> _sections = [];
        private readonly List<string> _warnings = [];
        private int _mode;
        private uint _uid, _gid;
        private bool IsFolder => (_mode & TypeMask) == 0x4000;
        private bool IsRegular => (_mode & TypeMask) == 0x8000;

        public InspectionReport Run()
        {
            string format = OperatingSystem.IsLinux() ? Linux() : Mac();
            ct.ThrowIfCancellationRequested();
            if (HiddenData.HiddenDataSection.Of(new HiddenData.UnixHiddenData(), path) is { } hidden) _sections.Add(hidden);
            _sections.Add(Permissions());
            return new InspectionReport(format, _sections, _warnings);
        }

        // ---- Linux ------------------------------------------------------------------------------------------

        private string Linux()
        {
            const uint Mask = 0x7FF | 0x800 /* btime */ | 0x1000 /* mount ID */ | 0x2000 /* direct I/O alignment */;
            byte* b = stackalloc byte[256];
            new Span<byte>(b, 256).Clear();
            if (Statx(-100 /* AT_FDCWD */, path, 0x100 /* AT_SYMLINK_NOFOLLOW */, Mask, b) != 0) throw Error(path);
            uint got = *(uint*)b;
            _mode = *(ushort*)(b + 28);
            _uid = *(uint*)(b + 20);
            _gid = *(uint*)(b + 24);
            ulong inode = *(ulong*)(b + 32);
            long size = (long)*(ulong*)(b + 40), blocks = (long)*(ulong*)(b + 48);
            ulong attributes = *(ulong*)(b + 8), known = *(ulong*)(b + 56);
            static UnixTime At(byte* p) => new(*(long*)p, *(uint*)(p + 8));
            var times = new Times((got & 0x800) != 0 ? At(b + 80) : null, At(b + 112), At(b + 96), At(b + 64));
            uint devMajor = *(uint*)(b + 136), devMinor = *(uint*)(b + 140);
            ulong mountId = (got & 0x1000) != 0 ? *(ulong*)(b + 144) : 0;
            var mount = mountId != 0 ? Mount(mountId) : null;

            var item = new List<(string, string)>
            {
                ("Path", path),
                ("Kind", Kind(_mode, (*(uint*)(b + 128), *(uint*)(b + 132)))),
            };
            if (mount is { } m) item.Add(("Volume", $"{m.MountPoint} · {m.Type} · {m.Source}"));
            item.Add(("Device", $"{devMajor}:{devMinor}"));
            item.Add(("Inode", inode.ToString("N0", CultureInfo.CurrentCulture)));
            item.Add(("Hard links", (*(uint*)(b + 16)).ToString(CultureInfo.CurrentCulture)));
            if ((_mode & TypeMask) == 0xA000 && Target() is { } target) item.Add(("Link to", target));
            var flags = Words(attributes & known, StatxAttributes);
            if (flags.Length > 0) item.Add(("Attributes", flags));
            var lines = new List<string>();
            var inodeFlags = IsRegular || IsFolder ? InodeFlags(inode) : null;
            if (inodeFlags is { } f)
            {
                item.Add(("Inode flags", f.Text));
                if (f.Generation is { } generation) item.Add(("Generation", generation.ToString(CultureInfo.CurrentCulture)));
                if (f.Project is { } project) item.Add(("Project ID", project.ToString(CultureInfo.CurrentCulture)));
                if (f.Problem is { } problem) lines.Add(problem);
            }
            _sections.Add(new InspectionSection("Item", item) { Lines = lines });
            _sections.Add(TimesSection(times, "Linux keeps four times to the nanosecond. Programs can set the access and modification times; the status change time moves to the present whenever anything about the item changes, and nothing can set it. Birth is when the inode was made (not every file system keeps it)."));
            _sections.Add(ChecksSection(times));
            _sections.Add(LayoutSection(size, blocks * 512, (int)*(uint*)(b + 4), inodeFlags?.Extents));
            if (mount is { } fat && FatKind(fat.Type) is { } typed && FatSection(fat, sniff: !typed) is { } entry) _sections.Add(entry);
            if (mount is { } volume) _sections.Add(VolumeSection(volume));
            return $"File-system record · {mount?.Type ?? "Linux"}";
        }

        private sealed record MountInfo(string MountPoint, string Type, string Source, string Options, string DeviceNumber);

        /// <summary>
        /// True for FAT and exFAT mount types (the kernel's, or a FUSE subtype such as "fuseblk.exfat"); false for FUSE
        /// block mounts that do not say (exfat-fuse, ntfs-3g), whose boot sector tells; null for everything else.
        /// </summary>
        private static bool? FatKind(string type) => type switch
        {
            "vfat" or "msdos" or "exfat" => true,
            "fuseblk" or "fuse" => false,
            _ when type.StartsWith("fuse", StringComparison.Ordinal) && type.EndsWith(".exfat", StringComparison.Ordinal) => true,
            _ => null,
        };

        /// <summary>
        /// The item's raw directory entry on a FAT or exFAT volume, read from its device (root only). With
        /// <paramref name="sniff"/>, the volume is first asked whether it is FAT at all (nothing is said when it is not).
        /// </summary>
        private InspectionSection? FatSection(MountInfo mount, bool sniff)
        {
            string relative = Path.GetRelativePath(mount.MountPoint, path);
            if (relative == ".") return null;
            if (GetEuid() != 0)
                return sniff ? null : new InspectionSection("Directory entry", []) { Lines = RecordText.Wrap($"Its raw directory entry (the 8.3 name and case bits, every time as FAT keeps it, its first cluster) is read from {mount.Source}, which needs root.") };
            int fd = Open(mount.Source, 0x80000 /* O_RDONLY | O_CLOEXEC */);
            if (fd < 0) return sniff ? null : new InspectionSection("Directory entry", []) { Lines = RecordText.Wrap($"{mount.Source} could not be opened: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}") };
            try
            {
                byte[] Read(long offset, int count)
                {
                    var buffer = new byte[count];
                    long got;
                    fixed (byte* b = buffer) got = PRead(fd, b, (nuint)count, offset);
                    return got <= 0 ? [] : buffer.AsSpan(0, (int)got).ToArray();
                }
                if (sniff && !FatEntries.IsFatBootSector(Read(0, 512))) return null;
                return FatEntries.Describe(Read, relative, out string? problem) ?? new InspectionSection("Directory entry", []) { Lines = RecordText.Wrap(problem ?? "It was not found.") };
            }
            finally { Close(fd); }
        }

        /// <summary>The mount with this ID in /proc/self/mountinfo: ID, parent, major:minor, root, mount point, options, …, "-", type, source, super options.</summary>
        private static MountInfo? Mount(ulong id)
        {
            try
            {
                foreach (string line in File.ReadLines("/proc/self/mountinfo"))
                {
                    var fields = line.Split(' ');
                    if (fields.Length < 10 || fields[0] != id.ToString(CultureInfo.InvariantCulture)) continue;
                    int dash = Array.IndexOf(fields, "-");
                    if (dash < 0 || dash + 3 >= fields.Length) return null;
                    return new MountInfo(Unescape(fields[4]), fields[dash + 1], Unescape(fields[dash + 2]), fields[5] + "; " + fields[dash + 3], fields[2]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return null;
        }

        /// <summary>mountinfo writes space, tab, newline, and backslash as octal escapes.</summary>
        private static string Unescape(string field)
        {
            if (!field.Contains('\\')) return field;
            var sb = new StringBuilder();
            for (int i = 0; i < field.Length; i++)
            {
                if (field[i] == '\\' && i + 3 < field.Length && int.TryParse(field.AsSpan(i + 1, 3), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    sb.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                    i += 3;
                }
                else sb.Append(field[i]);
            }
            return sb.ToString();
        }

        private sealed record InodeInfo(string Text, uint? Generation, uint? Project, string? Problem, List<(long Logical, long Physical, long Length, uint Flags)>? Extents);

        /// <summary>The flags lsattr shows, the generation, the project ID, and the extents, through a descriptor opened only to read.</summary>
        private InodeInfo? InodeFlags(ulong inode)
        {
            // O_RDONLY | O_NONBLOCK | O_NOCTTY | O_CLOEXEC | O_NOFOLLOW (whose value depends on the architecture).
            int noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm or Architecture.Ppc64le ? 0x8000 : 0x20000;
            int fd = Open(path, 0x800 | 0x100 | 0x80000 | noFollow);
            if (fd < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                return new InodeInfo(errno == 13 ? "not shown: they need permission to read the item (its extents too)" : $"not read: {Marshal.GetPInvokeErrorMessage(errno)}", null, null, null, null);
            }
            try
            {
                // The same inode that statx described, not something put in its place since.
                byte* check = stackalloc byte[256];
                if (Statx(fd, "", 0x1000 /* AT_EMPTY_PATH */, 0x100, check) != 0 || *(ulong*)(check + 32) != inode) return null;
                string text = "not kept by this file system";
                int flagsValue = 0;
                if (Ioctl(fd, 0x8008_6601 /* FS_IOC_GETFLAGS */, &flagsValue) == 0) text = LsattrText((uint)flagsValue);
                uint generation = 0;
                uint? gen = Ioctl(fd, 0x8008_7601 /* FS_IOC_GETVERSION */, &generation) == 0 ? generation : null;
                byte* xattr = stackalloc byte[28];
                new Span<byte>(xattr, 28).Clear();
                uint? project = Ioctl(fd, 0x801C_581F /* FS_IOC_FSGETXATTR */, xattr) == 0 && *(uint*)(xattr + 12) != 0 ? *(uint*)(xattr + 12) : null;
                return new InodeInfo(text, gen, project, null, IsRegular ? Fiemap(fd) : null);
            }
            finally { Close(fd); }
        }

        /// <summary>FIEMAP: the file's extents, without syncing it first (reading must change nothing).</summary>
        private List<(long, long, long, uint)>? Fiemap(int fd)
        {
            const int Count = 256, Header = 32, ExtentSize = 56;
            var extents = new List<(long, long, long, uint)>();
            var buffer = new byte[Header + Count * ExtentSize];
            ulong start = 0;
            fixed (byte* b = buffer)
            {
                while (extents.Count < 10_000)
                {
                    ct.ThrowIfCancellationRequested();
                    Array.Clear(buffer);
                    *(ulong*)b = start;
                    *(ulong*)(b + 8) = ulong.MaxValue - start;
                    *(uint*)(b + 24) = Count;
                    if (Ioctl(fd, 0xC020_660B /* FS_IOC_FIEMAP */, b) != 0) return extents.Count > 0 ? extents : null;
                    uint mapped = *(uint*)(b + 20);
                    if (mapped == 0) break;
                    bool last = false;
                    for (int i = 0; i < mapped && i < Count; i++)
                    {
                        byte* e = b + Header + i * ExtentSize;
                        long logical = (long)*(ulong*)e, physical = (long)*(ulong*)(e + 8), length = (long)*(ulong*)(e + 16);
                        uint flags = *(uint*)(e + 40);
                        extents.Add((logical, physical, length, flags));
                        last |= (flags & 1) != 0;
                        start = (ulong)(logical + length);
                    }
                    if (last || mapped < Count) break;
                }
            }
            return extents;
        }

        private InspectionSection VolumeSection(MountInfo mount)
        {
            var fields = new List<(string, string)>
            {
                ("Mounted at", mount.MountPoint),
                ("File system", mount.Type),
                ("Source", mount.Source),
                ("Options", mount.Options),
            };
            var children = new List<InspectionSection>();
            // ext4 (and ext3): its journal's statistics, and the file system's own counters.
            string? device = BlockDeviceName(mount.DeviceNumber);
            if (device is not null && mount.Type is "ext4" or "ext3")
            {
                string sys = $"/sys/fs/ext4/{device}";
                string? Value(string name)
                {
                    try { return File.Exists(Path.Combine(sys, name)) ? File.ReadAllText(Path.Combine(sys, name)).Trim() : null; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
                }
                if (Value("lifetime_write_kbytes") is { } written && long.TryParse(written, out long kib)) fields.Add(("Written in its life", RecordText.Short(kib * 1024)));
                if (Value("errors_count") is { } errors) fields.Add(("Errors recorded", errors));
                if (Value("first_error_time") is { } first && first != "0" && long.TryParse(first, out long firstAt))
                    fields.Add(("First error", RecordText.Time(DateTime.UnixEpoch.AddSeconds(firstAt))));
                if (Value("last_error_time") is { } lastError && lastError != "0" && long.TryParse(lastError, out long lastAt))
                    fields.Add(("Last error", RecordText.Time(DateTime.UnixEpoch.AddSeconds(lastAt))));
                string journal = $"/proc/fs/jbd2/{device}-8/info";
                try
                {
                    if (File.Exists(journal))
                        children.Add(new InspectionSection("Journal (jbd2)", []) { Lines = [.. File.ReadAllLines(journal).Where(l => l.Length > 0).Select(l => l.Trim())] });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return new InspectionSection("Volume", fields) { Children = children };
        }

        /// <summary>The kernel's name of a block device ("sda1", "dm-0", "nvme0n1p2") from its major:minor.</summary>
        private static string? BlockDeviceName(string majorMinor)
        {
            try
            {
                var link = new FileInfo($"/sys/dev/block/{majorMinor}").LinkTarget;
                return link is null ? null : Path.GetFileName(link.TrimEnd('/'));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        // ---- macOS ------------------------------------------------------------------------------------------

        private string Mac()
        {
            var st = UnixPermissions.Stat(path) ?? throw new FileNotFoundException($"{path} does not exist.", path);
            _mode = (int)st.Mode | TypeOf(path);
            _uid = st.Uid;
            _gid = st.Gid;
            var attrs = MacAttributes.Read(path, IsFolder, IsRegular) ?? throw new IOException($"{path}: its attributes could not be read.");
            if (attrs.Mode is { } fullMode && (fullMode & TypeMask) != 0) _mode = (int)fullMode;
            var item = new List<(string, string)> { ("Path", path), ("Kind", Kind(_mode, (0, 0))) };
            var mount = UnixFiles.MountOf(path);
            if (mount is not null) item.Add(("Volume", $"{mount.Name} · {mount.DriveFormat}"));
            if (attrs.FileId is { } id) item.Add(("File ID (inode)", id.ToString("N0", CultureInfo.CurrentCulture)));
            if (attrs.ParentId is { } parent) item.Add(("Parent ID", parent.ToString("N0", CultureInfo.CurrentCulture)));
            if (attrs.LinkCount is { } links) item.Add(("Hard links", links.ToString(CultureInfo.CurrentCulture)));
            if ((_mode & TypeMask) == 0xA000 && Target() is { } target) item.Add(("Link to", target));
            if (attrs.Flags is { } bsd) item.Add(("Flags", ChflagsText(bsd)));
            if (attrs.ExtendedFlags is { } ext && ext != 0) item.Add(("APFS", Words(ext, ExtendedFlagNames)));
            if (attrs.CloneId is { } clone && clone != 0) item.Add(("Clone ID", $"0x{clone:X16}"));
            if (attrs.Generation is { } generation && generation != 0) item.Add(("Generation count", generation.ToString(CultureInfo.CurrentCulture)));
            if (attrs.DocumentId is { } document && document != 0) item.Add(("Document ID", document.ToString(CultureInfo.CurrentCulture)));
            if (attrs.EntryCount is { } entries) item.Add(("Entries", entries.ToString("N0", CultureInfo.CurrentCulture)));
            _sections.Add(new InspectionSection("Item", item));
            var times = new Times(attrs.Created, attrs.Modified ?? default, attrs.Changed ?? default, attrs.Accessed ?? default);
            var timeSection = TimesSection(times, "macOS keeps its times to the nanosecond. Programs can set the birth, modification, and access times; the status change time moves to the present whenever anything about the item changes. Added is when it came into its folder (moved, copied, or downloaded there).");
            var extra = new List<(string, string)>(timeSection.Fields);
            if (attrs.Added is { } added) extra.Add(("Added", added.ToString()));
            if (attrs.Backup is { Seconds: not 0 } backup) extra.Add(("Backed up", backup.ToString()));
            _sections.Add(timeSection with { Fields = extra });
            _sections.Add(ChecksSection(times));
            var layout = LayoutSection(attrs.TotalSize ?? 0, attrs.AllocatedSize ?? -1, 0, null);
            var layoutFields = new List<(string, string)>(layout.Fields);
            if (attrs.ResourceLength is > 0) layoutFields.Add(("Resource fork", RecordText.Bytes(attrs.ResourceLength.Value)));
            if (attrs.PrivateSize is { } own && attrs.CloneId is not (null or 0)) layoutFields.Add(("Its own blocks", RecordText.Bytes(own) + " (not shared with its clones)"));
            _sections.Add(layout with { Fields = layoutFields });
            return $"File-system record · {mount?.DriveFormat ?? "macOS"}";
        }

        private static int TypeOf(string path)
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null) return 0xA000;
            return Directory.Exists(path) ? 0x4000 : 0x8000;
        }

        // ---- Shared -----------------------------------------------------------------------------------------

        private string? Target()
        {
            try { return new FileInfo(path).LinkTarget; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        private static InspectionSection TimesSection(Times times, string note)
        {
            var fields = new List<(string, string)>();
            if (times.Birth is { } birth) fields.Add(("Birth", birth.ToString()));
            fields.Add(("Modified", times.Modified.ToString()));
            fields.Add(("Status changed", times.Changed.ToString()));
            fields.Add(("Accessed", times.Accessed.ToString()));
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            string zone = $"UTC{(offset < TimeSpan.Zero ? "−" : "+")}{offset:hh\\:mm}";
            return new InspectionSection("Times", fields) { Lines = RecordText.Wrap($"UTC; this computer is {zone}. {note}") };
        }

        private static InspectionSection ChecksSection(Times times)
        {
            var findings = TimestampChecks.CheckUnix(times.Birth?.Utc, times.Modified.Utc, times.Changed.Utc, times.Accessed.Utc,
                times.Modified.Nanoseconds == 0, times.Changed.Nanoseconds == 0, DateTime.UtcNow);
            var lines = new List<string>();
            foreach (var finding in findings) lines.AddRange(RecordText.Wrap(finding.Text, 110, finding.Strong ? "⚠ " : "• "));
            if (findings.Count == 0) lines.Add("Nothing unusual in what can be compared here.");
            return new InspectionSection("Timestamp checks", []) { Lines = lines };
        }

        private InspectionSection LayoutSection(long size, long allocated, int blockSize, List<(long Logical, long Physical, long Length, uint Flags)>? extents)
        {
            var fields = new List<(string, string)>();
            if (!IsFolder) fields.Add(("Size", RecordText.Bytes(size)));
            if (allocated >= 0) fields.Add(("Allocated", RecordText.Bytes(allocated)));
            if (blockSize > 0) fields.Add(("I/O block size", RecordText.Bytes(blockSize)));
            InspectionTable? table = null;
            if (extents is { Count: > 0 })
            {
                int fragments = 0;
                long end = -1;
                foreach (var e in extents)
                {
                    if (e.Physical != end) fragments++;
                    end = e.Physical + e.Length;
                }
                int shared = extents.Count(e => (e.Flags & 0x2000) != 0);
                fields.Add(("Fragments", $"{fragments:N0} ({extents.Count:N0} extent{(extents.Count == 1 ? "" : "s")})" + (shared > 0 ? $", {shared:N0} shared with other files (reflinks or snapshots)" : "")));
                table = new InspectionTable(["Offset in file", "On the device", "Length", "Flags"], extents.Take(200).Select(e => new[]
                {
                    e.Logical.ToString("N0", CultureInfo.CurrentCulture), e.Physical.ToString("N0", CultureInfo.CurrentCulture),
                    e.Length.ToString("N0", CultureInfo.CurrentCulture), Words(e.Flags & ~1u, ExtentFlagNames),
                }).ToList())
                {
                    More = extents.Count > 200 ? $"{extents.Count - 200:N0} more extents are not listed." : null,
                };
            }
            else if (extents is { Count: 0 } && size > 0) fields.Add(("Extents", "none mapped (its data is inline, or not yet written)"));
            return new InspectionSection("Layout on disk", fields) { Table = table };
        }

        /// <summary>Mode, owner, group, ACLs, labels, and capabilities, with the weak permissions stated.</summary>
        private InspectionSection Permissions()
        {
            var mode = (UnixFileMode)(_mode & 0xFFF);
            char type = (_mode & TypeMask) switch { 0x4000 => 'd', 0xA000 => 'l', 0x1000 => 'p', 0x2000 => 'c', 0x6000 => 'b', 0xC000 => 's', _ => '-' };
            var fields = new List<(string, string)>
            {
                ("Mode", $"{type}{UnixPermissions.Format(mode)} ({UnixPermissions.Octal(mode)})"),
                ("Owner", $"{UnixPermissions.UserName(_uid)} ({_uid})"),
                ("Group", $"{UnixPermissions.GroupName(_gid)} ({_gid})"),
            };
            var children = new List<InspectionSection>();
            var findings = new List<RecordFinding>();
            var hidden = new UnixHiddenData();
            IReadOnlyList<HiddenItem> attributes = [];
            try { attributes = hidden.List(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            byte[]? Value(string name) => attributes.FirstOrDefault(a => a.Name == name) is { } a ? Try(() => hidden.Read(path, a, 64 * 1024)) : null;
            if (OperatingSystem.IsLinux())
            {
                var rows = new List<string[]>();
                if (Value("system.posix_acl_access") is { } access) rows.AddRange(PosixAclRows(access, "this item"));
                if (IsFolder && Value("system.posix_acl_default") is { } defaults) rows.AddRange(PosixAclRows(defaults, "new items in it"));
                if (rows.Count > 0) children.Add(new InspectionSection("ACL (POSIX)", []) { Table = new InspectionTable(["Entry", "Who", "Rights", "For"], rows) });
                if (Value("security.selinux") is { } label) fields.Add(("SELinux", Encoding.UTF8.GetString(label).TrimEnd('\0')));
                if (Value("security.apparmor") is { } profile) fields.Add(("AppArmor", Encoding.UTF8.GetString(profile).TrimEnd('\0')));
                if (Value("security.capability") is { } caps && HiddenDataDecoder.Capabilities(caps) is { } decoded)
                {
                    fields.Add(("Capabilities", decoded.Item1["Capabilities: ".Length..]));
                    findings.Add(new(false, $"It runs with capabilities ({decoded.Item1["Capabilities: ".Length..]}) for whoever starts it."));
                }
            }
            else if (MacAcl(path) is { Count: > 0 } acl)
            {
                children.Add(new InspectionSection("ACL", []) { Table = new InspectionTable(["Type", "Who", "Rights", "Flags"], acl.Select(e => new[] { e.Type, e.Who, e.Rights, e.Flags }).ToList()) });
                foreach (var e in acl.Where(e => e.Type == "allow" && e.Who is "everyone" or "group:everyone" && e.Rights.Contains("write", StringComparison.Ordinal)))
                    findings.Add(new(true, $"Everyone may {e.Rights} (an ACL entry)."));
            }
            Weak(findings, mode);
            var lines = new List<string>();
            foreach (var finding in findings)
            {
                lines.AddRange(RecordText.Wrap(finding.Text, 110, finding.Strong ? "⚠ " : "• "));
                if (finding.Strong) _warnings.Add("Permissions: " + finding.Text);
            }
            return new InspectionSection("Security", fields) { Lines = lines, Children = children };
        }

        /// <summary>World-writable items, and programs that run with their owner's or group's rights.</summary>
        private void Weak(List<RecordFinding> findings, UnixFileMode mode)
        {
            bool otherWrite = (mode & UnixFileMode.OtherWrite) != 0;
            if (IsFolder && otherWrite)
                findings.Add((mode & UnixFileMode.StickyBit) != 0
                    ? new(false, "Anyone on this computer can add items to it; only their owners can delete or rename them (sticky bit).")
                    : new(true, "Anyone on this computer can add, delete, and rename items in it: it is world-writable without the sticky bit."));
            else if (IsRegular && otherWrite)
                findings.Add(new(true, "Anyone on this computer can change its content: it is world-writable."));
            else if ((_mode & TypeMask) == 0x6000 && (mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
                findings.Add(new(true, $"Anyone on this computer can {((mode & UnixFileMode.OtherWrite) != 0 ? "read and write" : "read")} this whole device, past every file system on it."));
            else if ((_mode & TypeMask) is 0x1000 or 0xC000 && otherWrite)
                findings.Add(new(false, "Anyone on this computer can write to it."));
            if (IsRegular && (mode & UnixFileMode.SetUser) != 0)
            {
                bool othersWrite = (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
                string runs = $"It runs as {UnixPermissions.UserName(_uid)} for whoever starts it (set-user-ID)";
                findings.Add(new RecordFinding(othersWrite, runs + (othersWrite ? ", and others can change it." : ".")));
            }
            if (IsRegular && (mode & UnixFileMode.SetGroup) != 0)
                findings.Add(new(false, $"It runs with the group {UnixPermissions.GroupName(_gid)} for whoever starts it (set-group-ID)."));
        }

        private static T? Try<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>A POSIX ACL (system.posix_acl_*: version 2, then tag, permissions, and ID per entry) as table rows.</summary>
        private static IEnumerable<string[]> PosixAclRows(byte[] value, string target)
        {
            if (value.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(value) != 2) yield break;
            for (int at = 4; at + 8 <= value.Length; at += 8)
            {
                ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(at));
                ushort perm = BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(at + 2));
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(at + 4));
                var (entry, who) = tag switch
                {
                    0x01 => ("user", "its owner"),
                    0x02 => ("user", UnixPermissions.UserName(id)),
                    0x04 => ("group", "its group"),
                    0x08 => ("group", UnixPermissions.GroupName(id)),
                    0x10 => ("mask", "most any named entry or group gets"),
                    0x20 => ("other", "everyone else"),
                    _ => ($"tag {tag}", id.ToString(CultureInfo.InvariantCulture)),
                };
                var rights = new List<string>();
                if ((perm & 4) != 0) rights.Add("read");
                if ((perm & 2) != 0) rights.Add("write");
                if ((perm & 1) != 0) rights.Add("execute");
                yield return [entry, who, rights.Count > 0 ? string.Join(", ", rights) : "none", target];
            }
        }
    }

    /// <summary>A Unix time to the nanosecond.</summary>
    private readonly record struct UnixTime(long Seconds, uint Nanoseconds)
    {
        public DateTime? Utc => Seconds is > -62_135_596_800 and < 253_402_300_799
            ? DateTime.UnixEpoch.AddSeconds(Seconds).AddTicks(Nanoseconds / 100) : null;

        public override string ToString() => Utc is { } utc
            ? utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "." + Nanoseconds.ToString("D9", CultureInfo.InvariantCulture) + " UTC"
            : $"invalid ({Seconds} s)";
    }

    private static string Kind(int mode, (uint Major, uint Minor) device) => (mode & TypeMask) switch
    {
        0x8000 => "file",
        0x4000 => "folder",
        0xA000 => "symbolic link",
        0x1000 => "FIFO (named pipe)",
        0xC000 => "socket",
        0x2000 => $"character device {device.Major}:{device.Minor}",
        0x6000 => $"block device {device.Major}:{device.Minor}",
        _ => $"type 0x{mode & TypeMask:X}",
    };

    private static readonly (ulong Flag, string Text)[] StatxAttributes =
    [
        (0x4, "compressed"), (0x10, "immutable"), (0x20, "append only"), (0x40, "not dumped"), (0x800, "encrypted"),
        (0x1000, "automount trigger"), (0x2000, "mount root"), (0x10_0000, "fs-verity protected"), (0x20_0000, "DAX"), (0x40_0000, "atomic writes"),
    ];

    private static readonly (ulong Flag, string Text)[] ExtentFlagNames =
    [
        (0x2, "location unknown"), (0x4, "delayed allocation"), (0x8, "encoded"), (0x80, "encrypted"), (0x100, "not aligned"),
        (0x200, "inline"), (0x400, "tail packed"), (0x800, "unwritten (preallocated)"), (0x1000, "merged"), (0x2000, "shared"),
    ];

    private static readonly (ulong Flag, string Text)[] ExtendedFlagNames =
    [
        (0x1, "may share blocks (a clone)"), (0x2, "no extended attributes"), (0x4, "sync root"), (0x8, "purgeable"),
        (0x10, "sparse"), (0x20, "synthetic"), (0x40, "shares all its blocks"),
    ];

    private static string Words(ulong value, (ulong Flag, string Text)[] names)
    {
        var parts = names.Where(n => (value & n.Flag) != 0).Select(n => n.Text).ToList();
        ulong known = names.Aggregate(0UL, (all, n) => all | n.Flag);
        if ((value & ~known) != 0) parts.Add($"0x{value & ~known:X}");
        return string.Join(", ", parts);
    }

    /// <summary>The flags as lsattr prints them ("----i---------e-----"), then in words.</summary>
    internal static string LsattrText(uint flags)
    {
        (uint Flag, char Letter, string Word)[] all =
        [
            (0x1, 's', "secure deletion"), (0x2, 'u', "undeletable"), (0x8, 'S', "synchronous updates"), (0x10000, 'D', "synchronous folder updates"),
            (0x10, 'i', "immutable"), (0x20, 'a', "append only"), (0x40, 'd', "no dump"), (0x80, 'A', "no access time"),
            (0x4, 'c', "compressed"), (0x800, 'E', "encrypted"), (0x4000, 'j', "data journaling"), (0x1000, 'I', "indexed folder"),
            (0x8000, 't', "no tail merging"), (0x20000, 'T', "top of a hierarchy"), (0x80000, 'e', "extents"), (0x800000, 'C', "no copy on write"),
            (0x2000000, 'x', "DAX"), (0x40000000, 'F', "case-insensitive"), (0x10000000, 'N', "inline data"), (0x20000000, 'P', "project inherit"),
            (0x100000, 'V', "fs-verity"), (0x400, 'm', "no compression"),
        ];
        string letters = new(all.Select(a => (flags & a.Flag) != 0 ? a.Letter : '-').ToArray());
        var words = all.Where(a => (flags & a.Flag) != 0).Select(a => a.Word).ToList();
        return words.Count > 0 ? $"{letters} · {string.Join(", ", words)}" : letters + " · none";
    }

    /// <summary>BSD file flags as chflags names them, then in words.</summary>
    internal static string ChflagsText(uint flags)
    {
        (uint Flag, string Name, string Word)[] all =
        [
            (0x1, "nodump", "not dumped"), (0x2, "uchg", "immutable"), (0x4, "uappnd", "append only"), (0x8, "opaque", "opaque in a union mount"),
            (0x20, "compressed", "compressed by the file system"), (0x40, "tracked", "tracked for document IDs"), (0x80, "datavault", "data vault"),
            (0x8000, "hidden", "hidden in the Finder"), (0x10000, "arch", "archived"), (0x20000, "schg", "system immutable"),
            (0x40000, "sappnd", "system append only"), (0x80000, "restricted", "protected by System Integrity Protection"),
            (0x100000, "sunlnk", "cannot be deleted"), (0x800000, "firmlink", "a firmlink"), (0x40000000, "dataless", "dataless (its content is in the cloud)"),
        ];
        var set = all.Where(a => (flags & a.Flag) != 0).ToList();
        uint known = all.Aggregate(0u, (acc, a) => acc | a.Flag);
        string extra = (flags & ~known) != 0 ? $", 0x{flags & ~known:X}" : "";
        return set.Count == 0 && extra.Length == 0 ? "none" : string.Join(",", set.Select(a => a.Name)) + " · " + string.Join(", ", set.Select(a => a.Word)) + extra;
    }

    /// <summary>
    /// getattrlist's answer for one item (macOS): the values come packed in bit order (common, directory, file, then the
    /// extended common ones), each 4-byte aligned; FSOPT_PACK_INVAL_ATTRS keeps a slot for every one asked, and the
    /// returned set says which are valid. A value is null when the file system does not keep it.
    /// </summary>
    private sealed class MacAttributes
    {
        public UnixTime? Created, Modified, Changed, Accessed, Backup, Added;
        public uint? Flags, Generation, DocumentId, LinkCount, EntryCount, Mode;
        public ulong? FileId, ParentId, CloneId, ExtendedFlags;
        public long? TotalSize, AllocatedSize, ResourceLength, PrivateSize;

        /// <summary>With the extended attributes (clones, private size) first, then without them for systems that lack them.</summary>
        public static MacAttributes? Read(string path, bool folder, bool file) => Query(path, folder, file, extended: true) ?? Query(path, folder, file, extended: false);

        private static MacAttributes? Query(string path, bool folder, bool file, bool extended)
        {
            const uint Returned = 0x8000_0000, ObjType = 0x8, Created = 0x200, Modified = 0x400, Changed = 0x800, Accessed = 0x1000, Backup = 0x2000;
            const uint Owner = 0x8000, Group = 0x10000, AccessMask = 0x20000, Flags = 0x40000, Generation = 0x80000, Document = 0x100000;
            const uint FileId = 0x200_0000, ParentId = 0x400_0000, Added = 0x1000_0000;
            const uint EntryCount = 0x2, LinkCount = 0x1, TotalSize = 0x2, AllocSize = 0x4, ResourceLength = 0x1000;
            const uint PrivateSize = 0x8, LinkId = 0x10, CloneId = 0x100, ExtFlags = 0x200;
            uint common = Returned | ObjType | Created | Modified | Changed | Accessed | Backup | Owner | Group | AccessMask | Flags | Generation | Document | FileId | ParentId | Added;
            uint dir = folder ? EntryCount : 0;
            uint fileSet = file ? LinkCount | TotalSize | AllocSize | ResourceLength : 0;
            uint ext = extended ? PrivateSize | LinkId | CloneId | ExtFlags : 0;
            byte* list = stackalloc byte[24];
            *(ushort*)list = 5; // ATTR_BIT_MAP_COUNT
            *(ushort*)(list + 2) = 0;
            *(uint*)(list + 4) = common;
            *(uint*)(list + 8) = 0;
            *(uint*)(list + 12) = dir;
            *(uint*)(list + 16) = fileSet;
            *(uint*)(list + 20) = ext;
            var buffer = new byte[1024];
            ulong options = 0x1 /* FSOPT_NOFOLLOW */ | 0x8 /* FSOPT_PACK_INVAL_ATTRS */ | (extended ? 0x20UL /* FSOPT_ATTR_CMN_EXTENDED */ : 0);
            int result;
            try
            {
                fixed (byte* b = buffer) result = GetAttrList(path, list, b, (nuint)buffer.Length, options);
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return null; }
            if (result != 0) return null;
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(buffer), (uint)buffer.Length);
            int at = 4;
            uint U32()
            {
                uint v = at + 4 <= length ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(at)) : 0;
                at += 4;
                return v;
            }
            ulong U64()
            {
                ulong v = at + 8 <= length ? BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at)) : 0;
                at += 8;
                return v;
            }
            UnixTime Time()
            {
                long seconds = (long)U64();
                return new UnixTime(seconds, (uint)U64());
            }
            uint rCommon = U32();
            U32(); // volume
            uint rDir = U32(), rFile = U32(), rExt = U32();
            var a = new MacAttributes();
            U32(); // object type
            var created = Time();
            var modified = Time();
            var changed = Time();
            var accessed = Time();
            var backup = Time();
            U32(); // owner (read with stat)
            U32(); // group
            uint mode = U32(), flags = U32(), generation = U32(), document = U32();
            ulong fileId = U64(), parentId = U64();
            var added = Time();
            if ((rCommon & Created) != 0) a.Created = created;
            if ((rCommon & Modified) != 0) a.Modified = modified;
            if ((rCommon & Changed) != 0) a.Changed = changed;
            if ((rCommon & Accessed) != 0) a.Accessed = accessed;
            if ((rCommon & Backup) != 0) a.Backup = backup;
            if ((rCommon & AccessMask) != 0) a.Mode = mode;
            if ((rCommon & Flags) != 0) a.Flags = flags;
            if ((rCommon & Generation) != 0) a.Generation = generation;
            if ((rCommon & Document) != 0) a.DocumentId = document;
            if ((rCommon & FileId) != 0) a.FileId = fileId;
            if ((rCommon & ParentId) != 0) a.ParentId = parentId;
            if ((rCommon & Added) != 0) a.Added = added;
            if (folder)
            {
                uint entries = U32();
                if ((rDir & EntryCount) != 0) a.EntryCount = entries;
            }
            if (file)
            {
                uint links = U32();
                long total = (long)U64(), allocated = (long)U64(), resource = (long)U64();
                if ((rFile & LinkCount) != 0) a.LinkCount = links;
                if ((rFile & TotalSize) != 0) a.TotalSize = total;
                if ((rFile & AllocSize) != 0) a.AllocatedSize = allocated;
                if ((rFile & ResourceLength) != 0) a.ResourceLength = resource;
            }
            if (extended)
            {
                long own = (long)U64();
                U64(); // link ID
                ulong clone = U64(), extFlags = U64();
                if ((rExt & PrivateSize) != 0) a.PrivateSize = own;
                if ((rExt & CloneId) != 0) a.CloneId = clone;
                if ((rExt & ExtFlags) != 0) a.ExtendedFlags = extFlags;
            }
            return a;
        }
    }

    [LibraryImport("libc", EntryPoint = "getattrlist", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int GetAttrList(string path, void* list, void* buffer, nuint size, ulong options);

    private sealed record MacAclEntry(string Type, string Who, string Rights, string Flags);

    /// <summary>The macOS ACL as acl_to_text writes it: "user|group:UUID:name:id:allow|deny[,flags]:rights".</summary>
    private static List<MacAclEntry>? MacAcl(string path)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            nint acl = AclGetLink(path, 0x100 /* ACL_TYPE_EXTENDED */);
            if (acl == 0) return null;
            try
            {
                nint text = AclToText(acl, out _);
                if (text == 0) return null;
                try
                {
                    var entries = new List<MacAclEntry>();
                    foreach (string line in (Marshal.PtrToStringUTF8(text) ?? "").Split('\n'))
                    {
                        var f = line.Split(':');
                        if (f.Length < 6 || f[0] is not ("user" or "group")) continue;
                        var kind = f[4].Split(',');
                        string who = f[2].Length > 0 ? (f[0] == "group" ? "group:" : "") + f[2] : f[1];
                        entries.Add(new MacAclEntry(kind[0], who == "group:everyone" ? "everyone" : who, f[5].Replace(",", ", ", StringComparison.Ordinal), string.Join(", ", kind.Skip(1))));
                    }
                    return entries;
                }
                finally { AclFree(text); }
            }
            finally { AclFree(acl); }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return null; }
    }

    private static Exception Error(string path)
    {
        int errno = Marshal.GetLastPInvokeError();
        string message = Marshal.GetPInvokeErrorMessage(errno);
        return errno switch
        {
            2 or 20 => new FileNotFoundException($"{path} does not exist.", path),
            1 or 13 => new UnauthorizedAccessException($"{path}: {message}"),
            _ => new IOException($"{path}: {message}", errno),
        };
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int directory, string path, int flags, uint mask, byte* buffer);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "pread", SetLastError = true)]
    private static partial nint PRead(int fd, byte* buffer, nuint count, long offset);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, ulong request, void* argument);

    [LibraryImport("libc", EntryPoint = "acl_get_link_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint AclGetLink(string path, int type);

    [LibraryImport("libc", EntryPoint = "acl_to_text")]
    private static partial nint AclToText(nint acl, out nint length);

    [LibraryImport("libc", EntryPoint = "acl_free")]
    private static partial int AclFree(nint item);
}
