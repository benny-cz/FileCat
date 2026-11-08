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
    internal void RequireSelectionRoot() { lock (Gate) { EnsureOpen(); RequireCapacity(); VerifyNamespace(); } }
    internal static void ValidateSelectionLeaf(string leaf) => ValidateLeaf(leaf);
    internal void CaptureSourceDirectory(ArchiveCapturedInputs inputs, int id, string leaf, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs); ValidateLeaf(leaf);
        inputs.CaptureDirectory(id, token => ObserveSourceDirectory(leaf, token), ct);
    }

    internal ArchiveDirectoryObservation ObserveSelectionDirectory(string leaf, CancellationToken ct) => ObserveSourceDirectory(leaf, ct);
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
                string removal = RemovalDigest(metadata, basic.ChangeTime);
                return new(identity.ToString(), Identity.ToString(), leaf, Convert.ToHexString(hash.GetHashAndReset()), origin,
                    modified, 0x1ed, removal);
            }
        }
    }

    // No namespace enumeration: this guard remains linear in the number of moved children.
    internal (string Digest, string StableDigest) ObserveRemovalDirectory(string leaf, string expectedIdentity, CancellationToken ct)
    {
        lock (Gate)
        {
            ct.ThrowIfCancellationRequested(); EnsureOpen(); VerifyNamespace(); ValidateLeaf(leaf);
            using var held = OpenOrThrow(Handle, leaf, ReadAttributes | Synchronize | 0x20000, ShareAll,
                NtFile.Open, DirectoryFile | OpenReparsePoint, "Review source folder removal metadata");
            var identity = ProtectedHexFile.Identity(held);
            if (identity.ToString() != expectedIdentity) throw ChangedDirectory();
            var before = Observe(); var after = Observe();
            VerifyNamespace(); RequireDirectoryName(leaf, identity);
            if (before != after) throw ChangedDirectory();
            return after;

            (string, string) Observe()
            {
                ct.ThrowIfCancellationRequested(); uint links = WindowsArchiveNative.SourceDirectory(held);
                var basic = GetBasic(held); var security = WindowsArchiveNative.ReadOuterSecurity(held);
                var origin = ReadDirectoryOrigin(leaf, identity, ct);
                var metadata = new WindowsArchiveOuterMetadata(identity, basic, links, security, origin);
                string current = RemovalDigest(metadata, basic.ChangeTime); basic.LastWriteTime = 0;
                return (current, new WindowsArchiveOuterMetadata(identity, basic, links, security, origin).Digest);
            }
        }
    }

    private static string RemovalDigest(WindowsArchiveOuterMetadata metadata, long changeTime)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> changed = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(changed, changeTime);
        hash.AppendData(Convert.FromHexString(metadata.Digest)); hash.AppendData(changed);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal void RemoveCapturedDirectory(string leaf, ArchiveDirectoryObservation expected, string currentRemovalDigest, CancellationToken ct)
    {
        lock (Gate)
        {
            ct.ThrowIfCancellationRequested(); EnsureOpen(); VerifyNamespace(); ValidateLeaf(leaf);
            if (expected.ParentIdentity != Identity.ToString() || expected.LeafName != leaf || expected.RemovalMetadataDigest is null) throw ChangedDirectory();
            using var held = OpenOrThrow(Handle, leaf, ListDirectory | ReadAttributes | Delete | Synchronize | 0x20000,
                ShareRead, NtFile.Open, DirectoryFile | OpenReparsePoint, "Remove the captured empty source folder");
            var identity = ProtectedHexFile.Identity(held);
            if (identity.ToString() != expected.Version.Identity) throw ChangedDirectory();
            uint links = WindowsArchiveNative.SourceDirectory(held);
            var basic = GetBasic(held);
            var origin = ReadDirectoryOrigin(leaf, identity, ct);
            var metadata = new WindowsArchiveOuterMetadata(identity, basic, links, WindowsArchiveNative.ReadOuterSecurity(held), origin);
            if (RemovalDigest(metadata, basic.ChangeTime) != currentRemovalDigest || origin.Digest != expected.Origin.Digest) throw ChangedDirectory();
            WindowsArchiveNative.RequireCreationStreams(held, 0, origin, directory: true);
            WindowsArchiveNative.SourceDirectoryNamespace(held, ct, (_, _, _) => throw new IOException("The source folder still contains skipped, changed or newly added items."));
            VerifyNamespace(); RequireDirectoryName(leaf, identity); ct.ThrowIfCancellationRequested();
            WindowsArchiveNative.DeleteExact(held); // The kernel refuses a nonempty directory; no recursive deletion.
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
