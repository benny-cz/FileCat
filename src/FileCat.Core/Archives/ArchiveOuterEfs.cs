using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

internal static class ArchiveOuterEfs
{
    internal const string UpdateUnavailable = "FileCat cannot update an EFS-encrypted archive because its temporary update files do not preserve EFS protection.";
    internal const string EditUnavailable = "FileCat cannot create an edit session from an EFS-encrypted archive because the working copy would lose EFS protection.";
    internal const string WorkingCopyUnavailable = "FileCat cannot capture an EFS-encrypted working file because its temporary copy would lose EFS protection.";
    internal const string PackUnavailable = "FileCat cannot pack this EFS-protected input because this archive route cannot preserve its EFS protection.";

    internal static void RequireClear(SafeFileHandle source, string reason = UpdateUnavailable)
    {
        if (OperatingSystem.IsWindows()) RequireClear(File.GetAttributes(source), reason);
    }

    internal static void RequireClear(FileAttributes attributes, string reason)
    {
        if ((attributes & FileAttributes.Encrypted) != 0) throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, reason);
    }
}
