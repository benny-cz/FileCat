using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

internal static class ArchiveOuterEfs
{
    internal const string UpdateUnavailable = "FileCat cannot update an EFS-encrypted archive because its temporary update files do not preserve EFS protection.";
    internal const string UpdateStageUnavailable = "The rebuilt ZIP is EFS-protected, so FileCat cannot publish the update.";
    internal const string UpdateOutputUnavailable = "Windows reported EFS protection on the published ZIP during verification.";
    internal const string EditUnavailable = "FileCat cannot create an edit session from an EFS-encrypted archive because the working copy would lose EFS protection.";
    internal const string WorkingCopyUnavailable = "FileCat cannot capture an EFS-encrypted working file because its temporary copy would lose EFS protection.";
    internal const string PackUnavailable = "FileCat cannot pack this EFS-protected input because this archive route cannot preserve its EFS protection.";
    internal const string SpoolUnavailable = "This archive uses Windows EFS. FileCat cannot keep its extracted content in a disk cache that lacks EFS protection.";
    internal const string SpoolUnknown = "FileCat cannot verify Windows EFS protection for every archive input, so it cannot use a disk cache for this content.";

    internal static string OutputUnavailable(ContentOutputKind method) => method switch
    {
        ContentOutputKind.Extraction => "This archive uses Windows EFS. FileCat cannot verify that this extraction will retain EFS protection.",
        ContentOutputKind.Drag => "This archive uses Windows EFS. FileCat cannot stage its members for dragging without losing that protection. Use a reviewed extraction that preserves EFS.",
        ContentOutputKind.RemoteUpload => "This archive uses Windows EFS. FileCat cannot upload its members without losing that protection.",
        _ => SpoolUnavailable,
    };

    private static string OutputUnknown(ContentOutputKind method) => method == ContentOutputKind.DiskCache
        ? SpoolUnknown
        : "FileCat cannot verify Windows EFS protection for every archive input, so it cannot prepare this output.";

    internal static void RequireClearOutput(OutputEfsObservation protection, ContentOutputKind method) =>
        RequireClearOutput(protection.Status, method);

    internal static void RequireClearOutput(OutputEfsObservation.State protection, ContentOutputKind method)
    {
        switch (protection)
        {
            case OutputEfsObservation.State.Encrypted:
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, OutputUnavailable(method));
            case OutputEfsObservation.State.Unknown:
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, OutputUnknown(method));
        }
    }

    internal static void RequireClear(SafeFileHandle source, string reason = UpdateUnavailable)
    {
        if (OperatingSystem.IsWindows()) RequireClear(File.GetAttributes(source), reason);
    }

    internal static void RequireClear(FileAttributes attributes, string reason)
    {
        if ((attributes & FileAttributes.Encrypted) != 0) throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, reason);
    }
}
