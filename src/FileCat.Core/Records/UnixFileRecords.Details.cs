using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
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
        return OperatingSystem.IsLinux() ? LinuxDetails(full, fullReport, ct) : MacDetails(full, ct);
    }

    private static FileDetailsReport LinuxDetails(string path, bool fullReport, CancellationToken ct)
    {
        int noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm or Architecture.Ppc64le ? 0x8000 : 0x20000;
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
            var report = ParseLinuxDetails(path, new ReadOnlySpan<byte>(bytes, 256));
            if (report.Summary.Kind == FileObjectKind.SymbolicLink)
            {
                byte[] buffer = new byte[16 * 1024];
                ct.ThrowIfCancellationRequested();
                nint count;
                fixed (byte* data = buffer) count = ReadLinkAt(fd, "", data, (nuint)buffer.Length);
                ct.ThrowIfCancellationRequested();
                var target = count > 0 && count < buffer.Length
                    ? new System.Text.UTF8Encoding(false, true).GetString(buffer.AsSpan(0, (int)count)) : null;
                report = report with { Summary = report.Summary with { Target = target is null
                    ? ObservedFact<FileSystem.StoredLinkTarget>.Unavailable(FactState.Partial, FactScope.Entry, "readlinkat(O_PATH)", "Stored target unavailable or exceeds 16 KiB.")
                    : ObservedFact<FileSystem.StoredLinkTarget>.Known(new(target, "", !target.StartsWith('/')), FactScope.Entry, "readlinkat(O_PATH)") } };
            }
            if (fullReport && report.Summary is { Kind: FileObjectKind.File, Identity: { State: FactState.Known, Value: { } identity } })
                report = AddLinuxExtents(report, ReadLinuxPinnedExtents(fd, identity, ct));
            return report;
        }
        finally { Close(fd); }
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
            LogicalSize = rawSize <= long.MaxValue ? Field(0x200, (long)rawSize, FactScope.Entry) : ObservedFact<long>.Unavailable(FactState.Failed, FactScope.Entry, Source, "Size overflow."),
            Allocated = blocks <= long.MaxValue / 512 ? Field(0x400, (long)blocks * 512, FactScope.Entry) : ObservedFact<long>.Unavailable(FactState.Failed, FactScope.Entry, Source, "Allocation overflow."),
            LinkCount = Field(4, BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]), FactScope.Entry),
            NativeAttributes = ObservedFact<ulong>.Known(attrs & supported, FactScope.Entry, Source + " · supported mask only"),
            UnixMode = Field(2, mode & 0xFFF, FactScope.Entry),
            Owner = Field(8, BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]), FactScope.Entry),
            Group = Field(0x10, BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]), FactScope.Entry),
            Compression = Flag(4), FileEncryption = Flag(0x800),
            Restrictions = ObservedFact<string>.Known($"0x{attrs & supported:X}; supported mask 0x{supported:X}", FactScope.Entry, Source),
            Notes = ["Allocation is inode-reported blocks × 512, not exclusive storage or recursive contents.",
                $"statx field mask: 0x{mask:X}; attribute mask: 0x{supported:X}; raw attributes: 0x{attrs:X}.",
                "Filesystem policy, fscrypt mechanism, xattrs and effective access were not queried."]
        };
        return FileDetailsReport.Metadata(snapshot, new InspectionSection("Native statx metadata", [("Returned mask", $"0x{mask:X}"), ("Attribute support mask", $"0x{supported:X}"), ("Raw attributes", $"0x{attrs:X}")]));
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

    private static FileDetailsReport MacDetails(string path, CancellationToken ct)
    {
        // A separate summary request: no entry count, resource fork, clone statistics, ACL or xattr values.
        const uint Common = 0x80000000 | 0x2 | 0x8 | 0x200 | 0x400 | 0x800 | 0x1000 | 0x8000 | 0x10000 | 0x20000 | 0x40000 | 0x2000000;
        byte* list = stackalloc byte[24];
        new Span<byte>(list, 24).Clear();
        *(ushort*)list = 5;
        *(uint*)(list + 4) = Common;
        *(uint*)(list + 16) = 7; // link count, total size and allocation (all forks); returned mask determines validity
        byte[] buffer = new byte[256];
        ct.ThrowIfCancellationRequested();
        int status;
        fixed (byte* data = buffer) status = GetAttrList(path, list, data, (nuint)buffer.Length, 1 | 8);
        ct.ThrowIfCancellationRequested();
        if (status != 0) throw Error(path);
        return ParseMacDetails(path, buffer);
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
            Notes = ["Compression is a recorded BSD representation flag; FileVault and volume protection were not queried.",
                "This minimal request excludes directory entry count, fork values and APFS sharing statistics."]
        };
        return FileDetailsReport.Metadata(snapshot, new InspectionSection("Native getattrlist metadata", [("Returned common mask", $"0x{common:X}"), ("Returned file mask", $"0x{files:X}"), ("Packed length", length.ToString())]));
    }

    [LibraryImport("libc", EntryPoint = "readlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint ReadLinkAt(int fd, string path, byte* buffer, nuint size);
}
