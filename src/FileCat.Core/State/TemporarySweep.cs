using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.State;

/// <summary>
/// How FileCat removes what earlier sessions left in its temporary folder (I645): staged drags, pasted virtual files and
/// spooled archive members. Nothing here goes through a reparse point or symbolic link. What a link leads to is never
/// listed, given other attributes or deleted: a stale link where a sweep removes things is removed itself, and a temporary
/// folder, or a staging folder in it, that is itself a link is not swept at all. Removing changes no attributes either:
/// on Windows a read-only item is deleted as it is, through a handle to the item itself; on Linux and macOS removing a name
/// needs no right on the file.
/// <para>
/// The checks are made on paths: something that can change the temporary folder while a sweep runs could still swap a
/// folder it checked for a link before the sweep lists it. That is why an elevated or root FileCat runs no sweep
/// (<c>AppServices.SweepTemporaryFolders</c>): its temporary folder is the ordinary account's, which any program of that
/// account can change. An ordinary FileCat sweeps only what its own account could remove anyway.
/// </para>
/// </summary>
internal static unsafe partial class TemporarySweep
{
    /// <summary>
    /// Removes the direct entries of <c>tempRoot/folder</c> made more than <paramref name="age"/> ago: the folders FileCat
    /// staged there, and links, which are removed themselves. Files there are not FileCat's and stay. Nothing is swept when
    /// the temporary folder or the staging folder is itself a link.
    /// </summary>
    internal static void RemoveStaleFolders(string tempRoot, string folder, TimeSpan age)
    {
        string root = Path.Combine(tempRoot, folder);
        if (!IsPlainFolder(tempRoot) || !IsPlainFolder(root)) return;
        foreach (var entry in Entries(root))
        {
            try
            {
                // The entry as the listing describes it: a link's own attributes and times, never those of what it leads to.
                bool link = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                if (!link && (entry.Attributes & FileAttributes.Directory) == 0) continue;
                if (DateTime.UtcNow - entry.CreationTimeUtc <= age) continue;
                if (link) RemoveItself(entry.FullName);
                else RemoveTree(entry.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>A folder's entries as its listing describes them, a link as a link; none when it cannot be listed.</summary>
    internal static List<FileSystemInfo> Entries(string folder)
    {
        try { return new DirectoryInfo(folder).EnumerateFileSystemInfos().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
    }

    /// <summary>Whether the path is a folder and not itself a reparse point or symbolic link; false when missing or unreadable.</summary>
    internal static bool IsPlainFolder(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// Removes a folder FileCat made and everything in it, without going through a link: a link inside is removed itself,
    /// every folder is emptied before it goes, and each item is checked again just before it is handled. What cannot be
    /// removed stays. A path that is a file or a link is removed itself.
    /// </summary>
    internal static void RemoveTree(string path)
    {
        // Depth first on a stack of its own, so that no depth of folders can exhaust the thread's.
        var pending = new Stack<(string Path, bool Emptied)>();
        pending.Push((path, false));
        while (pending.Count > 0)
        {
            var (current, emptied) = pending.Pop();
            if (emptied || !IsPlainFolder(current))
            {
                RemoveItself(current);
                continue;
            }
            pending.Push((current, true));
            foreach (var entry in Entries(current)) pending.Push((entry.FullName, false));
        }
    }

    /// <summary>
    /// Removes the item at the path itself, never what it leads to: a link as a link, a file even when it is read-only,
    /// a folder only when it is empty. False when it stays.
    /// </summary>
    internal static bool RemoveItself(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return RemoveItselfOnWindows(Path.GetFullPath(path));
            // unlink and rmdir act on the name itself: neither follows a link that ends the path.
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                Directory.Delete(path);
            else File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private const uint DeleteAccess = 0x00010000, ReadAttributes = 0x80, WriteAttributes = 0x100;
    private const uint OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const int FileBasicInfo = 0, FileDispositionInfo = 4, FileDispositionInfoEx = 21;
    private const uint DispositionDelete = 0x1, DispositionPosixSemantics = 0x2, DispositionIgnoreReadOnly = 0x10;

    /// <summary>
    /// Windows: the item is opened as itself (a link is not followed) and deleted through that handle, so whatever its
    /// path came to name in the meantime, only this item goes. POSIX semantics take its name at once; a read-only item
    /// is deleted without its attributes being changed.
    /// </summary>
    private static bool RemoveItselfOnWindows(string full)
    {
        string native = full.Length >= 248 && !full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\" + full : full;
        using var handle = CreateFile(native, DeleteAccess | ReadAttributes | WriteAttributes,
            FileShare.ReadWrite | FileShare.Delete, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid) return false;
        uint flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadOnly;
        if (SetFileInformationByHandle(handle, FileDispositionInfoEx, &flags, sizeof(uint))) return true;
        // Error 1, 50 or 87: no extended disposition on this Windows (before 1809) or volume (FAT, say). The classic one
        // refuses a read-only item, so its read-only flag goes first: on this item, which the handle holds as itself.
        if (Marshal.GetLastPInvokeError() is not (1 or 50 or 87)) return false;
        BasicInfo basic;
        if (!GetFileInformationByHandleEx(handle, FileBasicInfo, &basic, (uint)sizeof(BasicInfo))) return false;
        if ((basic.FileAttributes & (uint)FileAttributes.ReadOnly) != 0)
        {
            basic.FileAttributes &= ~(uint)FileAttributes.ReadOnly;
            if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
            basic.CreationTime = basic.LastAccessTime = basic.LastWriteTime = basic.ChangeTime = 0; // zero: unchanged
            if (!SetFileInformationByHandle(handle, FileBasicInfo, &basic, (uint)sizeof(BasicInfo))) return false;
        }
        byte delete = 1;
        return SetFileInformationByHandle(handle, FileDispositionInfo, &delete, 1);
    }

    /// <summary>FILE_BASIC_INFO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, FileShare share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, void* information, uint size);
}
