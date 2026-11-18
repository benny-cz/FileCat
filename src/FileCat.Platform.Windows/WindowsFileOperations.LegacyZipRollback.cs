using System.ComponentModel;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations
{
    LegacyZipRollbackResult ILegacyZipBackupRestorer.RollbackLegacyZip(string archive, string backup, string stage,
        LegacyZipRollbackFile published, LegacyZipRollbackFile original) =>
        RollbackLegacyZipCore(archive, backup, stage, published, original, afterRetire: null);

    internal static LegacyZipRollbackResult RollbackLegacyZipCore(string archive, string backup, string stage,
        LegacyZipRollbackFile published, LegacyZipRollbackFile original, Action? afterRetire)
    {
        string folder = Path.GetDirectoryName(archive)!;
        if (folder != Path.GetDirectoryName(backup) || folder != Path.GetDirectoryName(stage) ||
            published.Encrypted || published.Version.Identity == original.Version.Identity)
            return new(false, false, "The legacy ZIP rollback does not have a valid local folder and two distinct files.");
        using var parent = LocalIconFileLease.TryOpenPlainDirectory(folder);
        if (parent is null) return new(false, false, "The legacy ZIP folder could not be held.");
        string archiveName = Path.GetFileName(archive);
        try
        {
            using var rebuild = OpenLegacyZipRollbackFile(parent.LastHandle, archiveName);
            using var retained = OpenLegacyZipRollbackFile(parent.LastHandle, Path.GetFileName(backup));
            var rebuildMark = VerifyLegacyZipRollbackFile(rebuild, published, allowMissingOrigin: true);
            VerifyLegacyZipRollbackFile(retained, original, allowMissingOrigin: false);
            NtFile.Rename(rebuild, parent.LastHandle, Path.GetFileName(stage), replace: false);
            try
            {
                afterRetire?.Invoke();
                CheckLegacyZipRollbackProtection(retained, original.Encrypted);
                NtFile.Rename(retained, parent.LastHandle, archiveName, replace: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
            {
                try { NtFile.Rename(rebuild, parent.LastHandle, archiveName, replace: false); }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    WriteThrough(folder);
                    return new(false, true, "The original could not return to its name. The rebuilt ZIP remains at its temporary name. " + error.Message);
                }
                WriteThrough(folder);
                return new(false, false, "The original could not return to its name. The rebuilt ZIP returned to the archive name. " + error.Message);
            }

            rebuild.Dispose();
            bool kept = !TryDiscardLegacyZipRebuild(parent.LastHandle, Path.GetFileName(stage), published, rebuildMark);
            WriteThrough(folder);
            return new(true, kept);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new(false, false, error.Message);
        }
    }

    private static SafeFileHandle OpenLegacyZipRollbackFile(SafeFileHandle parent, string name, bool allowDelete = true) =>
        OpenOrThrow(parent, name, ReadData | ReadAttributes | Synchronize | (allowDelete ? Delete : 0), ShareRead, Open,
            NonDirectoryFile | OpenReparsePoint, "Open held ZIP file");

    private static ArchiveOriginSnapshot VerifyLegacyZipRollbackFile(SafeFileHandle file, LegacyZipRollbackFile expected,
        bool allowMissingOrigin, Func<ArchiveOriginSnapshot>? readOrigin = null, Action? checkpoint = null)
    {
        WindowsArchiveNative.Regular(file, expected.Version.Length, artifact: false, mayRename: true);
        if (ProtectedHexFile.Identity(file).ToString() != expected.Version.Identity ||
            !DigestMatches(file, expected.Version.Sha256, expected.Version.Length, CancellationToken.None,
                () =>
                {
                    checkpoint?.Invoke();
                    CheckLegacyZipRollbackProtection(file, expected.Encrypted);
                }))
            throw new IOException("A ZIP object's identity or bytes changed before recovery or cleanup.");
        var mark = readOrigin?.Invoke() ?? ReadLegacyZipRollbackOrigin(file, expected.Encrypted);
        RequireZipOrigin(mark, expected.Origin, allowMissingOrigin);
        return mark;
    }

    private static void RequireZipOrigin(ArchiveOriginSnapshot mark, string? expected, bool allowMissingOrigin = false)
    {
        string? text = null;
        if (mark.State == ArchiveOriginState.Present)
        {
            using var reader = new StreamReader(new MemoryStream(mark.Bytes.ToArray(), writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        if ((!allowMissingOrigin || text is not null) && !string.Equals(text, expected, StringComparison.Ordinal))
            throw new IOException("A ZIP object's download mark changed before recovery or cleanup.");
    }

    private static ArchiveOriginSnapshot ReadLegacyZipRollbackOrigin(SafeFileHandle file, bool encrypted)
    {
        CheckLegacyZipRollbackProtection(file, encrypted);
        const ArchiveOriginFormat format = ArchiveOriginFormat.WindowsZoneIdentifier;
        int status = TryOpen(file, ZoneStream, GenericRead | Synchronize, ShareRead | ShareDelete, Open, NonDirectoryFile, out var zone);
        using (zone)
        {
            if (status == NameNotFound) return ArchiveOriginSnapshot.Absent(format);
            if (status < 0) throw Error(status, "Open held ZIP download mark");
            return ReadHeldZipOrigin(file, zone, encrypted);
        }
    }

    private static ArchiveOriginSnapshot ReadHeldZipOrigin(SafeFileHandle file, SafeFileHandle zone, bool encrypted)
    {
        if (ProtectedHexFile.Identity(zone) != ProtectedHexFile.Identity(file))
            throw new IOException("The download mark does not belong to the held ZIP.");
        long size = RandomAccess.GetLength(zone);
        if (size < 0 || size > MaxOriginMarkBytes) throw new IOException("The ZIP download mark exceeds the inspection limit.");
        var bytes = new byte[(int)size];
        int offset = 0;
        while (offset < bytes.Length)
        {
            CheckLegacyZipRollbackProtection(file, encrypted);
            CheckLegacyZipRollbackProtection(zone, encrypted);
            int count = RandomAccess.Read(zone, bytes.AsSpan(offset), offset);
            if (count == 0) throw new IOException("The download mark changed during review.");
            offset += count;
        }
        CheckLegacyZipRollbackProtection(file, encrypted);
        CheckLegacyZipRollbackProtection(zone, encrypted);
        if (RandomAccess.GetLength(zone) != size) throw new IOException("The download mark changed during review.");
        return ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes);
    }

    private static void CheckLegacyZipRollbackProtection(SafeFileHandle file, bool encrypted)
    {
        var attributes = (FileAttributes)GetBasic(file).FileAttributes;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            attributes.HasFlag(FileAttributes.Encrypted) != encrypted)
            throw new IOException("A held ZIP object's protection changed before recovery or cleanup.");
    }
}
