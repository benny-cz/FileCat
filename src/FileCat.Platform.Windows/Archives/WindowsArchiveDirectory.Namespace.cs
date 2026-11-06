using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Archives;

internal static unsafe partial class WindowsArchiveNative
{
    private readonly record struct NamespaceDigest(ulong A, ulong B, ulong C, ulong D) : IComparable<NamespaceDigest>
    {
        public int CompareTo(NamespaceDigest other)
        {
            int result = A.CompareTo(other.A); if (result != 0) return result;
            result = B.CompareTo(other.B); if (result != 0) return result;
            result = C.CompareTo(other.C); return result != 0 ? result : D.CompareTo(other.D);
        }
    }

    // Stream only one native batch; retain fixed-size entry digests, not names or directory handles per child.
    // Sorting makes the result independent of kernel enumeration order. This is immediate membership evidence,
    // not an atomic recursive tree snapshot or authority to delete a source tree.
    internal static string SourceDirectoryNamespace(SafeFileHandle held, CancellationToken ct, Action<string?, ulong, uint>? visit = null)
    {
        var entries = new List<NamespaceDigest>(); byte[] buffer = new byte[64 << 10]; long nameBytes = 0, nativeBytes = 0;
        int records = 0; bool restart = true;
        using var entryHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> digest = stackalloc byte[32]; Span<byte> fields = stackalloc byte[8];
        fixed (byte* raw = buffer)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested(); IoStatus io = default;
                int status = QueryDirectory(held, 0, 0, 0, &io, raw, (uint)buffer.Length,
                    37 /* FileIdBothDirectoryInformation */, 0, 0, restart ? (byte)1 : (byte)0);
                restart = false;
                if (status == unchecked((int)0x80000006) /* STATUS_NO_MORE_FILES */)
                { if (io.Information != 0) throw NamespaceInvalid(); break; }
                if (status != 0 || io.Information <= 0 || io.Information > buffer.Length) throw NamespaceInvalid();
                ArchiveWriteBudget.Reserve(nativeBytes, (long)io.Information, ArchiveWritePolicy.MaxMetadataBytes); nativeBytes += (long)io.Information;
                int used = checked((int)io.Information), offset = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested(); var record = buffer.AsSpan(offset, used - offset);
                    if (++records > ArchiveWritePolicy.MaxMembers + 2) throw ArchiveWriteBudget.Limit();
                    if (record.Length < 104) throw NamespaceInvalid();
                    uint next = BinaryPrimitives.ReadUInt32LittleEndian(record), length = BinaryPrimitives.ReadUInt32LittleEndian(record[60..]);
                    if (length == 0 || (length & 1) != 0 || length > 2 * ArchiveWritePolicy.MaxNameChars || length > record.Length - 104) throw NamespaceInvalid();
                    var name = record.Slice(104, (int)length);
                    if (!name.SequenceEqual(".\0"u8) && !name.SequenceEqual(".\0.\0"u8))
                    {
                        if (entries.Count == ArchiveWritePolicy.MaxMembers || length > ArchiveWritePolicy.MaxMetadataBytes - nameBytes) throw ArchiveWriteBudget.Limit();
                        nameBytes += length;
                        if (visit is not null)
                        {
                            string? leaf;
                            try { leaf = StrictNamespaceUnicode.GetString(name); }
                            catch (DecoderFallbackException) { leaf = null; }
                            visit(leaf, BinaryPrimitives.ReadUInt64LittleEndian(record[96..]), BinaryPrimitives.ReadUInt32LittleEndian(record[56..]));
                        }
                        // File ID, exact UTF-16 spelling and object kind; exclude changing access timestamps/indexes.
                        entryHash.AppendData(record.Slice(96, 8));
                        BinaryPrimitives.WriteUInt32LittleEndian(fields, length);
                        BinaryPrimitives.WriteUInt32LittleEndian(fields[4..], BinaryPrimitives.ReadUInt32LittleEndian(record[56..]) & 0x410 /* directory/reparse */);
                        entryHash.AppendData(fields); entryHash.AppendData(name); entryHash.GetHashAndReset(digest);
                        entries.Add(new(BinaryPrimitives.ReadUInt64LittleEndian(digest), BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]),
                            BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]), BinaryPrimitives.ReadUInt64LittleEndian(digest[24..])));
                    }
                    if (next == 0) break;
                    if ((next & 7) != 0 || next < 104 + length || next > record.Length - 104) throw NamespaceInvalid();
                    offset = checked(offset + (int)next);
                }
            }
        }
        entries.Sort(); ct.ThrowIfCancellationRequested();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[8]; BinaryPrimitives.WriteInt32LittleEndian(header, 1); BinaryPrimitives.WriteInt32LittleEndian(header[4..], entries.Count);
        hash.AppendData(header);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested(); BinaryPrimitives.WriteUInt64LittleEndian(digest, entry.A); BinaryPrimitives.WriteUInt64LittleEndian(digest[8..], entry.B);
            BinaryPrimitives.WriteUInt64LittleEndian(digest[16..], entry.C); BinaryPrimitives.WriteUInt64LittleEndian(digest[24..], entry.D); hash.AppendData(digest);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static ArchiveWriteException NamespaceInvalid() => new(ArchiveWriteReason.SourceChanged, "The source folder's complete immediate namespace could not be reviewed.");
    private static readonly UnicodeEncoding StrictNamespaceUnicode = new(false, false, true);
    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryDirectoryFile")]
    private static partial int QueryDirectory(SafeFileHandle directory, nint @event, nint apc, nint context,
        IoStatus* status, void* information, uint length, uint informationClass, byte single, nint name, byte restart);
}
