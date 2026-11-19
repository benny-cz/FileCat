using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.HiddenData;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsHiddenData
{
    public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var file = OpenForEas(path, 0x80 | 0x100000 | (request.IncludeEaValues ? FileReadEa : 0));
        ct.ThrowIfCancellationRequested();
        uint attributes = Elevation.NtFile.GetBasic(file).FileAttributes;
        ct.ThrowIfCancellationRequested();
        if ((attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 || (attributes & 0x400000) != 0)
            return HiddenInventory.Unavailable(path, HiddenInventoryState.Unsupported, "The metadata-only inventory path is not qualified for reparse/offline/provider objects.");
        var backing = WindowsStorageBacking.Query(file, ct);
        if (backing.External != false) return HiddenInventory.Unavailable(path, HiddenInventoryState.Unsupported,
            backing.External == true ? $"Externally backed object (provider {backing.Provider}); supplementary values/actions are managed by the filesystem."
                : "External-backing state is unavailable; supplementary acquisition is not qualified. " + backing.Reason);
        var identity = ProtectedHexFile.Identity(file);
        ct.ThrowIfCancellationRequested();
        var streams = ReadStreamInventory(file, ct);
        var items = streams.Items;
        var categories = ImmutableArray.CreateBuilder<HiddenCategoryInventory>();
        categories.Add(new(HiddenKind.Stream, streams.State, streams.Items.Length, "held object; main stream excluded", streams.Problem));
        bool acquired = false;
        if (request.IncludeEaValues)
        {
            var eas = ReadEaInventory(file, ct);
            acquired = eas.Acquired;
            int bytes = items.Sum(i => i.Name.Length * 2 + 64) + eas.Items.Sum(i => i.Name.Length * 2 + 64);
            if (items.Length + eas.Items.Length <= HiddenInventory.MaxEntries && bytes <= HiddenInventory.MaxNameBytes) items = items.AddRange(eas.Items);
            else eas = eas with { Items = [], State = HiddenInventoryState.Partial, Problem = "The combined 1,024-entry/1 MiB metadata limit was reached." };
            categories.Add(new(HiddenKind.NtfsAttribute, eas.State, eas.Items.Length, "held object; native EA buffer includes values", eas.Problem));
        }
        else categories.Add(new(HiddenKind.NtfsAttribute, HiddenInventoryState.NotInspected, 0, "EA query returns names and values", "Use Read extended attributes to admit that query."));
        ct.ThrowIfCancellationRequested();
        return new(path, DateTimeOffset.UtcNow, items, categories.ToImmutable(), "FileStreamInfo / NtQueryEaFile on one no-follow metadata handle")
        { Identity = identity, BoundToObject = true, ValueBearingQueryIssued = acquired };
    }

    internal sealed record InventoryPart(ImmutableArray<HiddenItem> Items, HiddenInventoryState State, string? Problem = null, bool Acquired = false);

    internal static InventoryPart ReadStreamInventory(SafeFileHandle file, CancellationToken ct)
    {
        for (int attempt = 0, size = 64 * 1024; attempt < 3; attempt++, size *= 4)
        {
            byte[] buffer = new byte[size];
            ct.ThrowIfCancellationRequested();
            bool success;
            fixed (byte* data = buffer) success = GetInventoryInfo(file, 7 /* FileStreamInfo */, data, (uint)buffer.Length);
            int error = success ? 0 : Marshal.GetLastPInvokeError();
            ct.ThrowIfCancellationRequested();
            if (success) return ParseStreamInventory(buffer);
            if (error is 122 or 234 && attempt < 2) continue;
            return new([], error is 122 or 234 ? HiddenInventoryState.Partial : error == 5 ? HiddenInventoryState.Denied : error is 1 or 50 or 87 ? HiddenInventoryState.Unsupported : HiddenInventoryState.Failed,
                error is 122 or 234 ? "Stream metadata exceeded the bounded resize allowance." : new System.ComponentModel.Win32Exception(error).Message);
        }
        throw new InvalidOperationException();
    }

    internal static InventoryPart ParseStreamInventory(ReadOnlySpan<byte> bytes)
    {
        var items = ImmutableArray.CreateBuilder<HiddenItem>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int at = 0, retained = 0;
        while (at <= bytes.Length - 24)
        {
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]), length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 4)..]);
            if (length == 0 || (length & 1) != 0 || length > 65532 || length > bytes.Length - at - 24 || next != 0 && (next < 24 + length || next > bytes.Length - at || (next & 7) != 0))
                return new(items.ToImmutable(), HiddenInventoryState.Failed, "Malformed stream metadata record.");
            string raw;
            try { raw = new UnicodeEncoding(false, false, true).GetString(bytes.Slice(at + 24, (int)length)); }
            catch (DecoderFallbackException) { return new(items.ToImmutable(), HiddenInventoryState.Failed, "Stream name is not valid UTF-16; actions are unavailable."); }
            long size = BinaryPrimitives.ReadInt64LittleEndian(bytes[(at + 8)..]);
            if (size < 0 || !raw.StartsWith(':') || !raw.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase))
                return new(items.ToImmutable(), HiddenInventoryState.Failed, "Unqualified stream record or size.");
            if (!raw.Equals("::$DATA", StringComparison.OrdinalIgnoreCase))
            {
                string name = raw[1..^6];
                if (name.Length == 0 || name.IndexOfAny(['\0', ':', '/', '\\']) >= 0 || !names.Add(name))
                    return new(items.ToImmutable(), HiddenInventoryState.Failed, "Ambiguous, repeated or invalid stream name.");
                if (items.Count == HiddenInventory.MaxEntries || (retained += length > int.MaxValue - 64 ? int.MaxValue : (int)length + 64) > HiddenInventory.MaxNameBytes)
                    return new(items.ToImmutable(), HiddenInventoryState.Partial, "The 1,024-entry/1 MiB names limit was reached.");
                var item = new HiddenItem(name, HiddenKind.Stream, size);
                items.Add(item with { Role = HiddenMetadataRoles.Classify(item) });
            }
            if (next == 0) return new(items.ToImmutable(), HiddenInventoryState.Complete);
            at += (int)next;
        }
        return new(items.ToImmutable(), HiddenInventoryState.Failed, "Truncated stream metadata.");
    }

    private static InventoryPart ReadEaInventory(SafeFileHandle file, CancellationToken ct)
    {
        byte[] buffer = new byte[128 * 1024];
        var items = ImmutableArray.CreateBuilder<HiddenItem>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int acquired = 0;
        try
        {
            for (int call = 0; call < 8; call++)
            {
                buffer.AsSpan().Clear();
                ct.ThrowIfCancellationRequested();
                int status; IoStatusBlock io;
                fixed (byte* data = buffer) status = NtQueryEaFile(file, out io, data, (uint)buffer.Length, 0, null, 0, null, call == 0 ? (byte)1 : (byte)0);
                ct.ThrowIfCancellationRequested();
                if (status is NoEas or NoMoreEas) return new(items.ToImmutable(), HiddenInventoryState.Complete, Acquired: true);
                long got = io.Information;
                if (status < 0 && status != unchecked((int)0x80000005))
                    return new(items.ToImmutable(), status == unchecked((int)0xC0000022) ? HiddenInventoryState.Denied : HiddenInventoryState.Failed, Error(RtlNtStatusToDosError(status), "Extended attributes").Message, true);
                if (got <= 0 || got > buffer.Length || (acquired += (int)got) > 1024 * 1024)
                    return new(items.ToImmutable(), HiddenInventoryState.Partial, "EA query made no progress or reached its 1 MiB acquisition limit.", true);
                var part = ParseEaInventory(buffer.AsSpan(0, (int)got));
                foreach (var item in part.Items)
                {
                    if (!names.Add(item.Name)) return new(items.ToImmutable(), HiddenInventoryState.Changed, "EA enumeration repeated a name; the item may have changed.", true);
                    if (items.Count == HiddenInventory.MaxEntries) return new(items.ToImmutable(), HiddenInventoryState.Partial, "The 1,024-entry limit was reached.", true);
                    items.Add(item);
                }
                if (part.State != HiddenInventoryState.Complete) return part with { Items = items.ToImmutable(), Acquired = true };
                // A successful chunk is not an exhaustion proof. Continue on this held query cursor until the
                // native terminal status; a repeated name, missing progress or exhausted budget stays incomplete.
            }
            return new(items.ToImmutable(), HiddenInventoryState.Partial, "The bounded EA continuation limit was reached.", true);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    internal static InventoryPart ParseEaInventory(ReadOnlySpan<byte> bytes)
    {
        var items = ImmutableArray.CreateBuilder<HiddenItem>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int at = 0;
        while (at <= bytes.Length - 8)
        {
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
            int length = bytes[at + 5], value = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(at + 6)..]);
            int record = 8 + length + 1 + value;
            if (length == 0 || record > bytes.Length - at || bytes[at + 8 + length] != 0 || bytes.Slice(at + 8, length).Contains((byte)0) ||
                next != 0 && (next < record || next > bytes.Length - at || (next & 3) != 0))
                return new(items.ToImmutable(), HiddenInventoryState.Failed, "Malformed or truncated EA metadata record.", true);
            var item = new HiddenItem(Encoding.Latin1.GetString(bytes.Slice(at + 8, length)), HiddenKind.NtfsAttribute, value) { NativeFlags = bytes[at + 4] };
            if (!names.Add(item.Name)) return new(items.ToImmutable(), HiddenInventoryState.Changed, "EA enumeration repeated a native name.", true);
            items.Add(item with { Role = HiddenMetadataRoles.Classify(item) });
            if (items.Count > HiddenInventory.MaxEntries) return new(items.Take(HiddenInventory.MaxEntries).ToImmutableArray(), HiddenInventoryState.Partial, "The EA entry limit was reached.", true);
            if (next == 0) return new(items.ToImmutable(), HiddenInventoryState.Complete, Acquired: true);
            at += (int)next;
        }
        return new(items.ToImmutable(), HiddenInventoryState.Failed, "Truncated EA metadata header.", true);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetInventoryInfo(SafeFileHandle file, int infoClass, void* buffer, uint size);
}
