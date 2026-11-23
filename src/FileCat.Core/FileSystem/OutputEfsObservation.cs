using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Remembers protection observed on the actual inputs, including streams a reader opened and then closed.</summary>
internal sealed partial class OutputEfsObservation
{
    internal enum State { Clear, Encrypted, Unknown }

    private const int ClearSeen = 1, EfsSeen = 2, UnknownSeen = 4;
    internal const int MaxContributors = 1024, MaxContributorBytes = 1024 * 1024;
    internal readonly record struct Contributor(FileIdentity Identity, string Revision);
    internal sealed record Receipt(State Protection, ImmutableArray<Contributor> Contributors, bool HasUnknownInput);
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
        bool identified = ReadFileId(file, 18 /* FileIdInfo */, out var info, 24) && (info.Low | info.High) != 0;
        ObserveContributor(identified ? ReadIdentity(info) : null, revision, state);
    }

    internal void ObserveOpenedLinux(SafeFileHandle file)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        ObserveContributor(UnixFiles.Stat(file)?.Identity, SourceChangeToken.FromHandle(file), ClearSeen);
    }

    internal void ObserveOpenedMac(SafeFileHandle directory)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        ObserveContributor(UnixFiles.Stat(directory)?.Identity, SourceChangeToken.FromHandle(directory), ClearSeen);
    }

    private void ObserveContributor(FileIdentity? identity, string? revision, int state)
    {
        lock (_contributorLock)
        {
            if (identity is not { } known || revision is null || !Remember(new(known, revision)))
                state |= UnknownSeen;
            Interlocked.Or(ref _seen, state);
        }
    }

    private static FileIdentity ReadIdentity(NativeFileId info)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, info.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], info.High);
        return new(info.Volume, Convert.ToHexString(bytes));
    }

    private bool Remember(Contributor contribution)
    {
        long bytes = 64L + (contribution.Identity.FileId.Length + (long)contribution.Revision.Length) * sizeof(char);
        if (bytes > MaxContributorBytes) return false;
        if (_contributors.Contains(contribution)) return true;
        if (_contributors.Count >= MaxContributors || bytes > MaxContributorBytes - _contributorBytes) return false;
        _contributors.Add(contribution);
        _contributorBytes += (int)bytes;
        return true;
    }

    internal void Include(Receipt receipt)
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (_contributorLock)
        {
            int state = receipt.Protection switch { State.Clear => ClearSeen, State.Encrypted => EfsSeen, _ => UnknownSeen };
            if (receipt.HasUnknownInput) state |= UnknownSeen;
            foreach (var input in receipt.Contributors)
            {
                if (Remember(input)) continue;
                state |= UnknownSeen;
                break;
            }
            Interlocked.Or(ref _seen, state);
        }
    }

    internal Receipt Freeze()
    {
        lock (_contributorLock)
        {
            int seen = Volatile.Read(ref _seen);
            if (OperatingSystem.IsWindows() && _contributors.Count == 0) seen |= UnknownSeen;
            var state = ProtectionOf(seen);
            return new(state, _contributors.ToImmutableArray(), state == State.Unknown || (seen & UnknownSeen) != 0);
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

    internal State Status => ProtectionOf(Volatile.Read(ref _seen));

    private static State ProtectionOf(int seen)
    {
        if (!OperatingSystem.IsWindows()) return State.Clear;
        if ((seen & EfsSeen) != 0) return State.Encrypted;
        return seen == ClearSeen ? State.Clear : State.Unknown;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileId { public ulong Volume, Low, High; }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFileId(SafeFileHandle file, int infoClass, out NativeFileId output, uint length);
}
