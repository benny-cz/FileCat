using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>rename(2) and its no-replace forms on Linux and macOS, returning the error number (0 when done).</summary>
internal static partial class UnixRename
{
    private const int LinuxNoReplace = 1; // RENAME_NOREPLACE
    private const uint MacExclusive = 4; // RENAME_EXCL
    private const int AtFdCwd = -100;

    /// <summary>Renames only when <paramref name="destination"/> is free, atomically; an unsupported error when the system cannot.</summary>
    public static int NoReplace(string source, string destination)
    {
        try
        {
            if (OperatingSystem.IsLinux()) return RenameAt2(AtFdCwd, source, AtFdCwd, destination, LinuxNoReplace) == 0 ? 0 : Marshal.GetLastPInvokeError();
            if (OperatingSystem.IsMacOS()) return RenameXNp(source, destination, MacExclusive) == 0 ? 0 : Marshal.GetLastPInvokeError();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        return OperatingSystem.IsMacOS() ? 45 : 38; // ENOTSUP / ENOSYS: check, then rename
    }

    /// <summary>Renames, replacing a file at <paramref name="destination"/> (atomically: the old one stays until the new one is there).</summary>
    public static int Replace(string source, string destination) => Rename(source, destination) == 0 ? 0 : Marshal.GetLastPInvokeError();

    /// <summary>Whether the file system or the system cannot rename without replacing (and a check before the rename must do).</summary>
    public static bool Unsupported(int errno) => OperatingSystem.IsMacOS() ? errno is 22 or 45 or 78 or 102 : errno is 22 or 38 or 95;

    /// <summary>The exception .NET itself would throw for the error, with the error number as its HResult (see <see cref="Jobs.ErrorText.Classify"/>).</summary>
    public static Exception Failure(int errno, string source, string destination)
    {
        string message = Marshal.GetPInvokeErrorMessage(errno);
        return errno switch
        {
            2 => new FileNotFoundException($"{message}: {source}", source) { HResult = errno },
            1 or 13 => new UnauthorizedAccessException($"{message}: {source}") { HResult = errno },
            17 => new IOException($"An item named \"{Path.GetFileName(destination)}\" already exists.", errno),
            _ => new IOException($"{message}: {source}", errno),
        };
    }

    [LibraryImport("libc", EntryPoint = "rename", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Rename(string source, string destination);

    [LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameAt2(int sourceDirectory, string source, int destinationDirectory, string destination, int flags);

    [LibraryImport("libc", EntryPoint = "renamex_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameXNp(string source, string destination, uint flags);
}
