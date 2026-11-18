using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations : ILegacyZipInPlacePublisher
{
    SafeFileHandle? ILegacyZipInPlacePublisher.PinLegacyZipForPublication(SafeFileHandle file)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var pin = ReopenLegacyZipPin(file, ReadAttributes | Synchronize, ShareAll, OpenReparsePoint);
        if (!pin.IsInvalid) return pin;
        pin.Dispose();
        return null;
    }

    LegacyZipInPlaceResult? ILegacyZipInPlacePublisher.TryPublishLegacyZipInPlace(string archive, string stage, string backup,
        LegacyZipRollbackFile original, SafeFileHandle originalPin, LegacyZipRollbackFile rebuilt, SafeFileHandle stagePin, Action checkpoint) =>
        PublishLegacyZipInPlaceCore(archive, stage, backup, original, originalPin, rebuilt, stagePin, checkpoint, beforeCommit: null);

    internal static LegacyZipInPlaceResult? PublishLegacyZipInPlaceCore(string archive, string stage, string backup,
        LegacyZipRollbackFile original, SafeFileHandle originalPin, LegacyZipRollbackFile rebuilt, SafeFileHandle stagePin,
        Action checkpoint, Action? beforeCommit)
    {
        string folder = Path.GetDirectoryName(archive)!;
        if (folder != Path.GetDirectoryName(stage) || folder != Path.GetDirectoryName(backup))
            throw new IOException("The ZIP update requires its original, staged output and snapshot in the same folder.");
        using var parent = LocalIconFileLease.TryOpenPlainDirectory(folder);
        if (parent is null) return null;
        var directory = parent.LastHandle;
        bool snapshotPossible = false, commitAttempted = false;
        SafeFileHandle? snapshotPin = null;
        try
        {
            if (!SupportsZipTransactions(WindowsArchiveNative.Volume(directory, requirePrivateNtfs: false))) return null;
            string archiveName = Path.GetFileName(archive), stageName = Path.GetFileName(stage);
            using var transaction = new WindowsFileTransaction("FileCat legacy ZIP publication");
            var published = new LegacyZipRollbackFile(new(original.Version.Identity, rebuilt.Version.Length, rebuilt.Version.Sha256),
                rebuilt.Origin, Encrypted: false);
            LegacyZipRollbackFile snapshot;
            BasicInfo originalInfo;
            byte[] originalSecurity;
            ArchiveOriginSnapshot publishedOrigin;
            using (var target = OpenTransactedZip(directory, archiveName, transaction, GenericRead | GenericWrite | Synchronize))
            using (var input = OpenTransactedZip(directory, stageName, transaction, GenericRead | Synchronize))
            {
                if (ProtectedHexFile.Identity(target) != ProtectedHexFile.Identity(originalPin) ||
                    ProtectedHexFile.Identity(input) != ProtectedHexFile.Identity(stagePin))
                    throw new IOException("The archive or rebuilt ZIP no longer identifies the reviewed file.");
                // An in-place publication would also change every other hard link. Keep those updates on the
                // namespace-replacement route until that route has its own qualified publication protocol.
                if (WindowsArchiveNative.Regular(target, original.Version.Length, artifact: false, mayRename: true) != 1)
                    return null;
                var originalOrigin = VerifyTransactedPublicationInput(target, transaction, original, checkpoint);
                var rebuiltOrigin = VerifyTransactedPublicationInput(input, transaction, rebuilt, checkpoint);
                WindowsArchiveNative.RequireSnapshotStreams(input, rebuilt.Version.Length, rebuiltOrigin);
                originalInfo = GetBasic(target);
                originalSecurity = WindowsArchiveNative.ReadOuterSecurity(target);

                checkpoint();
                // Its exact name is durable in the caller's journal before this creation. The owner-only DACL
                // is installed by the creation call, before any archive data can be written to the snapshot.
                snapshotPossible = true;
                snapshot = CreateLegacyZipSnapshot(directory, Path.GetFileName(backup), target, original, originalOrigin, checkpoint,
                    out snapshotPin);

                CopyLegacyZipPublicationBytes(input, target, rebuilt.Version.Length, checkpoint);
                publishedOrigin = string.Equals(original.Origin, rebuilt.Origin, StringComparison.Ordinal) ? originalOrigin : rebuiltOrigin;
                if (publishedOrigin.Digest != originalOrigin.Digest)
                    WriteTransactedLegacyZipOrigin(target, transaction, originalOrigin, publishedOrigin);
                WindowsArchiveNative.Flush(target);
                RequirePreservedLegacyZipMetadata(target, published.Version.Length, originalInfo, originalSecurity);
            }

            // The read pin remains alive through commit and adoption of the normal reader. Writers and new
            // hard links stay excluded across that interval, including after the private writer is closed.
            using (var commitPin = OpenTransactedZip(directory, archiveName, transaction, GenericRead | Synchronize))
            {
                var actual = VerifyTransactedPublicationInput(commitPin, transaction, published, checkpoint);
                if (actual.Digest != publishedOrigin.Digest) throw new IOException("The verified ZIP download mark changed before commit.");
                RequirePreservedLegacyZipMetadata(commitPin, published.Version.Length, originalInfo, originalSecurity);
                beforeCommit?.Invoke();
                checkpoint();
                commitAttempted = true;
                transaction.Commit("Windows could not confirm publication of the ZIP update.");
                using var adopted = OpenOrThrow(directory, archiveName, GenericRead | Synchronize, ShareRead, Open,
                    NonDirectoryFile | OpenReparsePoint, "Verify the published ZIP");
                var committedMark = VerifyLegacyZipRollbackFile(adopted, published, allowMissingOrigin: false);
                if (committedMark.Digest != publishedOrigin.Digest)
                    throw new IOException("The published ZIP download mark changed during verification.");
                RequirePreservedLegacyZipMetadata(adopted, published.Version.Length, originalInfo, originalSecurity);
            }

            string? cleanupReview = CleanupLegacyZipBackupCore(archive, stage, published, rebuilt, beforeCommit: null)
                ?? CleanupLegacyZipBackupCore(archive, backup, published, snapshot, beforeCommit: null);
            return new(true, cleanupReview);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException or NotSupportedException)
        {
            string? review = commitAttempted
                ? "FileCat could not verify the ZIP publication. The original may have been updated."
                : snapshotPossible ? "The ZIP transaction stopped before commit. The original snapshot and rebuild require review." : null;
            return new(false, review, error is Win32Exception ? new IOException(error.Message, error) : error);
        }
        finally { snapshotPin?.Dispose(); }
    }

    private static ArchiveOriginSnapshot VerifyTransactedPublicationInput(SafeFileHandle file, WindowsFileTransaction transaction,
        LegacyZipRollbackFile expected, Action checkpoint) =>
        VerifyLegacyZipRollbackFile(file, expected, allowMissingOrigin: false,
            readOrigin: () => ReadZipCleanupOrigin(file, transaction), checkpoint: checkpoint);

    private static LegacyZipRollbackFile CreateLegacyZipSnapshot(SafeFileHandle directory, string leaf, SafeFileHandle source,
        LegacyZipRollbackFile original, ArchiveOriginSnapshot origin, Action checkpoint, out SafeFileHandle snapshotPin)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string owner = identity.User?.Value ?? throw new UnauthorizedAccessException("The archive snapshot owner is unavailable.");
        using var snapshot = OpenOrThrow(directory, leaf, GenericRead | GenericWrite | Synchronize, ShareRead, Create,
            NonDirectoryFile | OpenReparsePoint, "Create the private ZIP snapshot", (uint)FileAttributes.Normal,
            WindowsArchiveNative.PrivateSecurityDescriptor(owner));
        WindowsArchiveNative.RequirePrivateSecurity(snapshot, owner);
        CopyLegacyZipPublicationBytes(source, snapshot, original.Version.Length, checkpoint);
        if (origin.State == ArchiveOriginState.Present)
        {
            using var zone = OpenOrThrow(snapshot, ZoneStream, GenericWrite | ReadAttributes | Synchronize, ShareRead | ShareDelete, Create,
                NonDirectoryFile | OpenReparsePoint, "Keep the ZIP snapshot download mark");
            WriteZipPublicationOrigin(snapshot, zone, origin);
        }
        WindowsArchiveNative.Flush(snapshot);
        var expected = new LegacyZipRollbackFile(new(ProtectedHexFile.Identity(snapshot).ToString(), original.Version.Length,
            original.Version.Sha256), original.Origin, Encrypted: false);
        var observed = VerifyLegacyZipRollbackFile(snapshot, expected, allowMissingOrigin: false, checkpoint: checkpoint);
        if (observed.Digest != origin.Digest) throw new IOException("The private ZIP snapshot download mark changed.");
        WindowsArchiveNative.RequireSnapshotStreams(snapshot, expected.Version.Length, observed);
        WindowsArchiveNative.RequirePrivateSecurity(snapshot, owner);
        snapshotPin = ReopenLegacyZipPin(snapshot, ReadAttributes | Synchronize, ShareAll, OpenReparsePoint);
        if (snapshotPin.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return expected;
    }

    private static void WriteTransactedLegacyZipOrigin(SafeFileHandle target, WindowsFileTransaction transaction,
        ArchiveOriginSnapshot oldOrigin, ArchiveOriginSnapshot newOrigin)
    {
        if (newOrigin.State != ArchiveOriginState.Present) throw new IOException("The rebuilt ZIP would remove its download mark.");
        string path = WindowsFileTransaction.HeldPath(target) + ZoneStream;
        const uint access = GenericWrite | ReadAttributes | Synchronize, share = ShareRead | ShareDelete;
        using var zone = oldOrigin.State == ArchiveOriginState.Absent
            ? transaction.CreateFile(path, access, share, "Create the published ZIP download mark")
            : transaction.OpenExisting(path, access, share, "Update the published ZIP download mark");
        WriteZipPublicationOrigin(target, zone, newOrigin);
    }

    private static void WriteZipPublicationOrigin(SafeFileHandle file, SafeFileHandle zone, ArchiveOriginSnapshot origin)
    {
        if (ProtectedHexFile.Identity(file) != ProtectedHexFile.Identity(zone))
            throw new IOException("The download mark does not belong to the reviewed ZIP.");
        CheckLegacyZipRollbackProtection(file, encrypted: false);
        CheckLegacyZipRollbackProtection(zone, encrypted: false);
        RandomAccess.Write(zone, origin.Bytes, 0);
        RandomAccess.SetLength(zone, origin.Bytes.Length);
        WindowsArchiveNative.Flush(zone);
    }

    private static void CopyLegacyZipPublicationBytes(SafeFileHandle source, SafeFileHandle destination, long length, Action checkpoint)
    {
        CheckLegacyZipRollbackProtection(destination, encrypted: false);
        RandomAccess.SetLength(destination, 0);
        var buffer = new byte[128 * 1024];
        for (long offset = 0; offset < length;)
        {
            checkpoint();
            CheckLegacyZipRollbackProtection(source, encrypted: false);
            int count = RandomAccess.Read(source, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
            CheckLegacyZipRollbackProtection(source, encrypted: false);
            if (count == 0) throw new IOException("The reviewed ZIP length changed during publication.");
            CheckLegacyZipRollbackProtection(destination, encrypted: false);
            RandomAccess.Write(destination, buffer.AsSpan(0, count), offset);
            offset += count;
        }
    }

    private static void RequirePreservedLegacyZipMetadata(SafeFileHandle file, long length, BasicInfo original, byte[] security)
    {
        var current = GetBasic(file);
        if (WindowsArchiveNative.Regular(file, length, artifact: false, mayRename: true) != 1 ||
            current.CreationTime != original.CreationTime ||
            (current.FileAttributes & ~(uint)FileAttributes.Archive) != (original.FileAttributes & ~(uint)FileAttributes.Archive) ||
            !WindowsArchiveNative.ReadOuterSecurity(file).AsSpan().SequenceEqual(security))
            throw new IOException("The ZIP update could not verify that the original file metadata was preserved.");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "ReOpenFile", SetLastError = true)]
    private static partial SafeFileHandle ReopenLegacyZipPin(SafeFileHandle file, uint access, uint share, uint flags);
}
