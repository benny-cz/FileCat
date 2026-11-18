using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Remembers protection observed on the actual inputs, including streams a reader opened and then closed.</summary>
internal sealed class OutputEfsObservation
{
    internal enum State { Clear, Encrypted, Unknown }

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

    internal State Status
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return State.Clear;
            int state = Volatile.Read(ref _seen);
            if ((state & EfsSeen) != 0) return State.Encrypted;
            return state == ClearSeen ? State.Clear : State.Unknown;
        }
    }
}
