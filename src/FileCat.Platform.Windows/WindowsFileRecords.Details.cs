using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsFileRecords
{
    public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct) => ReadDetails(path, fullReport: false, ct).Summary;

    public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct)
    {
        if (policy != FileDetailsPolicy.MetadataOnly) throw new NotSupportedException("This details route supports metadata only.");
        return ReadDetails(path, fullReport: true, ct);
    }

    private static FileDetailsReport ReadDetails(string path, bool fullReport, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ct.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path);
        using var item = CreateFile(WindowsFileOperations.Long(full), FileReadAttributes | Synchronize, ShareAll, 0,
            OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (item.IsInvalid) throw new IOException("The metadata object could not be opened: " + new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        ct.ThrowIfCancellationRequested();
        bool Query(int infoClass, Span<byte> bytes)
        {
            ct.ThrowIfCancellationRequested();
            bytes.Clear();
            bool success = Info(item, infoClass, bytes);
            ct.ThrowIfCancellationRequested();
            return success;
        }

        Span<byte> basic = stackalloc byte[40], standard = stackalloc byte[24], id = stackalloc byte[24], tag = stackalloc byte[8];
        bool hasBasic = Query(0, basic), hasStandard = Query(1, standard), hasId = Query(18, id);
        uint attributes = hasBasic ? BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]) : 0;
        bool reparse = hasBasic && (attributes & (uint)FileAttributes.ReparsePoint) != 0;
        bool hasTag = reparse && Query(9, tag);
        uint reparseTag = hasTag ? BinaryPrimitives.ReadUInt32LittleEndian(tag[4..]) : 0;
        bool folder = hasStandard ? standard[21] != 0 : hasBasic && (attributes & (uint)FileAttributes.Directory) != 0;
        var kind = hasStandard || hasBasic ? folder ? FileObjectKind.Directory : FileObjectKind.File : FileObjectKind.Unknown;
        if (reparseTag == ReparseLinks.SymbolicLinkTag) kind = FileObjectKind.SymbolicLink;

        ct.ThrowIfCancellationRequested();
        char* label = stackalloc char[261], fs = stackalloc char[261];
        bool hasVolume = GetVolumeInformationByHandle(item, label, 261, out uint serial, out _, out uint caps, fs, 261);
        ct.ThrowIfCancellationRequested();
        string format = hasVolume ? new string(fs) : "";
        var scope = folder && !reparse ? FactScope.DirectoryPolicy : FactScope.Entry;
        const string BasicSource = "FileBasicInfo (held object)", StandardSource = "FileStandardInfo (held main stream)";
        ObservedFact<T> Known<T>(T value, FactScope s, string source) => ObservedFact<T>.Known(value, s, source);
        ObservedFact<T> Failed<T>(FactScope s, string source) => ObservedFact<T>.Unavailable(FactState.Failed, s, source, "The native metadata query did not return this field.");
        ObservedFact<bool> State(uint bit, uint capability, FactScope s) => !hasBasic || !hasVolume
            ? Failed<bool>(s, BasicSource)
            : (caps & capability) == 0 ? ObservedFact<bool>.Unavailable(FactState.Unsupported, s, BasicSource, "The observed volume does not advertise this capability.")
            : Known((attributes & bit) != 0, s, BasicSource);

        var snapshot = new FileDetailsSnapshot(full, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = kind,
            Identity = hasId ? Known(new DetailsIdentity(BinaryPrimitives.ReadUInt64LittleEndian(id).ToString("X16"),
                BinaryPrimitives.ReadUInt128LittleEndian(id[8..]).ToString("X32")), FactScope.Entry, "FileIdInfo") : Failed<DetailsIdentity>(FactScope.Entry, "FileIdInfo"),
            FileSystem = hasVolume ? Known(format, FactScope.Volume, "GetVolumeInformationByHandleW") : Failed<string>(FactScope.Volume, "GetVolumeInformationByHandleW"),
            NativeAttributes = hasBasic ? Known((ulong)attributes, FactScope.Entry, BasicSource) : Failed<ulong>(FactScope.Entry, BasicSource),
            LogicalSize = hasStandard && !folder ? Known(BinaryPrimitives.ReadInt64LittleEndian(standard[8..]), FactScope.MainData, StandardSource) : FileDetailsSnapshot.Unknown<long>(FactScope.MainData, "Directory contents were not counted."),
            Allocated = hasStandard ? Known(BinaryPrimitives.ReadInt64LittleEndian(standard), folder ? FactScope.Entry : FactScope.MainData, StandardSource) : Failed<long>(FactScope.MainData, StandardSource),
            LinkCount = hasStandard ? Known(BinaryPrimitives.ReadUInt32LittleEndian(standard[16..]), FactScope.Entry, StandardSource) : Failed<uint>(FactScope.Entry, StandardSource),
            ReparseTag = reparse ? hasTag ? Known(reparseTag, FactScope.Entry, "FileAttributeTagInfo")
                : ObservedFact<uint>.Unavailable(FactState.Failed, FactScope.Entry, "FileAttributeTagInfo", "Reparse type unavailable.")
                : ObservedFact<uint>.Unavailable(hasBasic ? FactState.NotApplicable : FactState.Failed, FactScope.Entry, BasicSource, "No observed reparse tag."),
            Compression = reparse ? FileDetailsSnapshot.Unknown<bool>(FactScope.MainData, "Reparse backing is separate from NTFS compression.") : State(0x800, 0x10, folder ? scope : FactScope.MainData),
            FileEncryption = State(0x4000, 0x20000, scope),
            CompressionLabel = format == "NTFS" ? "NTFS compression" : "Filesystem compression",
            EncryptionLabel = "EFS",
            Restrictions = hasBasic ? Known(RecordText.Attributes(attributes), FactScope.Entry, BasicSource) : Failed<string>(FactScope.Entry, BasicSource),
            Cloud = hasBasic && (ReparsePresentation.IsCloudTag(reparseTag) || (attributes & 0x580000) != 0)
                ? Known(CloudFiles.Observe((FileAttributes)attributes, ReparsePresentation.IsCloudTag(reparseTag), enumerationAttributes: false), FactScope.Entry, BasicSource)
                : FileDetailsSnapshot.Unknown<CloudObservation>(FactScope.Entry),
            Sparse = hasBasic ? Known((attributes & 0x200) != 0, FactScope.Entry, BasicSource + " · sparse indication; current main-data holes not assessed") : Failed<bool>(FactScope.Entry, BasicSource),
            Notes = ["The native queries share one metadata-only object handle; metadata may change between queries.",
                hasVolume ? $"Observed volume serial: {serial:X8}" : "Volume metadata unavailable."]
        };

        // Only supported stored filesystem names are requested. No target check, content read, raw device or legacy reader.
        if (reparseTag is ReparseLinks.SymbolicLinkTag or ReparseLinks.MountPointTag)
        {
            byte[] output = new byte[16 * 1024];
            ct.ThrowIfCancellationRequested();
            int error = Ioctl(item, FsctlGetReparsePoint, [], output, out int returned);
            ct.ThrowIfCancellationRequested();
            StoredLinkTarget? target = null;
            if (error == 0 && returned is >= 8 and <= 16 * 1024 && BinaryPrimitives.ReadUInt32LittleEndian(output) == reparseTag)
            {
                int size = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4));
                if (size <= returned - 8) target = ReparsePresentation.ParseLink(reparseTag, output.AsSpan(8, size));
            }
            snapshot = snapshot with { Target = target is not null ? Known(target, FactScope.Entry, "FSCTL_GET_REPARSE_POINT")
                : ObservedFact<StoredLinkTarget>.Unavailable(FactState.Failed, FactScope.Entry, "FSCTL_GET_REPARSE_POINT", "Stored link payload was unavailable, changed or malformed.") };
        }
        var fields = new List<(string, string)>();
        if (hasBasic)
        {
            fields.Add(("Native attributes", RecordText.Attributes(attributes)));
            string[] names = ["Created", "Accessed", "Modified", "Changed"];
            for (int i = 0; i < 4; i++) fields.Add((names[i], RecordText.Time(BinaryPrimitives.ReadInt64LittleEndian(basic[(8 * i)..]))));
        }
        if (reparse) fields.Add(("Reparse tag", hasTag ? RecordText.ReparseTag(reparseTag) : "Unavailable"));
        ct.ThrowIfCancellationRequested();
        var metadata = new InspectionSection("Native metadata", fields.ToArray());
        if (!fullReport || !hasBasic || !hasStandard || folder || reparse || format != "NTFS" ||
            (attributes & (0x1000u | 0x580000u)) != 0 || !StoragePaths.IsOrdinaryLocalPath(full) || !WindowsFileOperations.IsReplaceVolume(full))
            return FileDetailsReport.Metadata(snapshot, metadata);
        // A fixed drive letter can reach another provider through a parent. Qualify the volume of the held object.
        try { _ = WindowsStorageTransforms.ActualVolumeBinding(item, rootOnly: false, ct); }
        catch (StorageInspectionException) { return FileDetailsReport.Metadata(snapshot, metadata); }

        var extents = RetrievalSection(item, ct);
        var report = FileDetailsReport.Metadata(snapshot, metadata, extents);
        return report with { Technical = report.Technical with
        {
            Warnings = ["Not acquired: supplementary-data values and previews, raw volumes and journals, security value collectors, hard-link names and allocated byte ranges."],
        } };
    }
}
