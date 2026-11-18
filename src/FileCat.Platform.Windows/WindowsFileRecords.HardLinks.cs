using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsFileRecords
{
    internal const int HardLinkNameLimit = 1024, HardLinkByteLimit = 1024 * 1024;
    private const int HardLinkHeader = 8, HardLinkEntryHeader = 20, HardLinkAttempts = 3;
    private const int BufferOverflow = unchecked((int)0x80000005), BufferTooSmall = unchecked((int)0xc0000023);
    private const string HardLinkSource = "FileHardLinkInformation on the held file";
    private static readonly UnicodeEncoding HardLinkEncoding = new(false, false, true);

    internal readonly record struct HardLinkName(ulong ParentFileId, string Leaf);

    internal static ObservedFact<IReadOnlyList<HardLinkName>> ReadHardLinkNames(Func<byte[], (int Status, int Returned)> query,
        CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (status, returned) = query(buffer);
            ct.ThrowIfCancellationRequested();
            if (status is not (0 or BufferOverflow or BufferTooSmall))
            {
                var state = status switch
                {
                    unchecked((int)0xc0000022) => FactState.Denied,
                    unchecked((int)0xc0000003) or unchecked((int)0xc00000bb) => FactState.Unsupported,
                    _ => FactState.Failed,
                };
                return HardLinkResult(state, [], $"The hard-link query failed with NTSTATUS 0x{unchecked((uint)status):X8}.");
            }
            if (returned < HardLinkHeader || returned > buffer.Length)
                return HardLinkResult(FactState.Failed, [], "Windows returned an invalid hard-link response length.");
            uint required = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            if (required < HardLinkHeader || status == 0 && required > returned)
                return HardLinkResult(FactState.Failed, [], "The declared hard-link byte count is invalid.");
            if (status != 0 && required > buffer.Length && required <= HardLinkByteLimit && attempt + 1 < HardLinkAttempts)
            {
                buffer = new byte[(int)required];
                continue;
            }
            string? limit = status == 0 ? null : required > HardLinkByteLimit
                ? "The hard-link inventory exceeds the 1 MiB inspection limit."
                : "Windows did not complete the hard-link inventory within the bounded retries.";
            return ParseHardLinkNames(buffer.AsSpan(0, returned), limit, ct);
        }
    }

    private static ObservedFact<IReadOnlyList<HardLinkName>> ParseHardLinkNames(ReadOnlySpan<byte> bytes, string? limit, CancellationToken ct)
    {
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var names = new List<HardLinkName>();
        if (declared > (bytes.Length - HardLinkHeader) / (HardLinkEntryHeader + 2))
            return HardLinkResult(FactState.Failed, [], "The hard-link count exceeds the returned bytes.");

        int at = HardLinkHeader;
        for (int i = 0; i < declared && i < HardLinkNameLimit; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (at > bytes.Length - HardLinkEntryHeader) return Malformed();
            ReadOnlySpan<byte> entry = bytes[at..];
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            uint units = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            if (units is 0 or > 32767 || units > (entry.Length - HardLinkEntryHeader) / 2) return Malformed();
            int recordLength = HardLinkEntryHeader + 2 * (int)units;
            if (i + 1 == declared ? next != 0 : next < recordLength || next > entry.Length || next % 8 != 0)
                return Malformed();
            string leaf;
            try { leaf = HardLinkEncoding.GetString(entry.Slice(HardLinkEntryHeader, (int)units * 2)); }
            catch (DecoderFallbackException) { return Malformed(); }
            if (!StoragePaths.IsChildName(leaf)) return Malformed();
            names.Add(new(parent, leaf));
            at += (int)next;
        }
        if (declared > HardLinkNameLimit) limit = $"The inventory exceeds the {HardLinkNameLimit:N0}-name limit.";
        return HardLinkResult(limit is null ? FactState.Known : FactState.Partial, names.AsReadOnly(), limit);

        ObservedFact<IReadOnlyList<HardLinkName>> Malformed() => HardLinkResult(names.Count == 0 ? FactState.Failed : FactState.Partial,
            names.AsReadOnly(), "Windows returned a malformed hard-link name or continuation offset.");
    }

    private static ObservedFact<IReadOnlyList<HardLinkName>> HardLinkResult(FactState state, IReadOnlyList<HardLinkName> names, string? reason) =>
        new(state, names, FactScope.Namespace, HardLinkSource, reason);

    private static InspectionSection HardLinkSection(SafeFileHandle item, CancellationToken ct)
    {
        var result = ReadHardLinkNames(buffer =>
        {
            HardLinkIoStatus io = default;
            int status;
            fixed (byte* data = buffer) status = QueryHardLinkInformation(item, &io, data, (uint)buffer.Length, 46 /* FileHardLinkInformation */);
            return (status, io.Bytes <= int.MaxValue ? (int)io.Bytes : -1);
        }, ct);
        var names = result.Value!;
        const int displayLimit = 40;
        var parentNames = new Dictionary<ulong, string?>();
        var rows = new List<string[]>();
        int remainingPathUnits = HardLinkByteLimit / 2;
        foreach (var link in names.Take(displayLimit))
        {
            ct.ThrowIfCancellationRequested();
            if (!parentNames.TryGetValue(link.ParentFileId, out string? parent))
                parentNames[link.ParentFileId] = parent = ResolveHardLinkParent(item, link.ParentFileId, ct);
            string location = "Unavailable";
            if (parent is not null && parent.Length + 1 + link.Leaf.Length <= remainingPathUnits)
            {
                string relative = parent.TrimEnd('\\') + "\\" + link.Leaf;
                remainingPathUnits -= relative.Length;
                location = JsonSerializer.Serialize(relative);
            }
            rows.Add([JsonSerializer.Serialize(link.Leaf), link.ParentFileId == 0 ? "Unavailable" : $"{link.ParentFileId:X16}", location]);
        }
        bool complete = result.State == FactState.Known;
        return new("Hard-link names", [("Returned names", complete || names.Count > 0 ? names.Count.ToString("N0") : "Unavailable"),
            ("Inventory", complete ? "Complete" : result.State.ToString()), ("Source", result.Source)])
        {
            Table = rows.Count == 0 ? null : new InspectionTable(["Name", "Parent file ID", "Observed volume-relative path"], rows)
            { More = names.Count > displayLimit ? $"Showing the first {displayLimit} returned names." : null },
            Lines = result.Reason is { } reason ? [reason] : [],
        };
    }

    private static string? ResolveHardLinkParent(SafeFileHandle item, ulong identity, CancellationToken ct)
    {
        if (identity == 0) return null;
        ct.ThrowIfCancellationRequested();
        var descriptor = new HardLinkFileId { Size = (uint)sizeof(HardLinkFileId), Identity = identity };
        using var folder = OpenHardLinkParent(item, &descriptor, FileReadAttributes, ShareAll, 0, BackupSemantics | OpenReparsePoint);
        ct.ThrowIfCancellationRequested();
        if (folder.IsInvalid) return null;
        Span<byte> info = stackalloc byte[24];
        bool same = Info(folder, 18 /* FileIdInfo */, info) && BinaryPrimitives.ReadUInt64LittleEndian(info[8..]) == identity;
        ct.ThrowIfCancellationRequested();
        if (!same) return null;
        bool directory = Info(folder, 1 /* FileStandardInfo */, info) && info[21] != 0;
        ct.ThrowIfCancellationRequested();
        if (!directory) return null;
        var buffer = new char[256];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            uint length;
            fixed (char* data = buffer) length = HardLinkParentName(folder, data, (uint)buffer.Length, 4 /* VOLUME_NAME_NONE */);
            ct.ThrowIfCancellationRequested();
            if (length == 0 || length > 32767) return null;
            if (length < buffer.Length) return buffer[0] == '\\' ? new string(buffer, 0, (int)length) : null;
            buffer = new char[length + 1];
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardLinkIoStatus { public nint Status; public nuint Bytes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardLinkFileId { public uint Size, Kind; public ulong Identity, Padding; }

    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryInformationFile")]
    private static partial int QueryHardLinkInformation(SafeFileHandle file, HardLinkIoStatus* io, void* data, uint size, int kind);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenFileById", SetLastError = true)]
    private static partial SafeFileHandle OpenHardLinkParent(SafeFileHandle hint, HardLinkFileId* id, uint access, uint share, nint security, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint HardLinkParentName(SafeFileHandle file, char* buffer, uint length, uint flags);
}
