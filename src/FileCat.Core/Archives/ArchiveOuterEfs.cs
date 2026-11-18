using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

internal static class ArchiveOuterEfs
{
    internal const string UpdateUnavailable = "FileCat cannot update an EFS-encrypted archive because its temporary update files do not preserve EFS protection.";
    internal const string EditUnavailable = "FileCat cannot create an edit session from an EFS-encrypted archive because the working copy would lose EFS protection.";
    internal const string WorkingCopyUnavailable = "FileCat cannot capture an EFS-encrypted working file because its temporary copy would lose EFS protection.";
    internal const string PackUnavailable = "FileCat cannot pack this EFS-protected input because this archive route cannot preserve its EFS protection.";
    internal const string SpoolUnavailable = "This archive uses Windows EFS. FileCat cannot keep its extracted content in a disk cache that lacks EFS protection.";
    internal const string SpoolUnknown = "FileCat cannot verify Windows EFS protection for every archive input, so it cannot use a disk cache for this content.";

    internal static string OutputUnavailable(ArchiveOutputKind method) => method switch
    {
        ArchiveOutputKind.Extraction => "This archive uses Windows EFS. FileCat cannot verify that this extraction will retain EFS protection.",
        ArchiveOutputKind.Drag => "This archive uses Windows EFS. FileCat cannot stage its members for dragging without losing that protection.",
        _ => SpoolUnavailable,
    };

    internal static string OutputUnknown(ArchiveOutputKind method) => method == ArchiveOutputKind.DiskCache
        ? SpoolUnknown
        : "FileCat cannot verify Windows EFS protection for every archive input, so it cannot prepare this output.";

    internal static void RequireClear(SafeFileHandle source, string reason = UpdateUnavailable)
    {
        if (OperatingSystem.IsWindows()) RequireClear(File.GetAttributes(source), reason);
    }

    internal static void RequireClear(FileAttributes attributes, string reason)
    {
        if ((attributes & FileAttributes.Encrypted) != 0) throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, reason);
    }
}
