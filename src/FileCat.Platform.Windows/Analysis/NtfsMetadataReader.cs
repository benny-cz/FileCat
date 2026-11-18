using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Analysis;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Analysis;

internal sealed record NtfsMetadataRecord(string ObjectId, string ParentId, string NameUtf16, uint Attributes);
internal sealed record NtfsMetadataPage(ulong NextReference, ImmutableArray<NtfsMetadataRecord> Records);

/// <summary>Only the two fixed NTFS metadata controls are available on this held read-only volume.</summary>
internal sealed partial class NtfsMetadataReader : IDisposable
{
    private const uint QueryVolume = 0x00090064, Enumerate = 0x000900B3;
    private const int PageBytes = 64 * 1024;
    private readonly SafeFileHandle _volume;
    private readonly byte[] _page = new byte[PageBytes];
    private readonly string _rootId;
    private ulong _next;

    private NtfsMetadataReader(SafeFileHandle volume, string rootId)
    {
        _volume = volume;
        _rootId = rootId;
    }

    internal static unsafe NtfsMetadataReader Open(NtfsMetadataVolume scope, CancellationToken cancellation)
    {
        using var root = scope.ReopenRoot(cancellation);
        var volume = NtfsMetadataVolume.Native.CreateFile(scope.GuidPath[..^1], 0x80000000 /* GENERIC_READ */, 3,
            0, 3 /* OPEN_EXISTING */, 0, 0);
        try
        {
            if (volume.IsInvalid) throw new UnauthorizedAccessException("The approved NTFS metadata volume could not be read.");
            Span<byte> evidence = stackalloc byte[128];
            if (Control(volume, QueryVolume, [], evidence, out uint size) != 0 || size < 96 || size > evidence.Length ||
                BinaryPrimitives.ReadUInt64LittleEndian(evidence).ToString("X16") != scope.Serial ||
                NtfsMetadataVolume.Native.Capture(scope.GuidPath, root) != scope)
                throw new AnalysisSourceChangedException();
            cancellation.ThrowIfCancellationRequested();
            return new(volume, scope.RootFileId);
        }
        catch { volume.Dispose(); throw; }
    }

    internal NtfsMetadataPage? ReadNext(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Span<byte> request = stackalloc byte[24]; // MFT_ENUM_DATA_V0
        BinaryPrimitives.WriteUInt64LittleEndian(request, _next);
        request[8..16].Clear();
        BinaryPrimitives.WriteInt64LittleEndian(request[16..], long.MaxValue);
        int error = Control(_volume, Enumerate, request, _page, out uint used);
        cancellation.ThrowIfCancellationRequested();
        if (error == 38 /* ERROR_HANDLE_EOF */) return null;
        if (error != 0 || used > _page.Length) throw new IOException("The NTFS metadata query did not complete.");
        var page = ParsePage(_page.AsSpan(0, (int)used), _next, _rootId);
        _next = page.NextReference;
        return page;
    }

    internal static NtfsMetadataPage ParsePage(ReadOnlySpan<byte> data, ulong previous, string rootId)
    {
        if (data.Length <= sizeof(ulong) || data.Length > PageBytes) throw InvalidPage();
        ulong next = BinaryPrimitives.ReadUInt64LittleEndian(data);
        if (next <= previous) throw InvalidPage();
        var records = ImmutableArray.CreateBuilder<NtfsMetadataRecord>();
        int offset = sizeof(ulong), seen = 0;
        while (offset < data.Length)
        {
            var record = data[offset..];
            if (record.Length < 64 || ++seen > NtfsMetadataProtocol.MaximumRecordBatch)
                throw InvalidPage();
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(record);
            if (length < 64 || length > record.Length || (length & 7) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) != 2 ||
                BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) != 0)
                throw InvalidPage();
            ulong file = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
            ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(record[16..]);
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(record[52..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[56..]);
            int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[58..]);
            if (file == 0 || parent == 0 || nameOffset < 60 || (nameOffset & 1) != 0 ||
                nameLength is 0 or > NtfsMetadataProtocol.MaximumNameBytes || (nameLength & 1) != 0 || nameOffset + nameLength > length)
                throw InvalidPage();
            string fileId = file.ToString("X32");
            if (fileId != rootId)
            {
                var name = record.Slice(nameOffset, nameLength);
                if (!NtfsMetadataProtocol.ValidRawName(name)) throw InvalidPage();
                records.Add(new(fileId, parent.ToString("X32"), Convert.ToBase64String(name), attributes));
            }
            offset += (int)length;
        }
        return new(next, records.ToImmutable());
    }

    private static InvalidDataException InvalidPage() => new("Windows returned an invalid or unsupported NTFS metadata page.");

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_page);
        _volume.Dispose();
    }

    private static unsafe int Control(SafeFileHandle volume, uint code, ReadOnlySpan<byte> input, Span<byte> result, out uint used)
    {
        fixed (byte* from = input, to = result)
            return DeviceIoControl(volume, code, from, (uint)input.Length, to, (uint)result.Length, out used, 0)
                ? 0 : Marshal.GetLastPInvokeError();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle volume, uint code, byte* input, uint inputLength,
        byte* output, uint outputLength, out uint returned, nint overlapped);
}
