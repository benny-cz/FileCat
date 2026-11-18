using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Remembers protection observed on the actual inputs, including streams a reader opened and then closed.</summary>
internal sealed partial class OutputEfsObservation
{
    internal enum State { Clear, Encrypted, Unknown }

    private const int ClearSeen = 1, EfsSeen = 2, UnknownSeen = 4;
    internal const int MaxContributors = 1024, MaxContributorBytes = 1024 * 1024;
    private readonly record struct Contributor(FileIdentity Identity, string Revision);
    private readonly object _contributorLock = new();
    private readonly HashSet<Contributor> _contributors = [];
    private int _contributorBytes;
    private int _seen;

    /// <summary>Records an actual contributor once per identity and revision before a reader can consume it.</summary>
    internal void ObserveOpened(SafeFileHandle file, string? knownRevision = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        int state = ReadProtection(file);
        string? revision = knownRevision ?? SourceChangeToken.FromHandle(file);
        if (revision is null || !ReadFileId(file, 18 /* FileIdInfo */, out var info, 24) || (info.Low | info.High) == 0 ||
            !Remember(new(info.Volume, $"{info.High:X16}{info.Low:X16}"), revision)) state |= UnknownSeen;
        Interlocked.Or(ref _seen, state);
    }

    private bool Remember(FileIdentity identity, string revision)
    {
        var contribution = new Contributor(identity, revision);
        lock (_contributorLock)
        {
            if (_contributors.Contains(contribution)) return true;
            long bytes = 64L + (identity.FileId.Length + (long)revision.Length) * sizeof(char);
            if (_contributors.Count >= MaxContributors || bytes > MaxContributorBytes - _contributorBytes) return false;
            _contributors.Add(contribution);
            _contributorBytes += (int)bytes;
            return true;
        }
    }

    internal void Observe(SafeFileHandle file)
    {
        if (OperatingSystem.IsWindows()) Interlocked.Or(ref _seen, ReadProtection(file));
    }

    private static int ReadProtection(SafeFileHandle file)
    {
        try
        {
            return File.GetAttributes(file).HasFlag(FileAttributes.Encrypted) ? EfsSeen : ClearSeen;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException)
        {
            return UnknownSeen;
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileId { public ulong Volume, Low, High; }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFileId(SafeFileHandle file, int infoClass, out NativeFileId output, uint length);
}
