using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Inspect;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const string BtrfsSubvolumeSource = "BTRFS_IOC_INO_LOOKUP on the held object";
    private const string BtrfsCompressionSource = "btrfs.compression property on the held inode";

    private static bool IsHeldBtrfs(int held, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte* filesystem = stackalloc byte[256];
        bool btrfs = DetailsFstatfs(held, filesystem) == 0 && (uint)*(nint*)filesystem == 0x9123683e; // BTRFS_SUPER_MAGIC
        ct.ThrowIfCancellationRequested();
        return btrfs;
    }

    private static ObservedFact<ulong> ReadBtrfsSubvolume(SafeFileHandle file, CancellationToken ct)
    {
        const ulong lookup = 0xd0009412; // BTRFS_IOC_INO_LOOKUP: the generic encoding, the same on x64 and ARM64.
        Span<byte> request = stackalloc byte[4096];
        request.Clear();
        // With treeid=0 and objectid=BTRFS_FIRST_FREE_OBJECTID, the ioctl returns
        // the containing subvolume ID without resolving a pathname or requiring privilege.
        BinaryPrimitives.WriteUInt64LittleEndian(request[8..], 256);
        ct.ThrowIfCancellationRequested();
        int error;
        fixed (byte* data = request)
            error = Ioctl((int)file.DangerousGetHandle(), lookup, data) == 0 ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        if (error != 0)
            return ObservedFact<ulong>.Unavailable(LinuxMetadataFailure(error), FactScope.Dataset, BtrfsSubvolumeSource,
                $"The containing-subvolume query returned errno {error}.");
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(request);
        return id != 0 && request[16] == 0
            ? ObservedFact<ulong>.Known(id, FactScope.Dataset, BtrfsSubvolumeSource)
            : ObservedFact<ulong>.Unavailable(FactState.Failed, FactScope.Dataset, BtrfsSubvolumeSource,
                "The kernel did not return a containing subvolume ID in the ID-only reply.");
    }

    private static ObservedFact<string?> ReadBtrfsCompression(SafeFileHandle file, FactScope scope, CancellationToken ct)
    {
        // This is the kernel's fixed compression property. Supplementary attributes and file contents stay unexamined.
        Span<byte> bytes = stackalloc byte[16];
        ct.ThrowIfCancellationRequested();
        nint length;
        int error;
        fixed (byte* data = bytes)
        {
            length = LinuxDetailsFGetXattr((int)file.DangerousGetHandle(), "btrfs.compression", data, (nuint)bytes.Length);
            error = length < 0 ? Marshal.GetLastPInvokeError() : 0;
        }
        ct.ThrowIfCancellationRequested();
        if (error == 61 || length == 0) // ENODATA or the default property value.
            return ObservedFact<string?>.Known(null, scope, BtrfsCompressionSource);
        if (error == 34 || length > bytes.Length) // ERANGE
            return ObservedFact<string?>.Unavailable(FactState.Partial, scope, BtrfsCompressionSource,
                "The compression property exceeds the 16-byte inspection limit.");
        if (error != 0)
            return ObservedFact<string?>.Unavailable(LinuxMetadataFailure(error), scope, BtrfsCompressionSource,
                $"The compression property query returned errno {error}.");
        return ParseBtrfsCompression(bytes[..(int)length], scope);
    }

    internal static ObservedFact<string?> ParseBtrfsCompression(ReadOnlySpan<byte> bytes, FactScope scope)
    {
        string? policy = Encoding.ASCII.GetString(bytes) switch
        {
            "zlib" => "zlib",
            "lzo" => "lzo",
            "zstd" => "zstd",
            "no" or "none" => "Disabled by the inode property",
            _ => null,
        };
        return policy is not null
            ? ObservedFact<string?>.Known(policy, scope, BtrfsCompressionSource)
            : ObservedFact<string?>.Unavailable(FactState.Unsupported, scope, BtrfsCompressionSource,
                "The kernel returned an unrecognized compression property.");
    }

    private static FileDetailsReport AddLinuxBtrfs(FileDetailsReport report, SafeFileHandle? file, FactState state,
        string? reason, CancellationToken ct)
    {
        bool directory = report.Summary.Kind == FileObjectKind.Directory;
        FactScope scope = directory ? FactScope.DirectoryPolicy : FactScope.Entry;
        var subvolume = file is null
            ? ObservedFact<ulong>.Unavailable(state, FactScope.Dataset, BtrfsSubvolumeSource, reason!)
            : ReadBtrfsSubvolume(file, ct);
        var compression = file is null
            ? ObservedFact<string?>.Unavailable(state, scope, BtrfsCompressionSource, reason!)
            : ReadBtrfsCompression(file, scope, ct);
        var lines = new List<string> { "Existing extent compression and mount-wide defaults were not assessed by this policy query." };
        if (subvolume.Reason is { } subvolumeReason) lines.Add(subvolumeReason);
        if (compression.Reason is { } compressionReason && compressionReason != subvolume.Reason) lines.Add(compressionReason);
        var section = new InspectionSection("Btrfs properties",
        [
            ("Containing subvolume ID", subvolume.State == FactState.Known ? subvolume.Value.ToString(CultureInfo.InvariantCulture) : subvolume.State.ToString()),
            ("Subvolume source", subvolume.Source),
            ("Compression policy", compression.State == FactState.Known ? compression.Value ?? "No explicit inode property" : compression.State.ToString()),
            ("Policy scope", directory ? "Directory policy for new entries" : "Regular file inode policy"),
            ("Policy source", compression.Source),
        ]) { Lines = lines };
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
    }
}
