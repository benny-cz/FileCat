using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>Remembers protection observed on the actual inputs, including streams a reader opened and then closed.</summary>
internal sealed class ArchiveSpoolProtection
{
    private const int ClearSeen = 1, EfsSeen = 2, UnknownSeen = 4;
    private int _seen;

    internal void Observe(SafeFileHandle file)
    {
        if (!OperatingSystem.IsWindows()) return;
        int state;
        try
        {
            state = File.GetAttributes(file).HasFlag(FileAttributes.Encrypted) ? EfsSeen : ClearSeen;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException)
        {
            state = UnknownSeen;
        }
        Interlocked.Or(ref _seen, state);
    }

    internal void RequireClear()
    {
        if (!OperatingSystem.IsWindows()) return;
        int state = Volatile.Read(ref _seen);
        if ((state & EfsSeen) != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, ArchiveOuterEfs.SpoolUnavailable);
        if (state != ClearSeen)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, ArchiveOuterEfs.SpoolUnknown);
    }
}
