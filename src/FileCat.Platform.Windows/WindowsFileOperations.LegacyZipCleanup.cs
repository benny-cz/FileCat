using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations
{
    string? ILegacyZipBackupRestorer.CleanupLegacyZipBackup(string archive, string backup,
        LegacyZipRollbackFile published, LegacyZipRollbackFile original) =>
        CleanupLegacyZipBackupCore(archive, backup, published, original, beforeCommit: null);

    string? ILegacyZipBackupRestorer.CleanupLegacyZipStage(string stage, string identity, ArchiveObjectVersion? verified,
        string? origin)
    {
        using var parent = LocalIconFileLease.TryOpenPlainDirectory(Path.GetDirectoryName(stage)!);
        if (parent is null) return "The temporary ZIP's folder could not be held.";
        try
        {
            string leaf = Path.GetFileName(stage);
            bool transacted = RequireLegacyZipCleanupMode(parent.LastHandle);
            if (verified is not null)
            {
                var expected = new LegacyZipRollbackFile(verified, origin, Encrypted: false);
                RemoveReviewedZip(parent.LastHandle, leaf, expected, exactOrigin: null, transacted, beforeCommit: null);
            }
            else
            {
                RemoveZip(parent.LastHandle, leaf, transacted, (file, transaction) =>
                {
                    CheckLegacyZipRollbackProtection(file, encrypted: false);
                    if (ProtectedHexFile.Identity(file).ToString() != identity)
                        throw new IOException("A different file now has the temporary ZIP's name.");
                    var mark = ReadZipCleanupOrigin(file, transaction);
                    RequireZipOrigin(mark, origin);
                    return (RandomAccess.GetLength(file), mark);
                }, beforeCommit: null);
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or NotSupportedException)
        {
            return error.Message;
        }
    }

    internal static string? CleanupLegacyZipBackupCore(string archive, string backup,
        LegacyZipRollbackFile published, LegacyZipRollbackFile original, Action? beforeCommit)
    {
        string folder = Path.GetDirectoryName(archive)!;
        if (folder != Path.GetDirectoryName(backup) || published.Encrypted || original.Encrypted ||
            published.Version.Identity == original.Version.Identity)
            return "The ZIP cleanup requires a local folder and two distinct unprotected files.";
        using var parent = LocalIconFileLease.TryOpenPlainDirectory(folder);
        if (parent is null) return "The legacy ZIP folder could not be held.";

        try
        {
            var directory = parent.LastHandle;
            string archiveName = Path.GetFileName(archive), backupName = Path.GetFileName(backup);
            bool transacted = RequireLegacyZipCleanupMode(directory);
            // Use a separate read transaction so the published ZIP remains isolated while the backup's
            // transacted handles are all closed for commit.
            using var publishedScope = transacted ? new WindowsFileTransaction("FileCat ZIP publication verification") : null;
            using var rebuild = OpenZipForCleanup(directory, archiveName, publishedScope, removing: false);
            VerifyZipForCleanup(rebuild, publishedScope, published, exactOrigin: null);
            RemoveReviewedZip(directory, backupName, original, exactOrigin: null, transacted, beforeCommit);
            if (!transacted) WriteThrough(folder);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or NotSupportedException)
        {
            return error.Message;
        }
    }

    private static bool TryDiscardLegacyZipRebuild(SafeFileHandle directory, string leaf, LegacyZipRollbackFile rebuilt,
        ArchiveOriginSnapshot expectedOrigin)
    {
        try
        {
            RemoveReviewedZip(directory, leaf, rebuilt, expectedOrigin, RequireLegacyZipCleanupMode(directory), beforeCommit: null);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static bool RequireLegacyZipCleanupMode(SafeFileHandle directory)
    {
        const uint supportsEncryption = 0x00020000,
            supportsNamedStreams = 0x00040000, supportsExtendedAttributes = 0x00800000;
        var volume = WindowsArchiveNative.Volume(directory, requirePrivateNtfs: false);
        if (SupportsZipTransactions(volume)) return true;
        if ((volume.Flags & (supportsEncryption | supportsNamedStreams | supportsExtendedAttributes)) != 0)
            throw new IOException("Automatic ZIP cleanup requires a transaction on this volume to protect against concurrent changes.");
        return false;
    }

    private static bool SupportsZipTransactions(WindowsArchiveVolume volume) =>
        volume.FileSystem == "NTFS" && (volume.Flags & 0x00200000 /* FILE_SUPPORTS_TRANSACTIONS */) != 0;

    private static SafeFileHandle OpenZipForCleanup(SafeFileHandle directory, string leaf, WindowsFileTransaction? transaction,
        bool removing)
    {
        if (transaction is null) return OpenLegacyZipRollbackFile(directory, leaf, allowDelete: removing);
        uint access = GenericRead | Synchronize | (removing ? GenericWrite | Delete : 0);
        return OpenTransactedZip(directory, leaf, transaction, access);
    }

    private static SafeFileHandle OpenTransactedZip(SafeFileHandle directory, string leaf, WindowsFileTransaction transaction,
        uint access)
    {
        string path = Path.Combine(WindowsFileTransaction.HeldPath(directory), leaf);
        var file = transaction.OpenExisting(path, access, ShareRead, "Open the reviewed ZIP in a transaction");
        try
        {
            using var atName = OpenOrThrow(directory, leaf, ReadAttributes | Synchronize, ShareAll, Open,
                NonDirectoryFile | OpenReparsePoint, "Check the transacted ZIP in the held directory");
            if (ProtectedHexFile.Identity(atName) != ProtectedHexFile.Identity(file))
                throw new IOException("The transacted ZIP is outside the held directory or its name changed.");
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static ArchiveOriginSnapshot VerifyZipForCleanup(SafeFileHandle file, WindowsFileTransaction? transaction,
        LegacyZipRollbackFile expected, ArchiveOriginSnapshot? exactOrigin)
    {
        ArchiveOriginSnapshot mark = VerifyLegacyZipRollbackFile(file, expected, allowMissingOrigin: exactOrigin is not null,
            readOrigin: () => ReadZipCleanupOrigin(file, transaction));
        if (exactOrigin is not null && mark.Digest != exactOrigin.Digest)
            throw new IOException("The retained ZIP's download mark changed during review.");
        return mark;
    }

    private static ArchiveOriginSnapshot ReadZipCleanupOrigin(SafeFileHandle file, WindowsFileTransaction? transaction)
    {
        if (transaction is null) return ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
        CheckLegacyZipRollbackProtection(file, encrypted: false);
        try
        {
            using var zone = transaction.OpenExisting(WindowsFileTransaction.HeldPath(file) + ZoneStream,
                GenericRead | Synchronize, ShareRead | ShareDelete, "Open the reviewed ZIP download mark in its transaction");
            return ReadHeldZipOrigin(file, zone, encrypted: false);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 2 /* ERROR_FILE_NOT_FOUND */)
        {
            return ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
        }
    }

    private static void RemoveReviewedZip(SafeFileHandle directory, string leaf, LegacyZipRollbackFile expected,
        ArchiveOriginSnapshot? exactOrigin, bool transacted, Action? beforeCommit) =>
        RemoveZip(directory, leaf, transacted,
            (file, transaction) => (expected.Version.Length, VerifyZipForCleanup(file, transaction, expected, exactOrigin)), beforeCommit);

    private static void RemoveZip(SafeFileHandle directory, string leaf, bool transacted,
        Func<SafeFileHandle, WindowsFileTransaction?, (long Length, ArchiveOriginSnapshot Origin)> review, Action? beforeCommit)
    {
        using var transaction = transacted ? new WindowsFileTransaction("FileCat ZIP cleanup") : null;
        using (var file = OpenZipForCleanup(directory, leaf, transaction, removing: true))
        {
            var (length, mark) = review(file, transaction);
            WindowsArchiveNative.Regular(file, length, artifact: true, mayRename: true);
            if (transaction is not null)
            {
                if (!WindowsArchiveNative.HasNoExtendedAttributes(file))
                    throw new IOException("The retained ZIP has extended attributes or their absence could not be verified.");
                WindowsArchiveNative.RequireSnapshotStreams(file, length, mark);
            }
            CheckLegacyZipRollbackProtection(file, encrypted: false);
            if (transaction is null) WindowsArchiveNative.DeleteExact(file);
            else WindowsFileTransaction.RemoveOnClose(file);
        }
        if (transaction is not null)
        {
            beforeCommit?.Invoke();
            transaction.Commit("Windows could not confirm the ZIP cleanup commit.");
        }

        int status = TryOpen(directory, leaf, ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, out var remaining);
        using (remaining)
        {
            if (status != NameNotFound) throw new IOException("FileCat could not confirm that the reviewed ZIP name is now vacant.");
        }
    }
}
