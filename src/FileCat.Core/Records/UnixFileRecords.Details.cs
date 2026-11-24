using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const int StoredLinkLimit = 16 * 1024;
    private static readonly UTF8Encoding StoredLinkEncoding = new(false, true);

    public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct) => ReadDetails(path, fullReport: false, ct).Summary;

    public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct)
    {
        if (policy != FileDetailsPolicy.MetadataOnly) throw new NotSupportedException("This details route supports metadata only.");
        return ReadDetails(path, fullReport: true, ct);
    }

    private FileDetailsReport ReadDetails(string path, bool fullReport, CancellationToken ct)
    {
        if (!IsSupported) throw new PlatformNotSupportedException();
        FileSystem.PathUtil.RefuseAmbiguousUnixName(path);
        ct.ThrowIfCancellationRequested();
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsLinux() ? LinuxDetails(full, fullReport, ct)
            : MacDetailsWithoutMaterialization(full, ct, () => MacDetails(full, fullReport, ct));
    }

    private static FileDetailsReport LinuxDetails(string path, bool fullReport, CancellationToken ct)
    {
        int noFollow = LinuxOpenFlags.Current.NoFollow;
        ct.ThrowIfCancellationRequested();
        // O_PATH pins even symlinks/special nodes without a data open or device/FIFO activation.
        int fd = Open(path, 0x200000 | 0x80000 | noFollow);
        if (fd < 0) throw Error(path);
        try
        {
            ct.ThrowIfCancellationRequested();
            byte* bytes = stackalloc byte[256];
            new Span<byte>(bytes, 256).Clear();
            int status = Statx(fd, "", 0x1000 | 0x100 | 0x800, 0xFFF | 0x1000, bytes);
            ct.ThrowIfCancellationRequested();
            if (status != 0) throw Error(path);
            var fields = new ReadOnlySpan<byte>(bytes, 256);
            var report = ParseLinuxDetails(path, fields);
            var fileSystem = ReadLinuxDetailsFileSystem(fd, fields, noFollow, fullReport, ct, out var zfsMount);
            string? format = fileSystem.State == FactState.Known ? fileSystem.Value : null;
            bool ext = format is "ext2" or "ext3" or "ext4";
            report = report with { Summary = report.Summary with { FileSystem = fileSystem } };
            if (ext)
            {
                var snapshot = report.Summary;
                // Ext-family statx reflects the historical c flag even though the filesystem implements no file compression.
                var compression = ObservedFact<bool>.Unavailable(FactState.Unsupported, FactScope.Entry, snapshot.Compression.Source,
                    "Recorded flags are separate. File compression behavior is not implemented by this filesystem.");
                report = report with { Summary = snapshot with { Compression = compression, CompressionLabel = "File compression" } };
            }
            if (report.Summary.Kind == FileObjectKind.SymbolicLink)
            {
                byte[] buffer = new byte[StoredLinkLimit];
                ct.ThrowIfCancellationRequested();
                nint count;
                fixed (byte* data = buffer) count = ReadLinkAt(fd, "", data, (nuint)buffer.Length);
                int error = count < 0 ? Marshal.GetLastPInvokeError() : 0;
                ct.ThrowIfCancellationRequested();
                var target = DecodeStoredLinkTarget(buffer, count, error, "readlinkat on the held O_PATH descriptor");
                report = report with { Summary = report.Summary with { Target = target } };
            }
            if (report.Summary is { Kind: FileObjectKind.File or FileObjectKind.Directory,
                    Identity: { State: FactState.Known, Value: { } identity } } summary)
            {
                bool x64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
                // The inode-flag, case-folding and Btrfs queries use the generic ioctl encodings and layouts, qualified with
                // native fixtures on x64 and ARM64. fscrypt, file capabilities and ZFS are qualified on x64 only.
                bool flagQueries = x64 || RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
                bool extInodeFlags = flagQueries && ext;
                bool btrfs = fullReport && flagQueries && format == "btrfs";
                bool fileExtents = fullReport && summary.Kind == FileObjectKind.File;
                if (fullReport && x64 && zfsMount is not null)
                    report = AddLinuxZfsDataset(report, fd, zfsMount, ct);
                if (extInodeFlags || btrfs || fileExtents)
                {
                    var (file, state, reason) = OpenLinuxMetadataDescriptor(fd, identity, summary.Kind, ct);
                    using var descriptor = file;
                    if (extInodeFlags)
                    {
                        var flags = ReadLinuxExtInodeFlags(file, state, reason, format!, ct);
                        report = AddLinuxExtInodeFlags(report, flags, format!, fullReport);
                        if (fullReport && format == "ext4")
                        {
                            if (x64)
                                report = AddLinuxFscrypt(report, file, state, reason, ct);
                            if (summary.Kind == FileObjectKind.Directory)
                                report = AddExt4DirectoryNamespace(report, flags);
                        }
                    }
                    if (btrfs)
                        report = AddLinuxBtrfs(report, file, state, reason, ct);
                    if (fileExtents)
                    {
                        if (x64 && format == "ext4")
                            report = AddLinuxFileCapabilities(report, file, state, reason, ct);
                        report = AddLinuxExtents(report, file is null ? FiemapUnavailable(state, reason!) : ReadLinuxExtents(file, ct));
                    }
                }
            }
            return report;
        }
        finally { Close(fd); }
    }

    private static ObservedFact<string> ReadLinuxDetailsFileSystem(int held, ReadOnlySpan<byte> statx, int noFollow, bool fullReport,
        CancellationToken ct, out LinuxZfsMount? zfsMount)
    {
        zfsMount = null;
        const string source = "statx on O_PATH descriptor + /proc/self/mountinfo";
        ObservedFact<string> Unavailable(FactState state, string reason) => ObservedFact<string>.Unavailable(state, FactScope.Volume, source, reason);
        ulong mountId = BinaryPrimitives.ReadUInt64LittleEndian(statx[144..]);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(statx) & 0x1000) == 0 || mountId == 0)
            return Unavailable(FactState.Unsupported, "statx did not return a mount ID.");
        byte[] id = Encoding.ASCII.GetBytes(mountId.ToString(CultureInfo.InvariantCulture));
        string device = string.Create(CultureInfo.InvariantCulture,
            $"{BinaryPrimitives.ReadUInt32LittleEndian(statx[136..])}:{BinaryPrimitives.ReadUInt32LittleEndian(statx[140..])}");
        try
        {
            ct.ThrowIfCancellationRequested();
            const string path = "/proc/self/mountinfo";
            int fd = Open(path, 0x80000 | 0x800 | 0x100 | noFollow); // O_CLOEXEC | O_NONBLOCK | O_NOCTTY | O_NOFOLLOW
            if (fd < 0) throw Error(path);
            using var handle = new SafeFileHandle((nint)fd, ownsHandle: true);
            ct.ThrowIfCancellationRequested();
            byte* filesystem = stackalloc byte[256];
            bool procfs = DetailsFstatfs(fd, filesystem) == 0 && *(nint*)filesystem == 0x9fa0; // PROC_SUPER_MAGIC
            ct.ThrowIfCancellationRequested();
            if (!procfs) return Unavailable(FactState.Unsupported, "The process mount table is unavailable on procfs.");
            using var input = new FileStream(handle, FileAccess.Read, bufferSize: 1);
            const int limit = 1 << 20;
            byte[] data = new byte[64 << 10];
            int count = 0;
            while (count <= limit)
            {
                ct.ThrowIfCancellationRequested();
                if (count == data.Length) Array.Resize(ref data, Math.Min(data.Length * 4, limit + 1));
                int read = input.Read(data, count, Math.Min(64 << 10, data.Length - count));
                if (read == 0) break;
                count += read;
            }
            ct.ThrowIfCancellationRequested();
            if (count > limit) return Unavailable(FactState.Partial, "The process mount table exceeds the 1 MiB read limit.");
            string? type = null;
            LinuxZfsMount? matchedZfsMount = null;
            bool btrfsSubvolume = false;
            ReadOnlySpan<byte> table = data.AsSpan(0, count);
            foreach (var range in table.Split((byte)'\n'))
            {
                ct.ThrowIfCancellationRequested();
                var row = table[range];
                int firstSpace = row.IndexOf((byte)' ');
                if (firstSpace < 0 || !row[..firstSpace].SequenceEqual(id)) continue;
                string[] columns = Encoding.UTF8.GetString(row).Split(' ');
                int separator = Array.IndexOf(columns, "-");
                if (type is not null || separator < 6 || separator + 4 != columns.Length ||
                    string.IsNullOrEmpty(columns[separator + 1]))
                    return Unavailable(FactState.Changed, "The mount record did not uniquely match the held item's device.");
                type = columns[separator + 1];
                if (fullReport && type == "zfs") matchedZfsMount = ParseLinuxZfsMount(columns[separator + 2], columns[separator + 3]);
                if (columns[2] == device) continue;
                // Btrfs can report an anonymous device number for a subvolume within the same mount ID.
                // Independently verify the filesystem on the held object before qualifying that case.
                if (type != "btrfs" || !IsHeldBtrfs(held, ct))
                    return Unavailable(FactState.Changed, "The mount record did not match the held item's device.");
                btrfsSubvolume = true;
            }
            zfsMount = matchedZfsMount;
            return type is null ? Unavailable(FactState.Partial, "The held item's mount is not visible in the process mount table.")
                : ObservedFact<string>.Known(type, FactScope.Volume, btrfsSubvolume ? source + " + fstatfs on the held subvolume" : source);
        }
        catch (UnauthorizedAccessException) { return Unavailable(FactState.Denied, "The process mount table is unavailable to this user."); }
        catch (IOException) { return Unavailable(FactState.Failed, "The process mount table could not be read."); }
    }

    internal static FileDetailsReport ParseLinuxDetails(string path, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 256) throw new InvalidDataException("Truncated statx result.");
        uint mask = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        uint mode = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]);
        ulong attrs = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]), supported = BinaryPrimitives.ReadUInt64LittleEndian(bytes[56..]);
        const string Source = "statx on O_PATH descriptor";
        ObservedFact<T> Field<T>(uint bit, T value, FactScope scope) => (mask & bit) != 0
            ? ObservedFact<T>.Known(value, scope, Source)
            : ObservedFact<T>.Unavailable(FactState.Unsupported, scope, Source, "The returned statx mask does not include this field.");
        ObservedFact<bool> Flag(ulong bit) => (supported & bit) != 0
            ? ObservedFact<bool>.Known((attrs & bit) != 0, FactScope.Entry, Source + " · recorded attribute flag; behavior not qualified")
            : ObservedFact<bool>.Unavailable(FactState.Unsupported, FactScope.Entry, Source, "The returned attribute mask does not support this flag.");
        ulong rawSize = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]), blocks = BinaryPrimitives.ReadUInt64LittleEndian(bytes[48..]);
        FileObjectKind kind = (mask & 1) == 0 ? FileObjectKind.Unknown : KindFromMode(mode);
        var snapshot = new FileDetailsSnapshot(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = kind,
            Identity = Field(0x100, LinuxIdentity(bytes), FactScope.Entry),
            LogicalSize = (mask & 0x200) == 0 ? Field(0x200, 0L, FactScope.Entry)
                : rawSize <= long.MaxValue ? Field(0x200, (long)rawSize, FactScope.Entry)
                : ObservedFact<long>.Unavailable(FactState.Failed, FactScope.Entry, Source, "Size overflow."),
            Allocated = (mask & 0x400) == 0 ? Field(0x400, 0L, FactScope.Entry)
                : blocks <= long.MaxValue / 512 ? Field(0x400, (long)blocks * 512, FactScope.Entry)
                : ObservedFact<long>.Unavailable(FactState.Failed, FactScope.Entry, Source, "Allocation overflow."),
            LinkCount = Field(4, BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]), FactScope.Entry),
            NativeAttributes = ObservedFact<ulong>.Known(attrs & supported, FactScope.Entry, Source + " · supported mask only"),
            UnixMode = Field(2, mode & 0xFFF, FactScope.Entry),
            Owner = Field(8, BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]), FactScope.Entry),
            Group = Field(0x10, BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]), FactScope.Entry),
            Compression = Flag(4), FileEncryption = Flag(0x800),
            Restrictions = LinuxRestrictionFlags(attrs, supported, kind, Source),
            Notes = ["Allocation is inode-reported blocks × 512, not exclusive storage or recursive contents.",
                $"statx field mask: 0x{mask:X}; attribute mask: 0x{supported:X}; raw attributes: 0x{attrs:X}.",
                "Additional filesystem policies, xattrs and effective access were not queried."]
        };
        return FileDetailsReport.Metadata(snapshot, new InspectionSection("Native statx metadata", [("Returned mask", $"0x{mask:X}"), ("Attribute support mask", $"0x{supported:X}"), ("Raw attributes", $"0x{attrs:X}")]));
    }

    private static ObservedFact<string> LinuxRestrictionFlags(ulong attributes, ulong supported, FileObjectKind kind, string source)
    {
        const ulong immutable = 0x10, append = 0x20, noDump = 0x40, verity = 0x10_0000;
        ulong applicable = immutable | append | noDump;
        if (kind == FileObjectKind.File) applicable |= verity;
        ulong returned = supported & applicable;
        if (returned == 0)
            return ObservedFact<string>.Unavailable(FactState.Unsupported, FactScope.Entry, source,
                "The returned statx attribute mask includes no supported restriction flag for this item.");

        ulong flags = attributes & returned;
        var descriptions = new List<string>();
        if ((flags & immutable) != 0) descriptions.Add("Immutable flag set.");
        if ((flags & append) != 0) descriptions.Add("Append-only flag set.");
        if ((flags & noDump) != 0) descriptions.Add("No-dump flag set. Backup tools may skip this item.");
        if ((flags & verity) != 0)
            descriptions.Add("fs-verity enabled. File data is read-only and checked against its integrity tree on read. Signature trust requires separate verification.");
        return ObservedFact<string>.Known(descriptions.Count == 0 ? "No returned restriction flag is set."
            : string.Join(" ", descriptions), FactScope.Entry, source);
    }

    private static DetailsIdentity LinuxIdentity(ReadOnlySpan<byte> bytes) => new(
        $"{BinaryPrimitives.ReadUInt32LittleEndian(bytes[136..])}:{BinaryPrimitives.ReadUInt32LittleEndian(bytes[140..])}",
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]).ToString());

    internal static FileObjectKind KindFromMode(uint mode) => (mode & TypeMask) switch
    {
        0x8000 => FileObjectKind.File, 0x4000 => FileObjectKind.Directory, 0xA000 => FileObjectKind.SymbolicLink,
        0x1000 => FileObjectKind.Fifo, 0xC000 => FileObjectKind.Socket, 0x2000 => FileObjectKind.CharacterDevice,
        0x6000 => FileObjectKind.BlockDevice, _ => FileObjectKind.Unknown,
    };

    private static FileDetailsReport MacDetails(string path, bool fullReport, CancellationToken ct)
    {
        byte[] fields = ReadMacDetailsFields(path, ct);
        var report = ParseMacDetails(path, fields);
        if (report.Summary.NativeAttributes is { State: FactState.Known } attributes &&
            (attributes.Value & MacMaterializationPolicy.DatalessFlag) != 0)
            return report with { Summary = report.Summary with
            {
                Notes = report.Summary.Notes.Add("This dataless item shows cached metadata. Downloads were disabled for this inspection."),
            } };
        if (report.Summary.Kind == FileObjectKind.SymbolicLink)
        {
            var target = ReadMacStoredTarget(path, report.Summary.Identity, fields, ct);
            report = report with { Summary = report.Summary with { Target = target } };
        }
        if (fullReport && report.Summary is
            {
                Kind: FileObjectKind.File or FileObjectKind.Directory or FileObjectKind.SymbolicLink,
                Identity.State: FactState.Known,
                NativeAttributes.State: FactState.Known,
            })
        {
            report = AddMacVolumeDetails(report, fields, ct);
        }
        return report;
    }

    internal static FileDetailsReport MacDetailsWithoutMaterialization(string path, CancellationToken ct, Func<FileDetailsReport> read) =>
        MacMaterializationPolicy.Run(ct, read, (failure, reason) => FileDetailsReport.Metadata(new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Identity = ObservedFact<DetailsIdentity>.Unavailable(failure switch
            {
                MacMaterializationPolicy.Failure.WouldMaterialize => FactState.NotInspected,
                MacMaterializationPolicy.Failure.RestoreFailed => FactState.Failed,
                _ => FactState.Unsupported,
            }, FactScope.Entry, "macOS metadata with materialization disabled", reason),
            Notes = [reason],
        }));

    private static byte[] ReadMacDetailsFields(string path, CancellationToken ct, bool apfsSharing = false)
    {
        // A separate summary request: no entry count, resource fork, clone statistics, ACL or xattr values.
        const uint Common = 0x80000000 | 0x2 | 0x8 | 0x200 | 0x400 | 0x800 | 0x1000 | 0x8000 | 0x10000 | 0x20000 | 0x40000 | 0x2000000;
        byte* list = stackalloc byte[24];
        new Span<byte>(list, 24).Clear();
        *(ushort*)list = 5;
        *(uint*)(list + 4) = Common;
        *(uint*)(list + 16) = 7; // link count, total size and allocation (all forks); returned mask determines validity
        if (apfsSharing) *(uint*)(list + 20) = 0x308; // ATTR_CMNEXT_PRIVATESIZE | ATTR_CMNEXT_CLONEID | ATTR_CMNEXT_EXT_FLAGS
        byte[] buffer = new byte[256];
        ct.ThrowIfCancellationRequested();
        int status;
        ulong options = 1 | 8 | (apfsSharing ? 0x20UL /* FSOPT_ATTR_CMN_EXTENDED */ : 0);
        fixed (byte* data = buffer) status = GetAttrList(path, list, data, (nuint)buffer.Length, options);
        ct.ThrowIfCancellationRequested();
        if (status != 0) throw Error(path);
        return buffer;
    }

    private static ObservedFact<FileSystem.StoredLinkTarget> ReadMacStoredTarget(string path, ObservedFact<DetailsIdentity> identity,
        ReadOnlySpan<byte> before, CancellationToken ct)
    {
        const string source = "Darwin readlink with before-and-after identity and timestamp samples";
        ObservedFact<FileSystem.StoredLinkTarget> Unavailable(FactState state, string reason) =>
            ObservedFact<FileSystem.StoredLinkTarget>.Unavailable(state, FactScope.Entry, source, reason);
        if (identity.State != FactState.Known || !HasMacIdentityTimestamps(before))
            return Unavailable(FactState.Partial, "The first link sample lacks identity or timestamp fields.");

        byte[] buffer = new byte[StoredLinkLimit];
        ct.ThrowIfCancellationRequested();
        nint count;
        fixed (byte* data = buffer) count = MacReadLink(path, data, (nuint)buffer.Length);
        int error = count < 0 ? Marshal.GetLastPInvokeError() : 0;
        ct.ThrowIfCancellationRequested();
        var target = DecodeStoredLinkTarget(buffer, count, error, source);
        if (target.State != FactState.Known) return target;

        try
        {
            byte[] after = ReadMacDetailsFields(path, ct);
            var current = ParseMacDetails(path, after).Summary;
            if (current.Kind is not (FileObjectKind.SymbolicLink or FileObjectKind.Unknown))
                return Unavailable(FactState.Changed, "The entry is no longer a symbolic link.");
            if (current.Kind == FileObjectKind.Unknown || current.Identity.State != FactState.Known || !HasMacIdentityTimestamps(after))
                return Unavailable(FactState.Partial, "The second link sample lacks type, identity or timestamp fields.");
            if (identity.Value != current.Identity.Value || !before.Slice(32, 16).SequenceEqual(after.AsSpan(32, 16)) ||
                !before.Slice(64, 16).SequenceEqual(after.AsSpan(64, 16)))
                return Unavailable(FactState.Changed, "The link identity or timestamps changed during the read.");
            return target;
        }
        catch (FileNotFoundException) { return Unavailable(FactState.Changed, "The link disappeared during the read."); }
        catch (UnauthorizedAccessException) { return Unavailable(FactState.Denied, "The link could not be sampled again with this access."); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Unavailable(FactState.Failed, "The second link sample failed."); }
    }

    internal static bool HasMacIdentityTimestamps(ReadOnlySpan<byte> fields)
    {
        // Creation and change times occupy bytes 32–47 and 64–79 of the packed result.
        const uint timestamps = 0x200 | 0x800; // ATTR_CMN_CRTIME | ATTR_CMN_CHGTIME
        return BinaryPrimitives.ReadUInt32LittleEndian(fields) >= 80 &&
            (BinaryPrimitives.ReadUInt32LittleEndian(fields[4..]) & timestamps) == timestamps;
    }

    private static ObservedFact<FileSystem.StoredLinkTarget> DecodeStoredLinkTarget(byte[] buffer, nint count, int error, string source)
    {
        ObservedFact<FileSystem.StoredLinkTarget> Unavailable(FactState state, string reason) =>
            ObservedFact<FileSystem.StoredLinkTarget>.Unavailable(state, FactScope.Entry, source, reason);
        if (count < 0) return error switch
        {
            1 or 13 => Unavailable(FactState.Denied, "The stored target could not be read with this access."),
            2 or 20 or 22 => Unavailable(FactState.Changed, "The link could no longer be read as observed."),
            _ => Unavailable(FactState.Failed, "The stored target could not be read."),
        };
        if (count == 0 || count >= buffer.Length)
            return Unavailable(FactState.Partial, "The stored target was empty or reached the 16 KiB limit.");
        try
        {
            string target = StoredLinkEncoding.GetString(buffer.AsSpan(0, (int)count));
            return ObservedFact<FileSystem.StoredLinkTarget>.Known(new(target, "", !target.StartsWith('/')), FactScope.Entry, source);
        }
        catch (DecoderFallbackException) { return Unavailable(FactState.Unsupported, "The stored target is not valid UTF-8."); }
    }

    internal static FileDetailsReport ParseMacDetails(string path, ReadOnlySpan<byte> bytes)
    {
        // Packed ABI: length, returned attribute_set_t, device, type, four timespecs, uid/gid/mode/flags,
        // file ID, then file link count and all-fork total/allocation. Every scalar needs both mask and slot.
        if (bytes.Length < 24) throw new InvalidDataException("Truncated getattrlist header.");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (length > bytes.Length || length < 24) throw new InvalidDataException("Invalid getattrlist result length.");
        bytes = bytes[..(int)length];
        uint common = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), files = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        const string Source = "getattrlist(FSOPT_NOFOLLOW | FSOPT_PACK_INVAL_ATTRS)";
        static ObservedFact<T> Field<T>(uint mask, uint bit, bool slot, T value, FactScope scope) => slot && (mask & bit) != 0
            ? ObservedFact<T>.Known(value, scope, Source)
            : ObservedFact<T>.Unavailable((mask & bit) != 0 ? FactState.Failed : FactState.Unsupported, scope, Source, "Missing returned mask or packed scalar slot.");
        uint device = bytes.Length >= 28 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) : 0;
        uint type = bytes.Length >= 32 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]) : 0;
        uint owner = bytes.Length >= 100 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[96..]) : 0;
        uint group = bytes.Length >= 104 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[100..]) : 0;
        uint mode = bytes.Length >= 108 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[104..]) : 0;
        uint flags = bytes.Length >= 112 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[108..]) : 0;
        ulong id = bytes.Length >= 120 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[112..]) : 0;
        uint links = bytes.Length >= 124 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[120..]) : 0;
        long size = bytes.Length >= 132 ? BinaryPrimitives.ReadInt64LittleEndian(bytes[124..]) : -1;
        long allocated = bytes.Length >= 140 ? BinaryPrimitives.ReadInt64LittleEndian(bytes[132..]) : -1;
        var snapshot = new FileDetailsSnapshot(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = (common & 8) == 0 || bytes.Length < 32 ? FileObjectKind.Unknown : type switch
            { 1 => FileObjectKind.File, 2 => FileObjectKind.Directory, 3 => FileObjectKind.BlockDevice, 4 => FileObjectKind.CharacterDevice, 5 => FileObjectKind.SymbolicLink, 6 => FileObjectKind.Socket, 7 => FileObjectKind.Fifo, _ => FileObjectKind.Unknown },
            Identity = Field(common, 0x2000000, bytes.Length >= 120 && (common & 2) != 0, new DetailsIdentity(device.ToString(), id.ToString()), FactScope.Entry),
            NativeAttributes = Field(common, 0x40000, bytes.Length >= 112, (ulong)flags, FactScope.Entry),
            LogicalSize = Field(files, 2, size >= 0 && bytes.Length >= 132, size, FactScope.AllForks),
            Allocated = Field(files, 4, allocated >= 0 && bytes.Length >= 140, allocated, FactScope.AllForks),
            LinkCount = Field(files, 1, bytes.Length >= 124, links, FactScope.Entry),
            UnixMode = Field(common, 0x20000, bytes.Length >= 108, mode & 0xFFF, FactScope.Entry),
            Owner = Field(common, 0x8000, bytes.Length >= 100, owner, FactScope.Entry),
            Group = Field(common, 0x10000, bytes.Length >= 104, group, FactScope.Entry),
            Compression = Field(common, 0x40000, bytes.Length >= 112, (flags & 0x20) != 0, FactScope.Entry),
            CompressionLabel = "Compressed representation flag",
            Restrictions = Field(common, 0x40000, bytes.Length >= 112, ChflagsText(flags), FactScope.Entry),
            Notes = ["Compression is a recorded BSD representation flag. This summary does not query volume protection.",
                "The summary request excludes directory entry count, fork values and APFS sharing statistics."]
        };
        return FileDetailsReport.Metadata(snapshot, new InspectionSection("Native getattrlist metadata", [("Returned common mask", $"0x{common:X}"), ("Returned file mask", $"0x{files:X}"), ("Packed length", length.ToString())]));
    }

    [LibraryImport("libc", EntryPoint = "readlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint ReadLinkAt(int fd, string path, byte* buffer, nuint size);

    [LibraryImport("libc", EntryPoint = "readlink", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacReadLink(string path, byte* buffer, nuint size);
}
