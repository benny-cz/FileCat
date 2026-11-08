using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Fixed-stream origin propagation to the still-open, newly created private editor file.</summary>
internal static class WindowsArchiveEditOrigin
{
    internal static void Copy(FileStream working, ArchiveOriginSnapshot origin, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (origin.Format != ArchiveOriginFormat.WindowsZoneIdentifier || origin.State == ArchiveOriginState.Unavailable)
            throw new IOException("The archive download mark cannot be preserved on the private edit copy.");
        if (origin.State == ArchiveOriginState.Absent) return;
        var handle = working.SafeFileHandle; var identity = ProtectedHexFile.Identity(handle);
        WindowsArchiveNative.Regular(handle, EditSessionStore.MaxMemberBytes, artifact: true, mayRename: false);
        using var parent = LocalIconFileLease.TryOpenDirectory(Path.GetDirectoryName(working.Name)!, LeaseLinks.Refuse, removable: true)
            ?? throw new IOException("The private edit directory cannot be pinned safely.");
        string leaf = Path.GetFileName(working.Name);
        using var named = OpenOrThrow(parent.LastHandle, leaf, ReadAttributes | Synchronize, ShareAll, NtFile.Open,
            NonDirectoryFile | OpenReparsePoint, "Check private edit origin owner");
        if (ProtectedHexFile.Identity(named) != identity || IsReparsePoint(GetBasic(named).FileAttributes))
            throw new IOException("The private edit name no longer identifies its held file.");
        var before = GetBasic(handle);
        using (var stream = OpenOrThrow(parent.LastHandle, leaf + ":Zone.Identifier", GenericRead | GenericWrite | Synchronize,
            ShareRead | ShareDelete, Create, NonDirectoryFile | OpenReparsePoint, "Create private edit origin"))
        {
            if (ProtectedHexFile.Identity(stream) != identity) throw new IOException("The private edit origin belongs to a different file.");
            RandomAccess.Write(stream, origin.Bytes, 0); WindowsArchiveNative.Flush(stream);
            byte[] bytes = new byte[origin.Bytes.Length]; int offset = 0;
            while (offset < bytes.Length)
            {
                int read = RandomAccess.Read(stream, bytes.AsSpan(offset), offset);
                if (read <= 0) throw new IOException("The private edit origin ended before complete readback.");
                offset += read;
            }
            if (RandomAccess.GetLength(stream) != bytes.Length || !bytes.AsSpan().SequenceEqual(origin.Bytes))
                throw new IOException("The private edit origin changed during exact readback.");
        }
        SetBasic(handle, new BasicInfo { LastWriteTime = before.LastWriteTime });
        WindowsArchiveNative.Flush(handle); ct.ThrowIfCancellationRequested();
    }
}
