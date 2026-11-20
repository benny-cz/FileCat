using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed record WindowsEfsStream(string NativeName, long Length, StorageObservationState Compression)
{
    internal WindowsStorageTransforms.SparseLayout? Sparse { get; init; }
}

/// <summary>Complete, bounded metadata from the held object. Reads no stream payload and never queries EAs.</summary>
internal static unsafe partial class WindowsEfsStreams
{
    internal const int EntryLimit = 1024, MetadataLimit = 1024 * 1024;
    private const int Overflow = unchecked((int)0x80000005), TooSmall = unchecked((int)0xC0000023), LengthMismatch = unchecked((int)0xC0000004);
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public nint Status; public nuint Information; }

    internal static ImmutableArray<WindowsEfsStream> Read(SafeFileHandle root, StorageObjectIdentity identity, bool directory,
        CancellationToken ct, bool includeSparseLayouts = false)
    {
        var names = ReadNames(root, directory, ct);
        var result = ImmutableArray.CreateBuilder<WindowsEfsStream>(names.Length);
        int sparseRanges = 0;
        foreach (var entry in names)
        {
            ct.ThrowIfCancellationRequested();
            if (!directory && entry.NativeName == "::$DATA")
            {
                var format = WindowsCompression.Query(root); ct.ThrowIfCancellationRequested();
                if (format.State is not (StorageObservationState.Enabled or StorageObservationState.Disabled)) throw Failure("stream-compression", "The unnamed stream's compression state could not be established.");
                result.Add(AddSparseLayout(entry with { Compression = format.State }, root));
                continue;
            }
            using var charge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
            int status = NtFile.TryOpen(root, entry.NativeName, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open,
                NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | 0x400000 /* FILE_OPEN_NO_RECALL */, out var stream, caseSensitive: true);
            using (stream)
            {
                if (status < 0) throw new StorageInspectionException(new(StorageReasonKind.Unknown, "stream-open", "A named stream could not be bound to its held object: " + NtFile.Error(status, "Inspect EFS stream").Message, true));
                RequireStreamIdentity(stream, identity, ct);
                var format = WindowsCompression.Query(stream); ct.ThrowIfCancellationRequested();
                if (format.State is not (StorageObservationState.Enabled or StorageObservationState.Disabled)) throw Failure("stream-compression", "A named stream's compression state could not be established.");
                var observed = entry with { Compression = format.State };
                if (includeSparseLayouts && WindowsStorageTransforms.HasSparseFlag(stream, ct))
                {
                    using var sparseCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
                    int sparseStatus = NtFile.TryOpen(root, entry.NativeName, NtFile.ReadData | NtFile.ReadAttributes | NtFile.Synchronize,
                        NtFile.ShareAll, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | 0x400000, out var sparse, caseSensitive: true);
                    using (sparse)
                    {
                        if (sparseStatus < 0) throw Failure("sparse-stream-open", "The named sparse stream's allocation could not be inspected: " +
                            NtFile.Error(sparseStatus, "Open held sparse stream").Message);
                        RequireStreamIdentity(sparse, identity, ct);
                        observed = AddSparseLayout(observed, sparse);
                        if (observed.Sparse is null)
                            throw Failure("sparse-stream-changed", "The named stream's sparse state changed during its observation.");
                    }
                }
                result.Add(observed);
            }
        }
        return result.MoveToImmutable();

        WindowsEfsStream AddSparseLayout(WindowsEfsStream entry, SafeFileHandle held)
        {
            if (!includeSparseLayouts) return entry;
            var layout = WindowsStorageTransforms.ReadSparseLayout(held, ct);
            if (layout is null) return entry;
            if (layout.Length != entry.Length)
                throw Failure("sparse-stream-changed", "A sparse stream's length changed during its held enumeration.");
            sparseRanges += layout.Ranges.Length;
            if (sparseRanges > WindowsStorageTransforms.SparseRangeLimit)
                throw Failure("sparse-range-limit", "The complete EFS sparse range inventory exceeds 4,096 retained ranges.");
            return entry with { Sparse = layout };
        }
    }

    private static void RequireStreamIdentity(SafeFileHandle stream, StorageObjectIdentity identity, CancellationToken ct)
    {
        Span<byte> id = stackalloc byte[24];
        ct.ThrowIfCancellationRequested();
        fixed (byte* bytes = id)
            if (!GetInfo(stream, 18 /* FileIdInfo */, bytes, (uint)id.Length))
                throw Failure("stream-identity", "A stream's held identity could not be read.");
        ct.ThrowIfCancellationRequested();
        if (BinaryPrimitives.ReadUInt64LittleEndian(id) != identity.Volume || Convert.ToHexString(id[8..]) != identity.ObjectId ||
            WindowsStorageTransforms.ActualVolumeBinding(stream, false, ct) != identity.VolumeBinding)
            throw Failure("stream-identity", "A stream did not belong to the held reviewed object.");
    }

    internal static bool SameSparseLayouts(ImmutableArray<WindowsEfsStream> before, ImmutableArray<WindowsEfsStream> after)
    {
        var pending = before.Where(entry => entry.Sparse is not null).ToDictionary(entry => entry.NativeName, StringComparer.Ordinal);
        foreach (var entry in after)
        {
            if (entry.Sparse is null) continue;
            if (!pending.Remove(entry.NativeName, out var previous) ||
                !WindowsStorageTransforms.SparseLayout.SameHoles(previous.Sparse, entry.Sparse))
                return false;
        }
        return pending.Count == 0;
    }

    internal static ImmutableArray<WindowsEfsStream> ReadNames(SafeFileHandle root, bool directory, CancellationToken ct)
    {
        for (int attempt = 0, size = 64 * 1024; attempt < 3; attempt++, size *= 4)
        {
            byte[] buffer = new byte[size]; IoStatus io;
            ct.ThrowIfCancellationRequested(); int status;
            fixed (byte* bytes = buffer) status = Query(root, &io, bytes, (uint)buffer.Length, 22 /* FileStreamInformation */);
            ct.ThrowIfCancellationRequested();
            if (status is Overflow or TooSmall or LengthMismatch && attempt < 2) continue;
            if (status != 0 || io.Information > (nuint)buffer.Length)
                throw Failure("stream-inventory", "The complete stream inventory could not be established within two bounded resizes.");
            return Parse(buffer.AsSpan(0, checked((int)io.Information)), directory);
        }
        throw new InvalidOperationException();
    }

    internal static ImmutableArray<WindowsEfsStream> Parse(ReadOnlySpan<byte> bytes, bool directory)
    {
        if (bytes.IsEmpty && directory) return [];
        var result = ImmutableArray.CreateBuilder<WindowsEfsStream>();
        var names = new HashSet<string>(StringComparer.Ordinal); int at = 0, retained = 0; bool main = false;
        while (at <= bytes.Length - 24)
        {
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]), length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 4)..]);
            long size = BinaryPrimitives.ReadInt64LittleEndian(bytes[(at + 8)..]), allocated = BinaryPrimitives.ReadInt64LittleEndian(bytes[(at + 16)..]);
            if (length == 0 || (length & 1) != 0 || length > 65532 || length > bytes.Length - at - 24 || size < 0 || allocated < 0 ||
                next != 0 && (next < 24 + length || next > bytes.Length - at - 24 || (next & 7) != 0)) throw Malformed();
            string raw;
            try { raw = new UnicodeEncoding(false, false, true).GetString(bytes.Slice(at + 24, (int)length)); }
            catch (DecoderFallbackException) { throw Malformed(); }
            if (!raw.StartsWith(':') || !raw.EndsWith(":$DATA", StringComparison.Ordinal) || !names.Add(raw)) throw Malformed();
            if (raw == "::$DATA") main = true;
            else if (raw.Length <= 7 || raw.AsSpan(1, raw.Length - 7).IndexOfAny('\0', ':', '\\') >= 0 || raw.Contains('/')) throw Malformed();
            if (result.Count == EntryLimit || (retained += checked((int)length + 128)) > MetadataLimit)
                throw Failure("stream-inventory-limit", "The complete EFS stream inventory exceeds 1,024 streams or 1 MiB metadata; no entries were omitted for approval.");
            result.Add(new(raw, size, StorageObservationState.Unknown));
            if (next == 0)
            {
                if (bytes.Length - at - 24 - length > 7) throw Malformed();
                if (!directory && !main) throw Malformed();
                return result.ToImmutable();
            }
            at += (int)next;
        }
        throw Malformed();
    }

    private static StorageInspectionException Malformed() => Failure("stream-inventory-shape", "The native stream inventory was incomplete, ambiguous or malformed.");
    private static StorageInspectionException Failure(string code, string message) => new(new(StorageReasonKind.Unknown, code, message, true));
    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryInformationFile")]
    private static partial int Query(SafeFileHandle file, IoStatus* io, void* output, uint length, int informationClass);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetInfo(SafeFileHandle file, int informationClass, void* output, uint length);
}
