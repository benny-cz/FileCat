using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

internal static class ArchiveOuterEfs
{
    internal const string UpdateUnavailable = "FileCat cannot update an EFS-encrypted archive because its temporary update files do not preserve EFS protection.";

    internal static void RequireClear(SafeFileHandle source)
    {
        if (OperatingSystem.IsWindows() && (File.GetAttributes(source) & FileAttributes.Encrypted) != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, UpdateUnavailable);
    }
}
