using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveDirectory
{
    internal void CaptureSourceDirectory(ArchiveCapturedInputs inputs, int id, string leaf, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs); ValidateLeaf(leaf);
        inputs.CaptureDirectory(id, token => ObserveSourceDirectory(leaf, token), ct);
    }

    private ArchiveDirectoryObservation ObserveSourceDirectory(string leaf, CancellationToken ct)
    {
        lock (Gate)
        {
            ct.ThrowIfCancellationRequested(); EnsureOpen(); RequireCapacity(); VerifyNamespace();
            using var held = OpenOrThrow(Handle, leaf, ListDirectory | ReadAttributes | Synchronize | 0x20000 /* READ_CONTROL */,
                ShareAll, NtFile.Open, DirectoryFile | OpenReparsePoint, "Capture selected folder");
            var identity = ProtectedHexFile.Identity(held);
            var first = Observe(); var second = Observe();
            VerifyNamespace(); RequireDirectoryName(leaf, identity);
            if (!first.Matches(second)) throw ChangedDirectory();
            ct.ThrowIfCancellationRequested(); return second;

            ArchiveDirectoryObservation Observe()
            {
                ct.ThrowIfCancellationRequested(); uint links = WindowsArchiveNative.SourceDirectory(held);
                var basic = GetBasic(held); byte[] security = WindowsArchiveNative.ReadOuterSecurity(held);
                var origin = ReadDirectoryOrigin(leaf, identity, ct);
                var metadata = new WindowsArchiveOuterMetadata(identity, basic, links, security, origin);
                // A directory's change time also detects native changes whose last-write time was restored.
                Span<byte> changed = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(changed, basic.ChangeTime);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(Convert.FromHexString(metadata.Digest)); hash.AppendData(changed);
                hash.AppendData(Convert.FromHexString(WindowsArchiveNative.SourceDirectoryNamespace(held, ct)));
                ArchiveTimestamp modified;
                try { modified = new DateTimeOffset(DateTime.FromFileTimeUtc(basic.LastWriteTime), TimeSpan.Zero); }
                catch (ArgumentOutOfRangeException)
                { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The folder's timestamp is outside the supported archive range."); }
                return new(identity.ToString(), Identity.ToString(), leaf, Convert.ToHexString(hash.GetHashAndReset()), origin,
                    modified, 0x1ed);
            }
        }
    }

    private void RequireDirectoryName(string leaf, FileIdentity expected)
    {
        using var named = OpenOrThrow(Handle, leaf, ReadAttributes | Synchronize, ShareAll, NtFile.Open,
            DirectoryFile | OpenReparsePoint, "Review selected folder name");
        WindowsArchiveNative.SourceDirectory(named);
        if (ProtectedHexFile.Identity(named) != expected) throw ChangedDirectory();
    }

    private ArchiveOriginSnapshot ReadDirectoryOrigin(string leaf, FileIdentity identity, CancellationToken ct)
    {
        RequireDirectoryName(leaf, identity);
        int status = TryOpen(Handle, leaf + ":Zone.Identifier", GenericRead | Synchronize, ShareAll, NtFile.Open,
            OpenReparsePoint, out var stream);
        using (stream)
        {
            if (status == NameNotFound) { RequireDirectoryName(leaf, identity); return ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier); }
            if (status < 0) throw Error(status, "Read selected folder origin");
            WindowsArchiveNative.DirectoryOriginStream(stream);
            if (ProtectedHexFile.Identity(stream) != identity) throw ChangedDirectory();
            long length = RandomAccess.GetLength(stream);
            if (length < 0 || length > ArchiveOriginSnapshot.MaxBytes) throw ArchiveWriteBudget.Limit();
            byte[] bytes = new byte[(int)length]; int offset = 0;
            while (offset < bytes.Length)
            {
                ct.ThrowIfCancellationRequested(); int read = RandomAccess.Read(stream, bytes.AsSpan(offset), offset);
                if (read == 0) throw ChangedDirectory(); offset += read;
            }
            RequireDirectoryName(leaf, identity); ct.ThrowIfCancellationRequested();
            if (RandomAccess.GetLength(stream) != length) throw ChangedDirectory();
            return ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes);
        }
    }
    private static ArchiveWriteException ChangedDirectory() => new(ArchiveWriteReason.SourceChanged, "The captured folder's identity or metadata changed.");
}
